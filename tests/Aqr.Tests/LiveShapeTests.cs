using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Aqr.Core.Model;
using Aqr.Core.Sources;
using Aqr.Core.Zones;

namespace Aqr.Tests;

/// <summary>
/// ArmQuotaSource against real Azure response shapes, captured read-only from a subscription on 2026-10-07
/// (subscription ID replaced). Guards the parsing that the mock can't: odd family names, restriction shapes,
/// zone mappings, retirement dates, Resource Graph paging and scoping.
/// </summary>
public sealed class LiveShapeTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private sealed class Handler(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
    {
        public List<(string Url, string Body)> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((request.RequestUri!.PathAndQuery, body));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(respond(request), Encoding.UTF8, "application/json"), RequestMessage = request };
        }
    }

    private static (ArmQuotaSource Source, Handler Handler) Source(Func<HttpRequestMessage, string> respond)
    {
        var h = new Handler(respond);
        return (new ArmQuotaSource(new HttpClient(h) { BaseAddress = new Uri("https://management.azure.com") }, new NoRawArchive()), h);
    }

    [Theory]
    [InlineData("standardDSv5Family", "standarddsv5family")]
    [InlineData("Standard NCASv3_T4 Family", "standardncasv3_t4family")]
    [InlineData("standard NDAMSv4_A100Family", "standardndamsv4_a100family")]
    [InlineData("StandardDadsv7Family", "standarddadsv7family")]
    public void Family_keys_normalize_azure_naming_variants(string raw, string key) => Assert.Equal(key, Keys.Family(raw));

    [Fact]
    public void Catalog_matches_spaced_family_names()
    {
        var f = TestHost.Catalog().Classify("Standard NCASv3_T4 Family");
        Assert.Equal(("Catalog", "T4", "standardncasv3_t4family"), (f.Classification, f.AcceleratorModel, f.FamilyId));
    }

    [Fact]
    public async Task Skus_parse_restrictions_zones_capabilities_and_retirement()
    {
        var (src, _) = Source(_ => Fixture("skus-eastus2.json"));
        var skus = await src.GetVmSkusAsync("sub", "eastus2", default);
        Assert.Equal(8, skus.Count);

        var d1 = skus.Single(s => s.Name == "Standard_D1");
        Assert.Equal(["1", "2", "3"], Assert.Single(d1.Restrictions).Zones);
        Assert.Equal("Zone", d1.Restrictions[0].Type);

        var promo = skus.Single(s => s.Name == "Standard_D11_v2_Promo");
        Assert.Contains(promo.Restrictions, r => r.Type == "Location");

        var t4 = skus.Single(s => s.Name == "Standard_NC4as_T4_v3");
        Assert.Equal("Standard NCASv3_T4 Family", t4.Family);
        Assert.True(t4.IntCapability("GPUs") > 0);

        Assert.Equal("Arm64", skus.Single(s => s.Name == "Standard_D2ps_v6").Capabilities["CpuArchitectureType"]);
        Assert.True(skus.Single(s => s.Name == "Standard_D1").Capabilities.ContainsKey("RetirementDateUtc"));

        // Zone status from real shapes: a Location restriction on the only SKU blocks the region.
        var promoFamily = ZoneEvaluator.EvaluateFamily("sub", "eastus2", Keys.Family(promo.Family), [promo], new Dictionary<string, string>());
        Assert.Equal(ZoneStatuses.RegionBlocked, promoFamily.Family.ZoneStatus);
        var d1Family = ZoneEvaluator.EvaluateFamily("sub", "eastus2", Keys.Family(d1.Family), [d1], new Dictionary<string, string> { ["1"] = "eastus2-az1", ["2"] = "eastus2-az2", ["3"] = "eastus2-az3" });
        Assert.Equal(ZoneStatuses.NoZones, d1Family.Family.ZoneStatus);
    }

    [Fact]
    public async Task Locations_keep_physical_regions_and_zone_mappings()
    {
        var (src, _) = Source(_ => Fixture("locations.json"));
        var locs = await src.GetLocationsAsync("sub", default);
        var e2 = locs.Single(l => l.Name == "eastus2");
        Assert.Equal(3, e2.ZoneMappings.Count);
        Assert.Equal("eastus2-az1", e2.ZoneMappings["1"]);
        Assert.Equal("US", e2.Geography);
        Assert.Empty(locs.Single(l => l.Name == "westcentralus").ZoneMappings);
    }

    [Fact]
    public async Task Resource_graph_is_scoped_paged_and_filters_empty_quota()
    {
        var page2 = Fixture("arg-quota.json");
        var page1 = JsonNode.Parse(page2)!.AsObject();
        page1["$skipToken"] = "next";
        var (src, h) = Source(req => h0(req));
        string h0(HttpRequestMessage _) => throw new InvalidOperationException();
        var calls = 0;
        (src, h) = Source(_ => calls++ == 0 ? page1.ToJsonString() : page2);

        var rows = await src.GetQuotaUsagesAsync(["00000000-0000-0000-0000-000000000001"], includeEmpty: false, default);
        Assert.Equal(2, h.Calls.Count);
        Assert.Contains("\"$skipToken\":\"next\"", h.Calls[1].Body);
        Assert.All(h.Calls, c => Assert.Contains("\"subscriptions\":[\"00000000-0000-0000-0000-000000000001\"]", c.Body));
        Assert.Contains("qlimit > 0 or usage > 0", JsonNode.Parse(h.Calls[0].Body)!["query"]!.GetValue<string>());
        Assert.Contains(rows, r => r.Name == "Standard NCASv3_T4 Family");
    }

    [Fact]
    public async Task Inventory_refuses_to_run_unscoped()
    {
        var (src, h) = Source(_ => "{}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => src.GetSubscriptionsAsync([], [], default));
        Assert.Empty(h.Calls);
    }

    [Fact]
    public async Task Inventory_with_explicit_subscriptions_scopes_the_query()
    {
        var (src, h) = Source(_ => """{"data":[{"subscriptionId":"s1","name":"one","state":"Enabled","chain":[]}]}""");
        var subs = await src.GetSubscriptionsAsync([], ["s1"], default);
        Assert.Equal("s1", Assert.Single(subs).SubscriptionId);
        Assert.Contains("\"subscriptions\":[\"s1\"]", Assert.Single(h.Calls).Body);
        Assert.DoesNotContain("managementGroups", h.Calls[0].Body);
    }
}
