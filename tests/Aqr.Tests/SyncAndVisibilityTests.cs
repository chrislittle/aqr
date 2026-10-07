using Aqr.Core;
using Aqr.Core.Access;
using Aqr.Core.Data;
using Aqr.Core.Model;
using Aqr.Core.Reporting;
using Aqr.Core.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Aqr.Tests;

public sealed class SyncPipelineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 7, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task First_run_populates_every_table_and_records_stages()
    {
        var db = TestHost.InMemoryFactory();
        var outcome = await TestHost.Sync(db, new FixedTime(T0)).RunAsync(new SyncRequest("Test", Force: true), default);

        Assert.True(outcome.Ran);
        Assert.Equal([SyncStages.Inventory, SyncStages.SubQuota, SyncStages.Groups, SyncStages.Zones, SyncStages.Catalog], outcome.Stages.Select(s => s.Stage));
        Assert.All(outcome.Stages, s => Assert.Equal("Succeeded", s.Status));

        await using var ctx = await db.CreateDbContextAsync();
        Assert.Equal(8, await ctx.Subscriptions.CountAsync());
        Assert.True(await ctx.SubscriptionQuotas.CountAsync(q => q.Kind == QuotaKinds.Family) > 50);
        Assert.True(await ctx.SubscriptionQuotas.CountAsync(q => q.Kind == QuotaKinds.RegionalTotal) > 10);
        Assert.Equal(2, await ctx.QuotaGroups.CountAsync());
        Assert.True(await ctx.GroupAllocations.AnyAsync());
        Assert.True(await ctx.FamilyZoneAccess.AnyAsync());
        Assert.True(await ctx.ZoneMappings.AnyAsync());
        Assert.True(await ctx.VmFamilies.AnyAsync(f => f.Classification == "Catalog" && f.MaxVcpu > 0));
        Assert.All(await ctx.SubscriptionQuotas.ToListAsync(), q => Assert.Equal(q.QuotaName, q.QuotaName.ToLowerInvariant()));
    }

    [Fact]
    public async Task Rerun_with_unchanged_data_writes_nothing()
    {
        var db = TestHost.InMemoryFactory();
        var time = new FixedTime(T0);
        await TestHost.Sync(db, time).RunAsync(new SyncRequest("Test", Force: true), default);
        var second = await TestHost.Sync(db, time).RunAsync(new SyncRequest("Test", Force: true), default);

        Assert.All(second.Stages, s => Assert.Equal(0, s.Inserted + s.Updated + s.Deleted));
        Assert.All(second.Stages.Where(s => s.Stage == SyncStages.SubQuota), s => Assert.True(s.KeysSeen > 0));
    }

    [Fact]
    public async Task Next_hour_only_updates_rows_whose_usage_moved()
    {
        var db = TestHost.InMemoryFactory();
        var time = new FixedTime(T0);
        await TestHost.Sync(db, time).RunAsync(new SyncRequest("Test", Force: true), default);
        time.Now = T0.AddHours(1);
        var second = await TestHost.Sync(db, time).RunAsync(new SyncRequest("Test", Force: true), default);

        var quota = second.Stages.Single(s => s.Stage == SyncStages.SubQuota);
        Assert.True(quota.Updated > 0, "mock usage drifts hourly");
        Assert.True(quota.Updated < quota.KeysSeen, "unchanged keys must not be rewritten");
        Assert.Equal(0, quota.Inserted + quota.Deleted);
    }

    [Fact]
    public async Task Schedule_skips_stages_that_are_not_due()
    {
        var db = TestHost.InMemoryFactory();
        var time = new FixedTime(T0);
        await TestHost.Sync(db, time).RunAsync(new SyncRequest("Test", Force: true), default);
        time.Now = T0.AddMinutes(10);
        var again = await TestHost.Sync(db, time).RunAsync(new SyncRequest("Schedule"), default);
        Assert.False(again.Ran);

        time.Now = T0.AddHours(1);
        var hourly = await TestHost.Sync(db, time).RunAsync(new SyncRequest("Schedule"), default);
        Assert.Contains(hourly.Stages, s => s.Stage == SyncStages.SubQuota);
        Assert.DoesNotContain(hourly.Stages, s => s.Stage == SyncStages.Zones);
    }

    [Fact]
    public async Task Missing_management_groups_fail_inventory_outside_mock_mode()
    {
        var db = TestHost.InMemoryFactory();
        var sync = TestHost.Sync(db, new FixedTime(T0), options: new AqrOptions { UseMock = false });
        var outcome = await sync.RunAsync(new SyncRequest("Test", Force: true), default);
        Assert.Equal("Failed", Assert.Single(outcome.Stages).Status);
    }

    [Fact]
    public async Task Change_applier_respects_the_deletable_scope()
    {
        var db = TestHost.InMemoryFactory();
        await using (var ctx = await db.CreateDbContextAsync())
        {
            ctx.Subscriptions.AddRange(new Subscription { SubscriptionId = "a", Name = "A" }, new Subscription { SubscriptionId = "b", Name = "B" });
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = await db.CreateDbContextAsync())
        {
            var c = await ChangeApplier.ApplyAsync(ctx, ctx.Subscriptions, [new Subscription { SubscriptionId = "c", Name = "C" }],
                s => s.SubscriptionId, s => s.SubscriptionId == "a", default);
            Assert.Equal((1, 0, 1), (c.Inserted, c.Updated, c.Deleted));
        }
        await using (var ctx = await db.CreateDbContextAsync())
            Assert.Equal(["b", "c"], await ctx.Subscriptions.Select(s => s.SubscriptionId).OrderBy(x => x).ToListAsync());
    }
}

public sealed class VisibilityTests
{
    private static async Task<(IDbContextFactory<AqrDbContext> Db, VisibilityResolver Resolver)> SeededAsync()
    {
        var db = TestHost.InMemoryFactory();
        await TestHost.Sync(db, new FixedTime(new DateTimeOffset(2026, 10, 7, 13, 0, 0, TimeSpan.Zero))).RunAsync(new SyncRequest("Test", Force: true), default);
        return (db, new VisibilityResolver(db, new MemoryCache(new MemoryCacheOptions())));
    }

    private static UserContext Reader(params string[] groups) => new("user-1", "Reader", false, true, groups, false);

    [Fact]
    public async Task Admin_sees_everything_and_reader_without_grants_sees_nothing()
    {
        var (_, resolver) = await SeededAsync();
        Assert.True((await resolver.ResolveAsync(new UserContext("x", "Admin", true, true, [], false), default)).All);
        var none = await resolver.ResolveAsync(Reader("g1"), default);
        Assert.True(none.IsEmpty);
    }

    [Fact]
    public async Task Management_group_grant_includes_descendant_subscriptions_only()
    {
        var (db, resolver) = await SeededAsync();
        await using (var ctx = await db.CreateDbContextAsync())
        {
            ctx.AccessGrants.Add(new AccessGrant { GrantId = Guid.NewGuid(), PrincipalObjectId = "g-ai", TargetType = GrantTargetTypes.ManagementGroup, TargetId = "mg-ai" });
            await ctx.SaveChangesAsync();
        }
        var v = await resolver.ResolveAsync(Reader("G-AI"), default);
        Assert.Equal(2, v.SubscriptionIds.Count);
        Assert.Contains("e6a20000-0000-4000-8000-000000000005", v.SubscriptionIds);
    }

    [Fact]
    public async Task Quota_group_and_subscription_grants_union()
    {
        var (db, resolver) = await SeededAsync();
        await using (var ctx = await db.CreateDbContextAsync())
        {
            ctx.AccessGrants.AddRange(
                new AccessGrant { GrantId = Guid.NewGuid(), PrincipalObjectId = "g1", TargetType = GrantTargetTypes.QuotaGroup, TargetId = "mg-platform/qg-prod-compute" },
                new AccessGrant { GrantId = Guid.NewGuid(), PrincipalObjectId = "user-1", TargetType = GrantTargetTypes.Subscription, TargetId = "19aa0000-0000-4000-8000-000000000008" });
            await ctx.SaveChangesAsync();
        }
        var v = await resolver.ResolveAsync(Reader("g1"), default);
        Assert.Equal(5, v.SubscriptionIds.Count);
    }

    [Fact]
    public async Task Explorer_never_returns_rows_outside_the_callers_scope()
    {
        var (db, _) = await SeededAsync();
        var reports = new ReportService(db);
        var sub = "19aa0000-0000-4000-8000-000000000008";
        var vis = new Visibility(false, [sub], []);
        var all = await reports.ExplorerAsync(vis, new ExplorerFilter { PageSize = 5000 }, default);
        Assert.NotEmpty(all.Rows);
        Assert.All(all.Rows, r => Assert.Equal(sub, r.SubscriptionId));

        // Explicitly asking for someone else's subscription still returns nothing.
        var other = await reports.ExplorerAsync(vis, new ExplorerFilter { Subscriptions = ["a1f30000-0000-4000-8000-000000000001"] }, default);
        Assert.Empty(other.Rows);
    }
}
