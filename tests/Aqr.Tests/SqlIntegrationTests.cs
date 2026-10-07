using Aqr.Core;
using Aqr.Core.Access;
using Aqr.Core.Data;
using Aqr.Core.Model;
using Aqr.Core.Reporting;
using Aqr.Core.Sync;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aqr.Tests;

/// <summary>
/// End-to-end against a real engine (the Azure SQL Database container locally, or an Azure SQL dev database):
/// migrations, temporal history, retention, write-on-change and "as of" reads. Each run uses a fresh database.
/// </summary>
public sealed class SqlIntegrationTests : IAsyncLifetime
{
    private string _conn = "";
    private string _db = "";

    public async Task InitializeAsync()
    {
        var baseConn = Environment.GetEnvironmentVariable(SqlFactAttribute.EnvVar);
        if (string.IsNullOrWhiteSpace(baseConn)) return;
        _db = "aqr_test_" + Guid.NewGuid().ToString("N")[..10];
        var master = new SqlConnectionStringBuilder(baseConn) { InitialCatalog = "master" };
        await using (var c = new SqlConnection(master.ConnectionString))
        {
            await c.OpenAsync();
            await using var cmd = new SqlCommand($"CREATE DATABASE [{_db}]", c);
            await cmd.ExecuteNonQueryAsync();
        }
        _conn = new SqlConnectionStringBuilder(baseConn) { InitialCatalog = _db }.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        if (_db.Length == 0) return;
        SqlConnection.ClearAllPools();
        var master = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(SqlFactAttribute.EnvVar)) { InitialCatalog = "master" };
        await using var c = new SqlConnection(master.ConnectionString);
        await c.OpenAsync();
        await using var cmd = new SqlCommand($"DROP DATABASE IF EXISTS [{_db}]", c);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<IDbContextFactory<AqrDbContext>> MigratedAsync()
    {
        var factory = TestHost.SqlFactory(_conn);
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
        await TemporalRetention.ApplyAsync(db, "1 YEAR", NullLogger.Instance, default);
        return factory;
    }

    [SqlFact]
    public async Task Engine_is_azure_sql_and_retention_is_one_year_on_every_temporal_table()
    {
        var factory = await MigratedAsync();
        await using var db = await factory.CreateDbContextAsync();
        var edition = await db.Database.SqlQueryRaw<int>("SELECT CAST(SERVERPROPERTY('EngineEdition') AS int) AS [Value]").SingleAsync();
        Assert.Equal(5, edition); // 5 = Azure SQL Database engine (the container or the cloud)

        var retention = await db.Database.SqlQueryRaw<string>(
            "SELECT CONCAT(history_retention_period, ' ', history_retention_period_unit_desc) AS [Value] FROM sys.tables WHERE temporal_type = 2").ToListAsync();
        Assert.Equal(AqrDbContext.TemporalTables.Count, retention.Count);
        Assert.All(retention, r => Assert.Equal("1 YEAR", r));
    }

    [SqlFact]
    public async Task Unchanged_rerun_creates_no_history_and_changes_are_queryable_as_of()
    {
        var factory = await MigratedAsync();
        var t0 = new DateTimeOffset(2026, 10, 7, 13, 0, 0, TimeSpan.Zero);
        var time = new FixedTime(t0);
        await TestHost.Sync(factory, time).RunAsync(new SyncRequest("Test", Force: true), default);
        var afterFirst = DateTime.UtcNow;
        await Task.Delay(1100);

        await TestHost.Sync(factory, time).RunAsync(new SyncRequest("Test", Force: true), default);
        await using (var db = await factory.CreateDbContextAsync())
            Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM quota.SubscriptionQuotaHistory").SingleAsync());

        time.Now = t0.AddHours(1);
        var second = await TestHost.Sync(factory, time).RunAsync(new SyncRequest("Test", Force: true), default);
        var updated = second.Stages.Single(s => s.Stage == SyncStages.SubQuota).Updated;
        Assert.True(updated > 0);
        await using (var db = await factory.CreateDbContextAsync())
            Assert.Equal(updated, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM quota.SubscriptionQuotaHistory").SingleAsync());

        // "As of" the first sync returns the earlier usage for a key that changed.
        var reports = new ReportService(factory);
        var now = await reports.ExplorerAsync(Visibility.Everything, new ExplorerFilter { PageSize = 5000 }, default);
        var then = await reports.ExplorerAsync(Visibility.Everything, new ExplorerFilter { PageSize = 5000, AsOf = afterFirst }, default);
        Assert.Equal(now.Total, then.Total);
        Assert.NotEqual(now.TotalUsage, then.TotalUsage);

        var key = now.Rows.First(r => then.Rows.Any(t => t.SubscriptionId == r.SubscriptionId && t.Region == r.Region && t.QuotaName == r.QuotaName && t.Usage != r.Usage));
        var trend = await reports.TrendAsync(Visibility.Everything, key.SubscriptionId, key.Region, key.QuotaName, DateTime.UtcNow.AddDays(-1), default);
        Assert.True(trend.Points.Count >= 2);
    }

    [SqlFact]
    public async Task Overview_groups_zones_and_families_run_on_the_real_engine()
    {
        var factory = await MigratedAsync();
        await TestHost.Sync(factory, new FixedTime(new DateTimeOffset(2026, 10, 7, 13, 0, 0, TimeSpan.Zero))).RunAsync(new SyncRequest("Test", Force: true), default);
        var reports = new ReportService(factory);
        var o = await reports.OverviewAsync(Visibility.Everything, null, default);
        Assert.True(o.FamilyQuotas > 0 && o.RegionalLimit > 0 && o.Top.Count > 0);
        Assert.NotEmpty((await reports.GroupsAsync(Visibility.Everything, null, "*", null, default)).Rows);
        Assert.NotEmpty((await reports.ZonesAsync(Visibility.Everything, "eastus2", null, null, default)).Rows);
        Assert.NotEmpty(await reports.FamiliesAsync(Visibility.Everything, default));
        _ = await reports.ExplorerAsync(new Visibility(false, ["19aa0000-0000-4000-8000-000000000008"], []), new ExplorerFilter
        {
            OpenInZones = ["1", "az2"], Features = ["premiumSsd"], QuotaGroups = ["(none)"], Search = "s",
        }, default);
    }

    [SqlFact]
    public async Task App_lock_admits_one_writer()
    {
        var factory = await MigratedAsync();
        await using var first = await SqlAppLock.TryAcquireAsync(factory, SqlAppLock.SyncResource, NullLogger.Instance, 0, default);
        Assert.NotNull(first);
        await using var second = await SqlAppLock.TryAcquireAsync(factory, SqlAppLock.SyncResource, NullLogger.Instance, 0, default);
        Assert.Null(second);
    }
}
