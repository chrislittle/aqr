using Aqr.Core.Access;
using Aqr.Core.Data;
using Aqr.Core.Model;
using Microsoft.EntityFrameworkCore;

namespace Aqr.Core.Reporting;

public sealed class ExplorerRow
{
    public string SubscriptionId { get; init; } = "";
    public string SubscriptionName { get; init; } = "";
    public string Region { get; init; } = "";
    public string Geography { get; init; } = "";
    public bool RegionHasZones { get; init; }
    public string QuotaName { get; init; } = "";
    public string Kind { get; init; } = "";
    public string? FamilyId { get; init; }
    public string LocalizedName { get; init; } = "";
    public string Series { get; init; } = "";
    public string Category { get; init; } = "";
    public string CpuManufacturer { get; init; } = "";
    public string Architecture { get; init; } = "";
    public string AcceleratorType { get; init; } = "";
    public string AcceleratorVendor { get; init; } = "";
    public string AcceleratorModel { get; init; } = "";
    public string Generation { get; init; } = "";
    public string Features { get; init; } = "";
    public string Lifecycle { get; init; } = "";
    public string Classification { get; init; } = "";
    public long Usage { get; init; }
    public long Limit { get; init; }
    public string? QuotaGroup { get; init; }
    public string? ZoneStatus { get; init; }
    public string OfferedZonesLogical { get; init; } = "";
    public string OpenZonesLogical { get; init; } = "";
    public string PartialZonesLogical { get; init; } = "";
    public string RestrictedZonesLogical { get; init; } = "";
    public string OfferedZonesPhysical { get; init; } = "";
    public string OpenZonesPhysical { get; init; } = "";
    public string PartialZonesPhysical { get; init; } = "";
    public string RestrictedZonesPhysical { get; init; } = "";

    public long Available => Math.Max(0, Limit - Usage);
    public double UtilPct => Limit > 0 ? 100.0 * Usage / Limit : 0;
    public bool NotDeployable => Available > 0 && ZoneStatus is Model.ZoneStatuses.NoZones or Model.ZoneStatuses.RegionBlocked;
}

public sealed record ExplorerResult(IReadOnlyList<ExplorerRow> Rows, int Total, long TotalUsage, long TotalLimit, int NotDeployable, bool HistoryAvailable);

public sealed record SumRow(string Key, string Key2, long Usage, long Limit, int Count)
{
    public double UtilPct => Limit > 0 ? 100.0 * Usage / Limit : 0;
}

public sealed record OverviewResult(
    long RegionalUsage, long RegionalLimit,
    int FamilyQuotas, int Hot, int AtLimit,
    long GroupPoolUnallocated, int GroupCount,
    int NotDeployable, long NotDeployableVcpu, int PartialZone,
    IReadOnlyList<ExplorerRow> Top, IReadOnlyList<ExplorerRow> Blocked,
    IReadOnlyList<SumRow> Heatmap, IReadOnlyList<SumRow> ByCpu, IReadOnlyList<SumRow> ByRegion, IReadOnlyList<SumRow> Restricted,
    IReadOnlyList<SyncRun> LastRuns);

public sealed record GroupRow(GroupQuota Quota, string Series, string CpuManufacturer, IReadOnlyList<GroupAllocation> Allocations);
public sealed record GroupMemberRow(string SubscriptionId, string Name, string ManagementGroupPath, bool Visible, long Allocated, long Usage, int Blocked);
public sealed record GroupsResult(IReadOnlyList<QuotaGroup> Groups, QuotaGroup? Selected, IReadOnlyList<string> Regions,
    IReadOnlyList<GroupRow> Rows, IReadOnlyList<GroupMemberRow> Members, IReadOnlyList<ZoneMapping> ZoneMappings);

public sealed record ZoneSubRow(Subscription Subscription, SubscriptionQuota? Quota, FamilyZoneAccess? Access, IReadOnlyList<SkuRestriction> Restrictions, IReadOnlyList<ZoneMapping> Mapping);
public sealed record FamilyOption(string FamilyId, string Series);
public sealed record ZonesResult(IReadOnlyList<string> Regions, IReadOnlyList<FamilyOption> Families, VmFamily? Family, Region? Region, IReadOnlyList<ZoneSubRow> Rows);

public sealed record TrendPoint(DateTime From, DateTime To, long Usage, long Limit);
public sealed record TrendResult(SubscriptionQuota? Current, string SubscriptionName, string Series, IReadOnlyList<TrendPoint> Points, bool HistoryAvailable);

public sealed record FamilyRow(VmFamily Family, int QuotasHeld, long TotalLimit, long TotalUsage);

/// <summary>
/// All report reads. Every query applies the caller's <see cref="Visibility"/> server-side, and every fact query
/// can be evaluated "as of" a past instant through the temporal tables (FOR SYSTEM_TIME AS OF).
/// </summary>
public sealed class ReportService(IDbContextFactory<AqrDbContext> dbFactory)
{
    private static IQueryable<T> At<T>(AqrDbContext db, DbSet<T> set, DateTime? asOf) where T : class =>
        asOf is { } t && db.Database.IsSqlServer() ? set.TemporalAsOf(t) : set;

    public static bool SupportsHistory(AqrDbContext db) => db.Database.IsSqlServer();

    // ------------------------------------------------------------------ Explorer
    public async Task<ExplorerResult> ExplorerAsync(Visibility vis, ExplorerFilter f, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = BaseQuery(db, vis, f);

        var total = await q.CountAsync(ct);
        var totalUsage = await q.SumAsync(r => (long?)r.Usage, ct) ?? 0;
        var totalLimit = await q.SumAsync(r => (long?)r.Limit, ct) ?? 0;
        var notDeployable = f.Kind == QuotaKinds.Family
            ? await q.CountAsync(r => r.Limit > r.Usage && (r.ZoneStatus == ZoneStatuses.NoZones || r.ZoneStatus == ZoneStatuses.RegionBlocked), ct)
            : 0;

        var rows = await Sort(q, f).Skip((f.Page - 1) * f.PageSize).Take(f.PageSize).ToListAsync(ct);
        return new ExplorerResult(rows, total, totalUsage, totalLimit, notDeployable, SupportsHistory(db));
    }

    /// <summary>Every matching row (no paging), streamed — used for CSV export so large scopes are never truncated.</summary>
    public async IAsyncEnumerable<ExplorerRow> ExplorerStreamAsync(Visibility vis, ExplorerFilter f, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await foreach (var row in Sort(BaseQuery(db, vis, f), f).AsAsyncEnumerable().WithCancellation(ct))
            yield return row;
    }

    private static IQueryable<ExplorerRow> Sort(IQueryable<ExplorerRow> q, ExplorerFilter f) => (f.Sort, f.Descending) switch
    {
        ("available", true) => q.OrderByDescending(r => r.Limit - r.Usage),
        ("available", false) => q.OrderBy(r => r.Limit - r.Usage),
        ("limit", true) => q.OrderByDescending(r => r.Limit),
        ("limit", false) => q.OrderBy(r => r.Limit),
        ("usage", true) => q.OrderByDescending(r => r.Usage),
        ("usage", false) => q.OrderBy(r => r.Usage),
        ("sub", var d) => d ? q.OrderByDescending(r => r.SubscriptionName) : q.OrderBy(r => r.SubscriptionName),
        ("region", var d) => d ? q.OrderByDescending(r => r.Region) : q.OrderBy(r => r.Region),
        ("family", var d) => d ? q.OrderByDescending(r => r.Series) : q.OrderBy(r => r.Series),
        (_, false) => q.OrderBy(r => r.Limit > 0 ? (double)r.Usage / r.Limit : 0).ThenBy(r => r.Limit),
        _ => q.OrderByDescending(r => r.Limit > 0 ? (double)r.Usage / r.Limit : 0).ThenByDescending(r => r.Limit),
    };

    internal static IQueryable<ExplorerRow> BaseQuery(AqrDbContext db, Visibility vis, ExplorerFilter f)
    {
        var quotas = At(db, db.SubscriptionQuotas, f.AsOf).Where(x => x.Kind == f.Kind);
        if (!vis.All) { var ids = vis.SubscriptionIds.ToList(); quotas = quotas.Where(x => ids.Contains(x.SubscriptionId)); }
        if (f.Subscriptions.Length > 0) { var s = f.Subscriptions; quotas = quotas.Where(x => s.Contains(x.SubscriptionId)); }
        if (f.Regions.Length > 0) { var r = f.Regions; quotas = quotas.Where(x => r.Contains(x.Region)); }
        if (f.MinUtil > 0) { var m = f.MinUtil; quotas = quotas.Where(x => x.Limit > 0 && x.Usage * 100 >= x.Limit * m); }
        if (f.MinAvailable > 0) { var m = f.MinAvailable; quotas = quotas.Where(x => x.Limit - x.Usage >= m); }

        var members = At(db, db.QuotaGroupMembers, f.AsOf);
        if (f.QuotaGroups.Length > 0)
        {
            var named = f.QuotaGroups.Where(g => g != "(none)").ToList();
            var none = f.QuotaGroups.Contains("(none)");
            quotas = quotas.Where(x => members.Any(m => m.SubscriptionId == x.SubscriptionId && named.Contains(m.GroupName))
                                    || (none && !members.Any(m => m.SubscriptionId == x.SubscriptionId)));
        }

        var rows =
            from x in quotas
            join s0 in At(db, db.Subscriptions, f.AsOf) on x.SubscriptionId equals s0.SubscriptionId into sj
            from s in sj.DefaultIfEmpty()
            join r0 in db.Regions on x.Region equals r0.Name into rj
            from r in rj.DefaultIfEmpty()
            join v0 in At(db, db.VmFamilies, f.AsOf) on x.FamilyId equals v0.FamilyId into vj
            from v in vj.DefaultIfEmpty()
            join z0 in At(db, db.FamilyZoneAccess, f.AsOf) on new { x.SubscriptionId, x.Region, FamilyId = x.FamilyId ?? "" } equals new { z0.SubscriptionId, z0.Region, z0.FamilyId } into zj
            from z in zj.DefaultIfEmpty()
            select new ExplorerRow
            {
                SubscriptionId = x.SubscriptionId,
                SubscriptionName = s != null ? s.Name : x.SubscriptionId,
                Region = x.Region,
                Geography = r != null ? r.Geography : "",
                RegionHasZones = r != null && r.HasZones,
                QuotaName = x.QuotaName,
                Kind = x.Kind,
                FamilyId = x.FamilyId,
                LocalizedName = x.LocalizedName,
                Series = v != null ? v.Series : (x.FamilyId ?? x.LocalizedName),
                Category = v != null ? v.Category : "Unclassified",
                CpuManufacturer = v != null ? v.CpuManufacturer : "Unclassified",
                Architecture = v != null ? v.Architecture : "",
                AcceleratorType = v != null ? v.AcceleratorType : "None",
                AcceleratorVendor = v != null ? v.AcceleratorVendor : "",
                AcceleratorModel = v != null ? v.AcceleratorModel : "",
                Generation = v != null ? v.Generation : "",
                Features = v != null ? v.Features : "",
                Lifecycle = v != null ? v.Lifecycle : "",
                Classification = v != null ? v.Classification : "",
                Usage = x.Usage,
                Limit = x.Limit,
                QuotaGroup = members.Where(m => m.SubscriptionId == x.SubscriptionId).Select(m => m.GroupName).FirstOrDefault(),
                ZoneStatus = z != null ? z.ZoneStatus : null,
                OfferedZonesLogical = z != null ? z.OfferedZonesLogical : "",
                OpenZonesLogical = z != null ? z.OpenZonesLogical : "",
                PartialZonesLogical = z != null ? z.PartialZonesLogical : "",
                RestrictedZonesLogical = z != null ? z.RestrictedZonesLogical : "",
                OfferedZonesPhysical = z != null ? z.OfferedZonesPhysical : "",
                OpenZonesPhysical = z != null ? z.OpenZonesPhysical : "",
                PartialZonesPhysical = z != null ? z.PartialZonesPhysical : "",
                RestrictedZonesPhysical = z != null ? z.RestrictedZonesPhysical : "",
            };

        if (f.Geographies.Length > 0) { var g = f.Geographies; rows = rows.Where(x => g.Contains(x.Geography)); }
        if (f.Kind == QuotaKinds.Family)
        {
            if (f.ZoneStatuses.Length > 0) { var z = f.ZoneStatuses; rows = rows.Where(x => x.ZoneStatus != null && z.Contains(x.ZoneStatus)); }
            if (f.Categories.Length > 0) { var c = f.Categories; rows = rows.Where(x => c.Contains(x.Category)); }
            if (f.CpuManufacturers.Length > 0) { var c = f.CpuManufacturers; rows = rows.Where(x => c.Contains(x.CpuManufacturer)); }
            if (f.Architectures.Length > 0) { var a = f.Architectures; rows = rows.Where(x => a.Contains(x.Architecture)); }
            if (f.Accelerators.Length > 0) { var a = f.Accelerators; rows = rows.Where(x => a.Contains(x.AcceleratorType)); }
            if (f.GpuModels.Length > 0) { var gm = f.GpuModels; rows = rows.Where(x => gm.Contains(x.AcceleratorModel)); }
            if (f.Generations.Length > 0) { var g = f.Generations; rows = rows.Where(x => g.Contains(x.Generation)); }
            if (f.Lifecycles.Length > 0) { var l = f.Lifecycles; rows = rows.Where(x => l.Contains(x.Lifecycle)); }
            foreach (var feat in f.Features) rows = rows.Where(x => ("," + x.Features + ",").Contains("," + feat + ","));
            foreach (var zone in f.OpenInZones)
            {
                if (zone.StartsWith("az"))
                {
                    var token = "-" + zone + ",";
                    rows = rows.Where(x => (x.OpenZonesPhysical + "," + x.PartialZonesPhysical + ",").Contains(token));
                }
                else
                {
                    var token = "," + zone + ",";
                    rows = rows.Where(x => ("," + x.OpenZonesLogical + "," + x.PartialZonesLogical + ",").Contains(token));
                }
            }
        }
        if (f.Search.Length > 0)
        {
            var term = f.Search;
            rows = rows.Where(x => x.Series.Contains(term) || x.QuotaName.Contains(term) || x.SubscriptionName.Contains(term) || x.Region.Contains(term) || x.LocalizedName.Contains(term));
        }
        return rows;
    }

    // ------------------------------------------------------------------ Overview
    public async Task<OverviewResult> OverviewAsync(Visibility vis, DateTime? asOf, CancellationToken ct)
    {
        var fam = new ExplorerFilter { AsOf = asOf };
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = BaseQuery(db, vis, fam);
        var cores = BaseQuery(db, vis, new ExplorerFilter { AsOf = asOf, Kind = QuotaKinds.RegionalTotal });

        var regionalUsage = await cores.SumAsync(r => (long?)r.Usage, ct) ?? 0;
        var regionalLimit = await cores.SumAsync(r => (long?)r.Limit, ct) ?? 0;
        var familyCount = await rows.CountAsync(ct);
        var hot = await rows.CountAsync(r => r.Limit > 0 && r.Usage * 100 >= r.Limit * 80, ct);
        var full = await rows.CountAsync(r => r.Limit > 0 && r.Usage >= r.Limit, ct);
        var blockedQ = rows.Where(r => r.Limit > r.Usage && (r.ZoneStatus == ZoneStatuses.NoZones || r.ZoneStatus == ZoneStatuses.RegionBlocked));
        var blockedCount = await blockedQ.CountAsync(ct);
        var blockedVcpu = await blockedQ.SumAsync(r => (long?)(r.Limit - r.Usage), ct) ?? 0;
        var partial = await rows.CountAsync(r => r.Limit > r.Usage && r.ZoneStatus == ZoneStatuses.PartialZones, ct);

        var top = await rows.Where(r => r.Limit > 0).OrderByDescending(r => (double)r.Usage / r.Limit).ThenByDescending(r => r.Limit).Take(12).ToListAsync(ct);
        var blocked = await rows.Where(r => r.Limit > r.Usage && (r.ZoneStatus == ZoneStatuses.NoZones || r.ZoneStatus == ZoneStatuses.RegionBlocked || r.ZoneStatus == ZoneStatuses.PartialZones))
            .OrderBy(r => r.ZoneStatus == ZoneStatuses.PartialZones ? 1 : 0).ThenByDescending(r => r.Limit - r.Usage).Take(8).ToListAsync(ct);

        var heat = await rows.GroupBy(r => new { r.Region, r.Category })
            .Select(g => new SumRow(g.Key.Region, g.Key.Category, g.Sum(x => x.Usage), g.Sum(x => x.Limit), g.Count())).ToListAsync(ct);
        var byCpu = await rows.GroupBy(r => r.CpuManufacturer)
            .Select(g => new SumRow(g.Key, "", g.Sum(x => x.Usage), g.Sum(x => x.Limit), g.Count())).ToListAsync(ct);
        var byRegion = await cores.GroupBy(r => r.Region)
            .Select(g => new SumRow(g.Key, "", g.Sum(x => x.Usage), g.Sum(x => x.Limit), g.Count())).ToListAsync(ct);
        var restricted = await rows.Where(r => r.Lifecycle == "Capacity-restricted").GroupBy(r => r.Series)
            .Select(g => new SumRow(g.Key, "", g.Sum(x => x.Usage), g.Sum(x => x.Limit), g.Count())).ToListAsync(ct);

        var (groups, pool) = await GroupPoolAsync(db, vis, asOf, ct);
        var lastRuns = await LastRunsAsync(db, ct);

        return new OverviewResult(regionalUsage, regionalLimit, familyCount, hot, full, pool, groups,
            blockedCount, blockedVcpu, partial, top, blocked,
            heat, byCpu.OrderByDescending(x => x.Usage).ToList(), byRegion.OrderByDescending(x => x.Limit).ToList(), restricted.OrderByDescending(x => x.Limit).ToList(),
            lastRuns);
    }

    private static async Task<(int Groups, long Pool)> GroupPoolAsync(AqrDbContext db, Visibility vis, DateTime? asOf, CancellationToken ct)
    {
        var visibleGroups = await VisibleGroupKeysAsync(db, vis, asOf, ct);
        var quotas = await At(db, db.GroupQuotas, asOf).Select(g => new { g.ManagementGroupId, g.GroupName, g.AvailableLimit }).ToListAsync(ct);
        var pool = quotas.Where(q => visibleGroups.Contains(q.ManagementGroupId + "/" + q.GroupName)).Sum(q => q.AvailableLimit);
        return (visibleGroups.Count, pool);
    }

    /// <summary>A quota group is visible if the caller can see at least one of its member subscriptions (admins see all).</summary>
    private static async Task<HashSet<string>> VisibleGroupKeysAsync(AqrDbContext db, Visibility vis, DateTime? asOf, CancellationToken ct)
    {
        var groups = await At(db, db.QuotaGroups, asOf).Select(g => new { g.ManagementGroupId, g.GroupName }).ToListAsync(ct);
        var members = await At(db, db.QuotaGroupMembers, asOf).ToListAsync(ct);
        return groups.Where(g => vis.All || members.Any(m => m.ManagementGroupId == g.ManagementGroupId && m.GroupName == g.GroupName && vis.CanSee(m.SubscriptionId)))
            .Select(g => g.ManagementGroupId + "/" + g.GroupName).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<List<SyncRun>> LastRunsAsync(AqrDbContext db, CancellationToken ct)
    {
        var runs = await db.SyncRuns.AsNoTracking().Where(r => r.Status != "Running").OrderByDescending(r => r.StartedUtc).Take(50).ToListAsync(ct);
        return runs.GroupBy(r => r.Stage).Select(g => g.First()).ToList();
    }

    // ------------------------------------------------------------------ Quota groups
    public async Task<GroupsResult> GroupsAsync(Visibility vis, string? groupKey, string? region, DateTime? asOf, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var visible = await VisibleGroupKeysAsync(db, vis, asOf, ct);
        var groups = (await At(db, db.QuotaGroups, asOf).AsNoTracking().ToListAsync(ct))
            .Where(g => visible.Contains(g.ManagementGroupId + "/" + g.GroupName)).OrderBy(g => g.GroupName).ToList();
        var selected = groups.FirstOrDefault(g => string.Equals(g.ManagementGroupId + "/" + g.GroupName, groupKey, StringComparison.OrdinalIgnoreCase)) ?? groups.FirstOrDefault();
        if (selected is null) return new GroupsResult(groups, null, [], [], [], []);

        var quotas = await At(db, db.GroupQuotas, asOf).AsNoTracking()
            .Where(g => g.ManagementGroupId == selected.ManagementGroupId && g.GroupName == selected.GroupName).ToListAsync(ct);
        var regions = quotas.Select(q => q.Region).Distinct().OrderBy(r => r).ToList();
        if (!string.IsNullOrEmpty(region) && region != "*") quotas = quotas.Where(q => q.Region == region).ToList();

        var allocs = await At(db, db.GroupAllocations, asOf).AsNoTracking()
            .Where(a => a.ManagementGroupId == selected.ManagementGroupId && a.GroupName == selected.GroupName).ToListAsync(ct);
        var famIds = quotas.Select(q => q.FamilyId).Distinct().ToList();
        var fams = await At(db, db.VmFamilies, asOf).AsNoTracking().Where(f => famIds.Contains(f.FamilyId)).ToDictionaryAsync(f => f.FamilyId, ct);

        // Allocations to subscriptions the caller can't see are aggregated, not itemised.
        var rows = quotas.OrderByDescending(q => q.GroupLimit).Select(q =>
        {
            var a = allocs.Where(x => x.Region == q.Region && x.FamilyId == q.FamilyId).ToList();
            var shown = a.Where(x => vis.CanSee(x.SubscriptionId)).ToList();
            var hidden = a.Where(x => !vis.CanSee(x.SubscriptionId)).Sum(x => x.QuotaAllocated);
            if (hidden > 0) shown.Add(new GroupAllocation { SubscriptionId = "(other subscriptions)", QuotaAllocated = hidden, Region = q.Region, FamilyId = q.FamilyId });
            var f = fams.GetValueOrDefault(q.FamilyId);
            return new GroupRow(q, f?.Series ?? q.FamilyId, f?.CpuManufacturer ?? "Unclassified", shown);
        }).ToList();

        var memberIds = await At(db, db.QuotaGroupMembers, asOf).AsNoTracking()
            .Where(m => m.ManagementGroupId == selected.ManagementGroupId && m.GroupName == selected.GroupName).Select(m => m.SubscriptionId).ToListAsync(ct);
        var subs = await At(db, db.Subscriptions, asOf).AsNoTracking().Where(s => memberIds.Contains(s.SubscriptionId)).ToDictionaryAsync(s => s.SubscriptionId, ct);
        var memberQuota = await At(db, db.SubscriptionQuotas, asOf).AsNoTracking()
            .Where(q => memberIds.Contains(q.SubscriptionId) && q.Kind == QuotaKinds.Family && (region == null || region == "*" || q.Region == region))
            .GroupBy(q => q.SubscriptionId).Select(g => new { g.Key, Usage = g.Sum(x => x.Usage) }).ToListAsync(ct);
        var blocked = await At(db, db.FamilyZoneAccess, asOf).AsNoTracking()
            .Where(z => memberIds.Contains(z.SubscriptionId) && (z.ZoneStatus == ZoneStatuses.NoZones || z.ZoneStatus == ZoneStatuses.RegionBlocked) && (region == null || region == "*" || z.Region == region))
            .GroupBy(z => z.SubscriptionId).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);

        var members = memberIds.Select(id => new GroupMemberRow(
            id, subs.GetValueOrDefault(id)?.Name ?? id, subs.GetValueOrDefault(id)?.ManagementGroupPath ?? "", vis.CanSee(id),
            allocs.Where(a => a.SubscriptionId == id && (region == null || region == "*" || a.Region == region)).Sum(a => a.QuotaAllocated),
            memberQuota.FirstOrDefault(m => m.Key == id)?.Usage ?? 0,
            blocked.FirstOrDefault(b => b.Key == id)?.N ?? 0)).OrderBy(m => m.Name).ToList();

        var mapRegions = (string.IsNullOrEmpty(region) || region == "*") ? regions.Take(4).ToList() : [region];
        var zm = await At(db, db.ZoneMappings, asOf).AsNoTracking()
            .Where(z => memberIds.Contains(z.SubscriptionId) && mapRegions.Contains(z.Region)).ToListAsync(ct);

        return new GroupsResult(groups, selected, regions, rows, members.Where(m => m.Visible).ToList(), zm.Where(z => vis.CanSee(z.SubscriptionId)).ToList());
    }

    // ------------------------------------------------------------------ Zones
    public async Task<ZonesResult> ZonesAsync(Visibility vis, string? region, string? familyId, DateTime? asOf, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var quotas = At(db, db.SubscriptionQuotas, asOf).AsNoTracking().Where(q => q.Kind == QuotaKinds.Family);
        if (!vis.All) { var ids = vis.SubscriptionIds.ToList(); quotas = quotas.Where(q => ids.Contains(q.SubscriptionId)); }

        var regions = await quotas.Select(q => q.Region).Distinct().OrderBy(r => r).ToListAsync(ct);
        region = regions.Contains(region ?? "") ? region : regions.FirstOrDefault();
        if (region is null) return new ZonesResult([], [], null, null, []);

        var famIds = await quotas.Where(q => q.Region == region).Select(q => q.FamilyId!).Distinct().ToListAsync(ct);
        var fams = await At(db, db.VmFamilies, asOf).AsNoTracking().Where(f => famIds.Contains(f.FamilyId)).ToListAsync(ct);
        var famList = famIds.Select(id => new FamilyOption(id, fams.FirstOrDefault(f => f.FamilyId == id)?.Series ?? id)).OrderBy(x => x.Series).ToList();
        familyId = famIds.Contains(familyId ?? "") ? familyId : famList.FirstOrDefault()?.FamilyId;
        var family = fams.FirstOrDefault(f => f.FamilyId == familyId);
        var regionRow = await db.Regions.AsNoTracking().FirstOrDefaultAsync(r => r.Name == region, ct);

        var q2 = await quotas.Where(q => q.Region == region && q.FamilyId == familyId).ToListAsync(ct);
        var subIds = q2.Select(q => q.SubscriptionId).ToList();
        var subs = await At(db, db.Subscriptions, asOf).AsNoTracking().Where(s => subIds.Contains(s.SubscriptionId)).ToListAsync(ct);
        var access = await At(db, db.FamilyZoneAccess, asOf).AsNoTracking().Where(z => subIds.Contains(z.SubscriptionId) && z.Region == region && z.FamilyId == familyId).ToListAsync(ct);
        var restr = await At(db, db.SkuRestrictions, asOf).AsNoTracking().Where(z => subIds.Contains(z.SubscriptionId) && z.Region == region && z.FamilyId == familyId).ToListAsync(ct);
        var maps = await At(db, db.ZoneMappings, asOf).AsNoTracking().Where(z => subIds.Contains(z.SubscriptionId) && z.Region == region).ToListAsync(ct);

        var rows = subs.OrderBy(s => s.Name).Select(s => new ZoneSubRow(s,
            q2.FirstOrDefault(q => q.SubscriptionId == s.SubscriptionId),
            access.FirstOrDefault(a => a.SubscriptionId == s.SubscriptionId),
            restr.Where(r => r.SubscriptionId == s.SubscriptionId).OrderBy(r => r.Sku).ToList(),
            maps.Where(m => m.SubscriptionId == s.SubscriptionId).OrderBy(m => m.LogicalZone).ToList())).ToList();

        return new ZonesResult(regions, famList, family, regionRow, rows);
    }

    // ------------------------------------------------------------------ Trends
    public async Task<IReadOnlyList<ExplorerRow>> TrendCandidatesAsync(Visibility vis, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await BaseQuery(db, vis, new ExplorerFilter()).Where(r => r.Limit > 0)
            .OrderByDescending(r => (double)r.Usage / r.Limit).ThenByDescending(r => r.Limit).Take(25).ToListAsync(ct);
    }

    public async Task<TrendResult> TrendAsync(Visibility vis, string subscriptionId, string region, string quotaName, DateTime fromUtc, CancellationToken ct)
    {
        subscriptionId = subscriptionId.ToLowerInvariant(); region = region.ToLowerInvariant(); quotaName = quotaName.ToLowerInvariant();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!vis.CanSee(subscriptionId)) return new TrendResult(null, "", "", [], SupportsHistory(db));

        var current = await db.SubscriptionQuotas.AsNoTracking().FirstOrDefaultAsync(q => q.SubscriptionId == subscriptionId && q.Region == region && q.QuotaName == quotaName, ct);
        var subName = await db.Subscriptions.Where(s => s.SubscriptionId == subscriptionId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? subscriptionId;
        var series = await db.VmFamilies.Where(f => f.FamilyId == quotaName).Select(f => f.Series).FirstOrDefaultAsync(ct) ?? quotaName;

        if (!SupportsHistory(db))
        {
            var now = DateTime.UtcNow;
            return new TrendResult(current, subName, series, current is null ? [] : [new TrendPoint(fromUtc, now, current.Usage, current.Limit)], false);
        }

        var points = await db.SubscriptionQuotas.TemporalFromTo(fromUtc, DateTime.UtcNow.AddMinutes(1))
            .Where(q => q.SubscriptionId == subscriptionId && q.Region == region && q.QuotaName == quotaName)
            .Select(q => new TrendPoint(
                EF.Property<DateTime>(q, AqrDbContext.PeriodStart),
                EF.Property<DateTime>(q, AqrDbContext.PeriodEnd), q.Usage, q.Limit))
            .OrderBy(p => p.From).ToListAsync(ct);
        return new TrendResult(current, subName, series, points, true);
    }

    // ------------------------------------------------------------------ Families
    public async Task<IReadOnlyList<FamilyRow>> FamiliesAsync(Visibility vis, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var quotas = db.SubscriptionQuotas.AsNoTracking().Where(q => q.Kind == QuotaKinds.Family);
        if (!vis.All) { var ids = vis.SubscriptionIds.ToList(); quotas = quotas.Where(q => ids.Contains(q.SubscriptionId)); }
        var agg = await quotas.GroupBy(q => q.FamilyId!).Select(g => new { g.Key, N = g.Count(), L = g.Sum(x => x.Limit), U = g.Sum(x => x.Usage) }).ToListAsync(ct);
        var fams = await db.VmFamilies.AsNoTracking().ToListAsync(ct);
        return fams.Select(f =>
        {
            var a = agg.FirstOrDefault(x => x.Key == f.FamilyId);
            return new FamilyRow(f, a?.N ?? 0, a?.L ?? 0, a?.U ?? 0);
        }).Where(r => vis.All || r.QuotasHeld > 0)
          .OrderBy(r => r.Family.Category).ThenBy(r => r.Family.Series).ToList();
    }

    // ------------------------------------------------------------------ Filter options (explorer)
    public sealed record FilterOptions(
        IReadOnlyList<(string Id, string Name)> Subscriptions, IReadOnlyList<string> Groups, IReadOnlyList<string> Geographies,
        IReadOnlyList<string> Regions, IReadOnlyList<string> Categories, IReadOnlyList<string> CpuManufacturers,
        IReadOnlyList<string> Architectures, IReadOnlyList<string> Accelerators, IReadOnlyList<string> GpuModels,
        IReadOnlyList<string> Generations, IReadOnlyList<string> Features, IReadOnlyList<string> Lifecycles);

    public async Task<FilterOptions> FilterOptionsAsync(Visibility vis, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var subs = await db.Subscriptions.AsNoTracking().OrderBy(s => s.Name).Select(s => new { s.SubscriptionId, s.Name }).ToListAsync(ct);
        var quotas = db.SubscriptionQuotas.AsNoTracking();
        if (!vis.All) { var ids = vis.SubscriptionIds.ToList(); quotas = quotas.Where(q => ids.Contains(q.SubscriptionId)); }
        var regions = await quotas.Select(q => q.Region).Distinct().OrderBy(r => r).ToListAsync(ct);
        var famIds = await quotas.Where(q => q.FamilyId != null).Select(q => q.FamilyId!).Distinct().ToListAsync(ct);
        var fams = await db.VmFamilies.AsNoTracking().Where(f => famIds.Contains(f.FamilyId)).ToListAsync(ct);
        var geos = await db.Regions.AsNoTracking().Where(r => regions.Contains(r.Name) && r.Geography != "").Select(r => r.Geography).Distinct().OrderBy(g => g).ToListAsync(ct);
        var groups = (await VisibleGroupKeysAsync(db, vis, null, ct)).Select(k => k.Split('/')[1]).Distinct().OrderBy(g => g).ToList();
        static List<string> D(IEnumerable<string> s) => s.Where(x => x.Length > 0).Distinct().OrderBy(x => x).ToList();
        return new FilterOptions(
            subs.Where(s => vis.CanSee(s.SubscriptionId)).Select(s => (s.SubscriptionId, s.Name)).ToList(),
            groups, geos, regions,
            D(fams.Select(f => f.Category)), D(fams.Select(f => f.CpuManufacturer)), D(fams.Select(f => f.Architecture)),
            D(fams.Select(f => f.AcceleratorType)), D(fams.Select(f => f.AcceleratorModel)), D(fams.Select(f => f.Generation)),
            D(fams.SelectMany(f => f.Features.Split(',', StringSplitOptions.RemoveEmptyEntries))), D(fams.Select(f => f.Lifecycle)));
    }

    // ------------------------------------------------------------------ Admin
    public async Task<(IReadOnlyList<SyncRun> Runs, IReadOnlyList<SyncCoverage> Gaps)> SyncHealthAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var runs = await db.SyncRuns.AsNoTracking().OrderByDescending(r => r.StartedUtc).Take(40).ToListAsync(ct);
        var latest = runs.GroupBy(r => r.Stage).Select(g => g.First().RunId).ToList();
        var gaps = await db.SyncCoverage.AsNoTracking().Where(c => latest.Contains(c.RunId)).ToListAsync(ct);
        return (runs, gaps);
    }

    public async Task<IReadOnlyList<(AccessGrant Grant, DateTime From, DateTime To)>> GrantHistoryAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!SupportsHistory(db)) return [];
        var rows = await db.AccessGrants.TemporalAll().AsNoTracking()
            .Select(g => new { G = g, From = EF.Property<DateTime>(g, AqrDbContext.PeriodStart), To = EF.Property<DateTime>(g, AqrDbContext.PeriodEnd) })
            .OrderByDescending(x => x.From).Take(100).ToListAsync(ct);
        return rows.Select(r => (r.G, r.From, r.To)).ToList();
    }
}
