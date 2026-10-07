using Aqr.Core.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aqr.Core.Data;

public sealed class AqrDbContext(DbContextOptions<AqrDbContext> options) : DbContext(options)
{
    public const string PeriodStart = "ValidFrom";
    public const string PeriodEnd = "ValidTo";

    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Region> Regions => Set<Region>();
    public DbSet<VmFamily> VmFamilies => Set<VmFamily>();
    public DbSet<SubscriptionQuota> SubscriptionQuotas => Set<SubscriptionQuota>();
    public DbSet<QuotaGroup> QuotaGroups => Set<QuotaGroup>();
    public DbSet<QuotaGroupMember> QuotaGroupMembers => Set<QuotaGroupMember>();
    public DbSet<GroupQuota> GroupQuotas => Set<GroupQuota>();
    public DbSet<GroupAllocation> GroupAllocations => Set<GroupAllocation>();
    public DbSet<FamilyZoneAccess> FamilyZoneAccess => Set<FamilyZoneAccess>();
    public DbSet<SkuRestriction> SkuRestrictions => Set<SkuRestriction>();
    public DbSet<ZoneMapping> ZoneMappings => Set<ZoneMapping>();
    public DbSet<AccessGrant> AccessGrants => Set<AccessGrant>();
    public DbSet<SyncRun> SyncRuns => Set<SyncRun>();
    public DbSet<SyncCoverage> SyncCoverage => Set<SyncCoverage>();

    /// <summary>Every system-versioned table as (schema, table, history table). Used to apply the retention policy.</summary>
    public static readonly IReadOnlyList<(string Schema, string Table)> TemporalTables =
    [
        ("dim", "Subscription"), ("dim", "VmFamily"),
        ("quota", "SubscriptionQuota"), ("quota", "QuotaGroup"), ("quota", "QuotaGroupMember"),
        ("quota", "GroupQuota"), ("quota", "GroupAllocation"),
        ("zone", "FamilyZoneAccess"), ("zone", "SkuRestriction"), ("zone", "ZoneMapping"),
        ("access", "Grant"),
    ];

    protected override void OnModelCreating(ModelBuilder m)
    {
        const int Id = 64, Name = 128, Long = 512;

        m.Entity<Subscription>(e =>
        {
            Temporal(e, "dim", "Subscription");
            e.HasKey(x => x.SubscriptionId);
            e.Property(x => x.SubscriptionId).HasMaxLength(Id);
            e.Property(x => x.Name).HasMaxLength(Name);
            e.Property(x => x.ManagementGroupPath).HasMaxLength(2048);
            e.Property(x => x.State).HasMaxLength(32);
        });

        m.Entity<Region>(e =>
        {
            e.ToTable("Region", "dim");
            e.HasKey(x => x.Name);
            e.Property(x => x.Name).HasMaxLength(Id);
            e.Property(x => x.DisplayName).HasMaxLength(Name);
            e.Property(x => x.Geography).HasMaxLength(Id);
        });

        m.Entity<VmFamily>(e =>
        {
            Temporal(e, "dim", "VmFamily");
            e.HasKey(x => x.FamilyId);
            e.Property(x => x.FamilyId).HasMaxLength(Name);
            e.Property(x => x.QuotaName).HasMaxLength(Name);
            e.Property(x => x.LocalizedName).HasMaxLength(Name * 2);
            e.Property(x => x.Series).HasMaxLength(Id);
            e.Property(x => x.Category).HasMaxLength(Id);
            e.Property(x => x.CpuManufacturer).HasMaxLength(32);
            e.Property(x => x.Architecture).HasMaxLength(16);
            e.Property(x => x.AcceleratorType).HasMaxLength(16);
            e.Property(x => x.AcceleratorVendor).HasMaxLength(32);
            e.Property(x => x.AcceleratorModel).HasMaxLength(32);
            e.Property(x => x.Generation).HasMaxLength(8);
            e.Property(x => x.Features).HasMaxLength(Name);
            e.Property(x => x.Lifecycle).HasMaxLength(32);
            e.Property(x => x.Classification).HasMaxLength(16);
            e.Property(x => x.SourceUrl).HasMaxLength(Long);
        });

        m.Entity<SubscriptionQuota>(e =>
        {
            Temporal(e, "quota", "SubscriptionQuota");
            e.HasKey(x => new { x.SubscriptionId, x.Region, x.QuotaName });
            e.Property(x => x.SubscriptionId).HasMaxLength(Id);
            e.Property(x => x.Region).HasMaxLength(Id);
            e.Property(x => x.QuotaName).HasMaxLength(Name);
            e.Property(x => x.RawName).HasMaxLength(Name);
            e.Property(x => x.Kind).HasMaxLength(16);
            e.Property(x => x.FamilyId).HasMaxLength(Name);
            e.Property(x => x.LocalizedName).HasMaxLength(Name * 2);
            e.HasIndex(x => new { x.FamilyId, x.Region });
            e.HasIndex(x => new { x.Region, x.Kind });
        });

        m.Entity<QuotaGroup>(e =>
        {
            Temporal(e, "quota", "QuotaGroup");
            e.HasKey(x => new { x.ManagementGroupId, x.GroupName });
            e.Property(x => x.ManagementGroupId).HasMaxLength(Id);
            e.Property(x => x.GroupName).HasMaxLength(Name);
            e.Property(x => x.DisplayName).HasMaxLength(Name * 2);
        });

        m.Entity<QuotaGroupMember>(e =>
        {
            Temporal(e, "quota", "QuotaGroupMember");
            e.HasKey(x => new { x.ManagementGroupId, x.GroupName, x.SubscriptionId });
            e.Property(x => x.ManagementGroupId).HasMaxLength(Id);
            e.Property(x => x.GroupName).HasMaxLength(Name);
            e.Property(x => x.SubscriptionId).HasMaxLength(Id);
        });

        m.Entity<GroupQuota>(e =>
        {
            Temporal(e, "quota", "GroupQuota");
            e.HasKey(x => new { x.ManagementGroupId, x.GroupName, x.Region, x.FamilyId });
            e.Property(x => x.ManagementGroupId).HasMaxLength(Id);
            e.Property(x => x.GroupName).HasMaxLength(Name);
            e.Property(x => x.Region).HasMaxLength(Id);
            e.Property(x => x.FamilyId).HasMaxLength(Name);
        });

        m.Entity<GroupAllocation>(e =>
        {
            Temporal(e, "quota", "GroupAllocation");
            e.HasKey(x => new { x.ManagementGroupId, x.GroupName, x.SubscriptionId, x.Region, x.FamilyId });
            e.Property(x => x.ManagementGroupId).HasMaxLength(Id);
            e.Property(x => x.GroupName).HasMaxLength(Name);
            e.Property(x => x.SubscriptionId).HasMaxLength(Id);
            e.Property(x => x.Region).HasMaxLength(Id);
            e.Property(x => x.FamilyId).HasMaxLength(Name);
        });

        m.Entity<FamilyZoneAccess>(e =>
        {
            Temporal(e, "zone", "FamilyZoneAccess");
            e.HasKey(x => new { x.SubscriptionId, x.Region, x.FamilyId });
            e.Property(x => x.SubscriptionId).HasMaxLength(Id);
            e.Property(x => x.Region).HasMaxLength(Id);
            e.Property(x => x.FamilyId).HasMaxLength(Name);
            e.Property(x => x.ZoneStatus).HasMaxLength(16);
            foreach (var p in new[] { "OfferedZonesLogical", "OpenZonesLogical", "PartialZonesLogical", "RestrictedZonesLogical" })
                e.Property(p).HasMaxLength(32);
            foreach (var p in new[] { "OfferedZonesPhysical", "OpenZonesPhysical", "PartialZonesPhysical", "RestrictedZonesPhysical" })
                e.Property(p).HasMaxLength(256);
            e.Property(x => x.ReasonCodes).HasMaxLength(Name);
        });

        m.Entity<SkuRestriction>(e =>
        {
            Temporal(e, "zone", "SkuRestriction");
            e.HasKey(x => new { x.SubscriptionId, x.Region, x.Sku });
            e.Property(x => x.SubscriptionId).HasMaxLength(Id);
            e.Property(x => x.Region).HasMaxLength(Id);
            e.Property(x => x.Sku).HasMaxLength(Name);
            e.Property(x => x.FamilyId).HasMaxLength(Name);
            e.Property(x => x.RestrictionType).HasMaxLength(16);
            e.Property(x => x.ZonesLogical).HasMaxLength(32);
            e.Property(x => x.ZonesPhysical).HasMaxLength(256);
            e.Property(x => x.ReasonCode).HasMaxLength(Id);
        });

        m.Entity<ZoneMapping>(e =>
        {
            Temporal(e, "zone", "ZoneMapping");
            e.HasKey(x => new { x.SubscriptionId, x.Region, x.LogicalZone });
            e.Property(x => x.SubscriptionId).HasMaxLength(Id);
            e.Property(x => x.Region).HasMaxLength(Id);
            e.Property(x => x.LogicalZone).HasMaxLength(8);
            e.Property(x => x.PhysicalZone).HasMaxLength(Id);
        });

        m.Entity<AccessGrant>(e =>
        {
            Temporal(e, "access", "Grant");
            e.HasKey(x => x.GrantId);
            e.Property(x => x.PrincipalObjectId).HasMaxLength(Id);
            e.Property(x => x.PrincipalType).HasMaxLength(16);
            e.Property(x => x.PrincipalDisplayName).HasMaxLength(Name * 2);
            e.Property(x => x.TargetType).HasMaxLength(32);
            e.Property(x => x.TargetId).HasMaxLength(Name * 2);
            e.Property(x => x.Note).HasMaxLength(Long);
            e.Property(x => x.ChangedBy).HasMaxLength(Name * 2);
            e.HasIndex(x => new { x.PrincipalObjectId, x.TargetType, x.TargetId }).IsUnique();
        });

        m.Entity<SyncRun>(e =>
        {
            e.ToTable("Run", "sync");
            e.HasKey(x => new { x.RunId, x.Stage });
            e.Property(x => x.Stage).HasMaxLength(32);
            e.Property(x => x.Trigger).HasMaxLength(32);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.Message).HasMaxLength(4000);
            e.HasIndex(x => x.StartedUtc);
        });

        m.Entity<SyncCoverage>(e =>
        {
            e.ToTable("Coverage", "sync");
            e.HasKey(x => new { x.RunId, x.Stage, x.SubscriptionId });
            e.Property(x => x.Stage).HasMaxLength(32);
            e.Property(x => x.SubscriptionId).HasMaxLength(Id);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.Error).HasMaxLength(2000);
        });
    }

    private static void Temporal<T>(EntityTypeBuilder<T> e, string schema, string table) where T : class =>
        e.ToTable(table, schema, t => t.IsTemporal(tt =>
        {
            tt.HasPeriodStart(PeriodStart);
            tt.HasPeriodEnd(PeriodEnd);
            tt.UseHistoryTable(table + "History", schema);
        }));
}
