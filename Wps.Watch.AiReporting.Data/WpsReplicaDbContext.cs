using Microsoft.EntityFrameworkCore;
using Wps.Watch.AiReporting.Data.Entities;

namespace Wps.Watch.AiReporting.Data;

/// <summary>
/// Read-only EF context against the wpsWatch operational database mirror.
/// All queries should use AsNoTracking (configured globally); this context is
/// for SELECT-only access. Eventual production target is a geo-replica with
/// ApplicationIntent=ReadOnly; the entity set deliberately excludes sensitive
/// columns per v5 S-V5-MUST-28.
/// </summary>
public class WpsReplicaDbContext : DbContext
{
    public WpsReplicaDbContext(DbContextOptions<WpsReplicaDbContext> options)
        : base(options)
    {
    }

    public DbSet<Region> Regions => Set<Region>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Deployment> Deployments => Set<Deployment>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Issue> Issues => Set<Issue>();
    public DbSet<OrganizationUserRole> OrganizationUserRoles => Set<OrganizationUserRole>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Region>().ToTable(nameof(Region)).HasKey(r => r.RegionId);
        modelBuilder.Entity<Organization>().ToTable(nameof(Organization)).HasKey(o => o.OrganizationId);
        modelBuilder.Entity<Site>(e =>
        {
            e.ToTable(nameof(Site));
            e.HasKey(s => s.SiteId);
        });
        modelBuilder.Entity<Deployment>(e =>
        {
            e.ToTable(nameof(Deployment));
            e.HasKey(d => d.DeploymentId);
        });
        modelBuilder.Entity<Device>(e =>
        {
            e.ToTable(nameof(Device));
            e.HasKey(d => d.DeviceId);
        });
        modelBuilder.Entity<Issue>(e =>
        {
            e.ToTable(nameof(Issue));
            e.HasKey(i => i.IssueId);
        });
        modelBuilder.Entity<OrganizationUserRole>(e =>
        {
            e.ToTable(nameof(OrganizationUserRole));
            e.HasKey(our => our.Id);
        });

        base.OnModelCreating(modelBuilder);
    }
}
