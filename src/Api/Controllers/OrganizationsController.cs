using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Application;
using Domain.Entities;
using Microsoft.Data.Sqlite;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrganizationsController : ControllerBase
{
    private readonly IAppDbContext _db;
    public OrganizationsController(IAppDbContext db) => _db = db;

    // ====== 유틸: 현재 사용자 ======
    private (string? sub, string? name) GetMe()
    {
        // sub(고정), name(표시용)
        var sub  = User.FindFirst("sub")?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

        var name = User.FindFirst("name")?.Value ?? User.Identity?.Name;
        return (sub, name);
    }

    private async Task<bool> IsOwnerAsync(Guid orgId, string mySub, CancellationToken ct)
    {
        // OrgMembers에 Owner로 있거나, (레거시) Organization.CreatedBy == mySub
        var ownerRow = await _db.OrgMembers
            .AsNoTracking()
            .AnyAsync(m => m.OrgId == orgId && m.UserSub == mySub && m.Role == "Owner", ct);

        if (ownerRow) return true;

        var legacyOwner = await _db.Organizations
            .AsNoTracking()
            .Where(o => o.Id == orgId)
            .Select(o => o.CreatedBy)
            .FirstOrDefaultAsync(ct);

        return legacyOwner != null && legacyOwner == mySub;
    }

    // ====== 기본 목록 (기존 호환) ======
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<List<Organization>>> Get(CancellationToken ct)
        => await _db.Organizations.AsNoTracking()
              .OrderByDescending(o => o.CreatedAt)
              .ToListAsync(ct);

    // ====== 검색/정렬/페이지 ======
    // GET /api/organizations/search?query=&sort=name|createdAt&order=asc|desc&page=1&pageSize=10
    public record Paged<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

    [HttpGet("search")]
    [AllowAnonymous]
    public async Task<ActionResult<Paged<Organization>>> Search(
        [FromQuery] string? query,
        [FromQuery] string sort = "createdAt",
        [FromQuery] string order = "desc",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var q = _db.Organizations.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q2 = query.Trim();
            q = q.Where(o => o.Name.Contains(q2));
        }

        // 정렬
        var desc = order.Equals("desc", StringComparison.OrdinalIgnoreCase);
        q = (sort.ToLower()) switch
        {
            "name"      => desc ? q.OrderByDescending(o => o.Name)      : q.OrderBy(o => o.Name),
            _           => desc ? q.OrderByDescending(o => o.CreatedAt) : q.OrderBy(o => o.CreatedAt)
        };

        var total = await q.CountAsync(ct);
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return new Paged<Organization>(items, total, page, pageSize);
    }

    // ====== 단건 조회 ======
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<Organization>> GetById(Guid id, CancellationToken ct)
    {
        var org = await _db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
        return org is null ? NotFound() : org;
    }

    // ====== 내 조직(Owner) ======
    [HttpGet("mine")]
    [Authorize(Policy = "ApiScope")]
    public async Task<ActionResult<List<Organization>>> GetMine(CancellationToken ct)
    {
        var (sub, _) = GetMe();
        if (string.IsNullOrWhiteSpace(sub)) return Forbid();

        // Owner 멤버십 기준 + 레거시 보정
        var ownerOrgIds = await _db.OrgMembers.AsNoTracking()
            .Where(m => m.UserSub == sub && m.Role == "Owner")
            .Select(m => m.OrgId)
            .ToListAsync(ct);

        var q = _db.Organizations.AsNoTracking().Where(o => ownerOrgIds.Contains(o.Id) || o.CreatedBy == sub);
        return await q.OrderByDescending(o => o.CreatedAt).ToListAsync(ct);
    }

    // ====== 생성 (Owner 자동 멤버 추가) ======
    public record CreateOrgDto([Required] string Name);

    [HttpPost]
    [Authorize(Policy = "ApiScope")]
    public async Task<ActionResult<Organization>> Create([FromBody] CreateOrgDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var (sub, name) = GetMe();

        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = sub,
            CreatedByName = name
        };

        _db.Organizations.Add(org);

        try
        {
            await _db.SaveChangesAsync(ct);

            // 생성자 Owner 멤버십 없으면 추가
            if (!string.IsNullOrWhiteSpace(sub))
            {
                var exists = await _db.OrgMembers.AsNoTracking()
                    .AnyAsync(m => m.OrgId == org.Id && m.UserSub == sub, ct);

                if (!exists)
                {
                    _db.OrgMembers.Add(new OrgMember
                    {
                        OrgId = org.Id,
                        UserSub = sub!,
                        UserName = name ?? "Unknown",
                        Role = "Owner",
                        CreatedAt = DateTime.UtcNow
                    });
                    await _db.SaveChangesAsync(ct);
                }
            }
        }
        catch (DbUpdateException ex)
        {
            var root = ex.GetBaseException();
            var detail = root.Message;
            if (root is SqliteException se)
                detail = $"SQLite[{se.SqliteErrorCode}/{se.SqliteExtendedErrorCode}]: {se.Message}";
            return Problem(title: "DB update failed", detail: detail, statusCode: StatusCodes.Status500InternalServerError);
        }

        return CreatedAtAction(nameof(GetById), new { id = org.Id }, org);
    }

    // ====== 수정 ======
    public record UpdateOrgDto([Required] string Name);

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "ApiScope")]
    public async Task<ActionResult<Organization>> Update(Guid id, [FromBody] UpdateOrgDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var (sub, _) = GetMe();
        if (string.IsNullOrWhiteSpace(sub)) return Forbid();

        var org = await _db.Organizations.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (org is null) return NotFound();

        if (!await IsOwnerAsync(id, sub, ct)) return Forbid();

        org.Name = dto.Name;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            var root = ex.GetBaseException();
            var detail = root.Message;
            if (root is SqliteException se)
                detail = $"SQLite[{se.SqliteErrorCode}/{se.SqliteExtendedErrorCode}]: {se.Message}";
            return Problem(title: "DB update failed", detail: detail, statusCode: StatusCodes.Status500InternalServerError);
        }

        return Ok(org);
    }

    // ====== 삭제 ======
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "ApiScope")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var (sub, _) = GetMe();
        if (string.IsNullOrWhiteSpace(sub)) return Forbid();

        var org = await _db.Organizations.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (org is null) return NotFound();

        if (!await IsOwnerAsync(id, sub, ct)) return Forbid();

        _db.Organizations.Remove(org);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ====== 멤버 관리 ======
    public record AddMemberDto([Required] string UserSub, [Required] string UserName, [Required] string Role);
    public record UpdateMemberDto([Required] string Role);

    [HttpGet("{id:guid}/members")]
    [Authorize(Policy = "ApiScope")]
    public async Task<ActionResult<List<OrgMember>>> GetMembers(Guid id, CancellationToken ct)
        => await _db.OrgMembers.AsNoTracking()
                .Where(m => m.OrgId == id)
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync(ct);

    [HttpPost("{id:guid}/members")]
    [Authorize(Policy = "ApiScope")]
    public async Task<ActionResult<OrgMember>> AddMember(Guid id, [FromBody] AddMemberDto dto, CancellationToken ct)
    {
        var (sub, _) = GetMe();
        if (string.IsNullOrWhiteSpace(sub)) return Forbid();
        if (!await IsOwnerAsync(id, sub, ct)) return Forbid();

        if (dto.Role != "Owner" && dto.Role != "Member")
            return ValidationProblem("Role must be 'Owner' or 'Member'.");

        var exists = await _db.OrgMembers.AnyAsync(m => m.OrgId == id && m.UserSub == dto.UserSub, ct);
        if (exists) return Conflict("Member already exists.");

        var m = new OrgMember
        {
            OrgId = id,
            UserSub = dto.UserSub,
            UserName = dto.UserName,
            Role = dto.Role,
            CreatedAt = DateTime.UtcNow
        };
        _db.OrgMembers.Add(m);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetMembers), new { id }, m);
    }

    [HttpPut("{id:guid}/members/{memberId:guid}")]
    [Authorize(Policy = "ApiScope")]
    public async Task<IActionResult> UpdateMember(Guid id, Guid memberId, [FromBody] UpdateMemberDto dto, CancellationToken ct)
    {
        var (sub, _) = GetMe();
        if (string.IsNullOrWhiteSpace(sub)) return Forbid();
        if (!await IsOwnerAsync(id, sub, ct)) return Forbid();

        if (dto.Role != "Owner" && dto.Role != "Member")
            return ValidationProblem("Role must be 'Owner' or 'Member'.");

        var m = await _db.OrgMembers.FirstOrDefaultAsync(x => x.Id == memberId && x.OrgId == id, ct);
        if (m is null) return NotFound();

        // 마지막 Owner 방지
        if (m.Role == "Owner" && dto.Role != "Owner")
        {
            var ownerCount = await _db.OrgMembers.CountAsync(x => x.OrgId == id && x.Role == "Owner", ct);
            if (ownerCount <= 1) return Problem(statusCode: 409, title: "Cannot demote the last Owner.");
        }

        m.Role = dto.Role;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}/members/{memberId:guid}")]
    [Authorize(Policy = "ApiScope")]
    public async Task<IActionResult> DeleteMember(Guid id, Guid memberId, CancellationToken ct)
    {
        var (sub, _) = GetMe();
        if (string.IsNullOrWhiteSpace(sub)) return Forbid();
        if (!await IsOwnerAsync(id, sub, ct)) return Forbid();

        var m = await _db.OrgMembers.FirstOrDefaultAsync(x => x.Id == memberId && x.OrgId == id, ct);
        if (m is null) return NotFound();

        // 마지막 Owner 삭제 방지
        if (m.Role == "Owner")
        {
            var ownerCount = await _db.OrgMembers.CountAsync(x => x.OrgId == id && x.Role == "Owner", ct);
            if (ownerCount <= 1) return Problem(statusCode: 409, title: "Cannot delete the last Owner.");
        }

        _db.OrgMembers.Remove(m);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}