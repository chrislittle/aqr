using Aqr.Core.Sources;
using Aqr.Core.Sync;
using Microsoft.EntityFrameworkCore;

namespace Aqr.Tests;

/// <summary>Regression tests for code-review findings.</summary>
public sealed class ReviewRegressionTests(DevAppFactory factory) : IClassFixture<DevAppFactory>
{
    private sealed class FailingGroupSource(IQuotaSource inner, string failingGroup) : IQuotaSource
    {
        public SourceStats Stats => inner.Stats;
        public Task<IReadOnlyList<SubscriptionInfo>> GetSubscriptionsAsync(IReadOnlyList<string> m, IReadOnlyList<string> s, CancellationToken ct) => inner.GetSubscriptionsAsync(m, s, ct);
        public Task<IReadOnlyList<QuotaUsage>> GetQuotaUsagesAsync(IReadOnlyList<string> s, bool e, CancellationToken ct) => inner.GetQuotaUsagesAsync(s, e, ct);
        public Task<IReadOnlyList<QuotaUsage>> GetComputeUsagesAsync(string s, string r, CancellationToken ct) => inner.GetComputeUsagesAsync(s, r, ct);
        public Task<IReadOnlyList<LocationInfo>> GetLocationsAsync(string s, CancellationToken ct) => inner.GetLocationsAsync(s, ct);
        public Task<IReadOnlyList<SkuInfo>> GetVmSkusAsync(string s, string r, CancellationToken ct) => inner.GetVmSkusAsync(s, r, ct);
        public Task<IReadOnlyList<QuotaGroupInfo>> GetQuotaGroupsAsync(string m, CancellationToken ct) => inner.GetQuotaGroupsAsync(m, ct);
        public Task<IReadOnlyList<string>> GetQuotaGroupSubscriptionsAsync(string m, string g, CancellationToken ct) => inner.GetQuotaGroupSubscriptionsAsync(m, g, ct);
        public Task<IReadOnlyList<GroupQuotaLimitInfo>> GetGroupQuotaLimitsAsync(string m, string g, string r, CancellationToken ct) =>
            g == failingGroup ? throw new ArmRequestException(System.Net.HttpStatusCode.TooManyRequests, "throttled") : inner.GetGroupQuotaLimitsAsync(m, g, r, ct);
        public Task<IReadOnlyList<GroupQuotaUsageInfo>> GetGroupQuotaUsagesAsync(string m, string g, string r, CancellationToken ct) => inner.GetGroupQuotaUsagesAsync(m, g, r, ct);
    }

    [Fact]
    public async Task A_group_that_fails_one_run_keeps_its_rows()
    {
        var db = TestHost.InMemoryFactory();
        var time = new FixedTime(new DateTimeOffset(2026, 10, 7, 13, 0, 0, TimeSpan.Zero));
        await TestHost.Sync(db, time).RunAsync(new SyncRequest("Test", Force: true), default);
        int groups, members, quotas;
        await using (var ctx = await db.CreateDbContextAsync())
            (groups, members, quotas) = (await ctx.QuotaGroups.CountAsync(), await ctx.QuotaGroupMembers.CountAsync(), await ctx.GroupQuotas.CountAsync());

        var failing = new FailingGroupSource(new MockQuotaSource(time), "qg-ai-gpu");
        var run = await TestHost.Sync(db, time, failing).RunAsync(new SyncRequest("Test", Force: true), default);
        var stage = run.Stages.Single(s => s.Stage == "Groups");
        Assert.Equal("Warning", stage.Status);
        Assert.Equal(0, stage.Deleted);

        await using var after = await db.CreateDbContextAsync();
        Assert.Equal(groups, await after.QuotaGroups.CountAsync());
        Assert.Equal(members, await after.QuotaGroupMembers.CountAsync());
        Assert.Equal(quotas, await after.GroupQuotas.CountAsync());
    }

    [Fact]
    public async Task Zones_api_serializes_family_options()
    {
        await factory.WaitForDataAsync();
        var json = await factory.CreateClient().GetStringAsync("/api/v1/zones?region=eastus2");
        Assert.Contains("\"familyId\":\"standard", json);
        Assert.DoesNotContain("[{},", json);
    }

    [Fact]
    public async Task Csv_export_contains_every_matching_row_regardless_of_page_size()
    {
        await factory.WaitForDataAsync();
        var client = factory.CreateClient();
        var page = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync("/api/v1/quota/subscriptions?pageSize=1"));
        var total = page.RootElement.GetProperty("total").GetInt32();
        var csv = await client.GetStringAsync("/api/v1/quota/subscriptions?format=csv&pageSize=1");
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(total > 1);
        Assert.Equal(total + 1, lines.Length);
    }
}
