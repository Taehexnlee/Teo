// src/Api/Controllers/SecurePingController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SecurePingController : ControllerBase
{
    [HttpGet]
    [Authorize] // 임시: 토큰 필요
    public IActionResult Get() => Ok(new { ok = true, user = User.Identity?.Name ?? "(no name)" });
}