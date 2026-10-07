using Aqr.Core.Access;
using Aqr.Core.Data;
using Aqr.Core.Model;
using Aqr.Core.Reporting;
using Microsoft.EntityFrameworkCore;

namespace Aqr.Tests;

/// <summary>
/// Proves the report queries translate to SQL Server T-SQL (including FOR SYSTEM_TIME AS OF), without a database:
/// ToQueryString() compiles the query but never opens a connection.
/// </summary>
public sealed class SqlTranslationTests
{
    private static AqrDbContext SqlContext() => new DesignTimeFactory().CreateDbContext([]);

    private static ExplorerFilter Everything(DateTime? asOf = null) => new()
    {
        AsOf = asOf,
        Subscriptions = ["s1"], QuotaGroups = ["qg-a", "(none)"], Geographies = ["US"], Regions = ["eastus2"],
        ZoneStatuses = [ZoneStatuses.PartialZones], OpenInZones = ["1", "az2"], Categories = ["GeneralPurpose"],
        CpuManufacturers = ["AMD"], Architectures = ["x64"], Accelerators = ["None"], GpuModels = ["H100"],
        Generations = ["v5"], Features = ["premiumSsd", "rdma"], Lifecycles = ["Current"], MinUtil = 80, MinAvailable = 10, Search = "dsv5",
    };

    [Fact]
    public void Explorer_with_every_filter_translates()
    {
        using var db = SqlContext();
        var sql = ReportService.BaseQuery(db, new Visibility(false, ["s1", "s2"], []), Everything()).ToQueryString();
        Assert.Contains("[quota].[SubscriptionQuota]", sql);
        Assert.Contains("[zone].[FamilyZoneAccess]", sql);
        Assert.DoesNotContain("FOR SYSTEM_TIME", sql);
    }

    [Fact]
    public void Explorer_as_of_uses_system_time_on_every_temporal_join()
    {
        using var db = SqlContext();
        var sql = ReportService.BaseQuery(db, Visibility.Everything, Everything(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc))).ToQueryString();
        foreach (var table in new[] { "[quota].[SubscriptionQuota]", "[dim].[Subscription]", "[dim].[VmFamily]", "[zone].[FamilyZoneAccess]", "[quota].[QuotaGroupMember]" })
            Assert.Contains($"{table} FOR SYSTEM_TIME AS OF", sql);
    }

    [Fact]
    public void Overview_aggregations_translate()
    {
        using var db = SqlContext();
        var rows = ReportService.BaseQuery(db, Visibility.Everything, new ExplorerFilter());
        _ = rows.GroupBy(r => new { r.Region, r.Category }).Select(g => new SumRow(g.Key.Region, g.Key.Category, g.Sum(x => x.Usage), g.Sum(x => x.Limit), g.Count())).ToQueryString();
        _ = rows.Where(r => r.Limit > 0).OrderByDescending(r => (double)r.Usage / r.Limit).ThenByDescending(r => r.Limit).Take(12).ToQueryString();
        _ = rows.Where(r => r.Lifecycle == "Capacity-restricted").GroupBy(r => r.Series).Select(g => new SumRow(g.Key, "", g.Sum(x => x.Usage), g.Sum(x => x.Limit), g.Count())).ToQueryString();
    }

    [Fact]
    public void Trend_history_query_translates_with_period_columns()
    {
        using var db = SqlContext();
        var sql = db.SubscriptionQuotas.TemporalFromTo(DateTime.UtcNow.AddDays(-30), DateTime.UtcNow)
            .Where(q => q.SubscriptionId == "s" && q.Region == "r" && q.QuotaName == "q")
            .Select(q => new TrendPoint(EF.Property<DateTime>(q, AqrDbContext.PeriodStart), EF.Property<DateTime>(q, AqrDbContext.PeriodEnd), q.Usage, q.Limit))
            .ToQueryString();
        Assert.Contains("FOR SYSTEM_TIME FROM", sql);
        Assert.Contains("[ValidFrom]", sql);
    }
}
