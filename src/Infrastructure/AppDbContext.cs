using Application;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure;

public class AppDbContext : DbContext, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrgMember>   OrgMembers    => Set<OrgMember>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Organization>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).IsRequired();
            b.Property(x => x.CreatedAt).IsRequired();
            b.Property(x => x.CreatedBy).HasMaxLength(200);
            b.Property(x => x.CreatedByName).HasMaxLength(200);
        });

        modelBuilder.Entity<OrgMember>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.UserSub).IsRequired().HasMaxLength(200);
            b.Property(x => x.UserName).IsRequired().HasMaxLength(200);
            b.Property(x => x.Role).IsRequired().HasMaxLength(20);

            b.HasIndex(x => new { x.OrgId, x.UserSub }).IsUnique();

            b.HasOne(x => x.Organization)
             .WithMany() // 필요하면 Organization에 ICollection<OrgMember> Members 추가해도 됨
             .HasForeignKey(x => x.OrgId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}