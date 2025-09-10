using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application;

public interface IAppDbContext
{
    DbSet<Organization> Organizations { get; }
    DbSet<OrgMember> OrgMembers { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}