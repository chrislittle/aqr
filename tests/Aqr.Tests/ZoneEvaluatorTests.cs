using Aqr.Core.Model;
using Aqr.Core.Sources;
using Aqr.Core.Zones;

namespace Aqr.Tests;

public sealed class ZoneEvaluatorTests
{
    private static readonly Dictionary<string, string> Map = new() { ["1"] = "eastus2-az2", ["2"] = "eastus2-az3", ["3"] = "eastus2-az1" };

    private static SkuInfo Sku(string name, string[] zones, params SkuRestrictionInfo[] restrictions) =>
        new(name, "standardDSv5Family", "eastus2", zones, restrictions, new Dictionary<string, string>());

    private static FamilyZoneAccess Eval(IReadOnlyDictionary<string, string> map, params SkuInfo[] skus) =>
        ZoneEvaluator.EvaluateFamily("sub", "eastus2", "standarddsv5family", skus, map).Family;

    [Fact]
    public void All_offered_zones_open() =>
        Assert.Equal(ZoneStatuses.AllZones, Eval(Map, Sku("a", ["1", "2", "3"]), Sku("b", ["1", "2", "3"])).ZoneStatus);

    [Fact]
    public void A_zone_restricted_for_every_sku_is_restricted_and_family_is_partial()
    {
        var z = new SkuRestrictionInfo("Zone", ["2"], "NotAvailableForSubscription");
        var r = Eval(Map, Sku("a", ["1", "2", "3"], z), Sku("b", ["1", "2", "3"], z));
        Assert.Equal(ZoneStatuses.PartialZones, r.ZoneStatus);
        Assert.Equal("1,3", r.OpenZonesLogical);
        Assert.Equal("2", r.RestrictedZonesLogical);
        Assert.Equal("eastus2-az3", r.RestrictedZonesPhysical);
        Assert.Equal("NotAvailableForSubscription", r.ReasonCodes);
    }

    [Fact]
    public void A_zone_open_for_some_skus_only_is_partial()
    {
        var r = Eval(Map, Sku("a", ["1", "2"], new SkuRestrictionInfo("Zone", ["1"], "NotAvailableForSubscription")), Sku("b", ["1", "2"]));
        Assert.Equal(ZoneStatuses.PartialZones, r.ZoneStatus);
        Assert.Equal("1", r.PartialZonesLogical);
        Assert.Equal("2", r.OpenZonesLogical);
    }

    [Fact]
    public void All_offered_zones_restricted_is_no_zones()
    {
        var z = new SkuRestrictionInfo("Zone", ["1", "2", "3"], "NotAvailableForSubscription");
        Assert.Equal(ZoneStatuses.NoZones, Eval(Map, Sku("a", ["1", "2", "3"], z)).ZoneStatus);
    }

    [Fact]
    public void Location_restriction_on_every_sku_blocks_the_region_and_is_listed()
    {
        var loc = new SkuRestrictionInfo("Location", [], "NotAvailableForSubscription");
        var result = ZoneEvaluator.EvaluateFamily("sub", "eastus2", "f", [Sku("a", ["1", "2", "3"], loc)], Map);
        Assert.Equal(ZoneStatuses.RegionBlocked, result.Family.ZoneStatus);
        Assert.Equal(0, result.Family.SkusRegionOpen);
        Assert.Single(result.Restrictions, r => r.RestrictionType == "Location");
    }

    [Fact]
    public void No_zonal_offering_is_regional() =>
        Assert.Equal(ZoneStatuses.Regional, Eval(new Dictionary<string, string>(), Sku("a", [])).ZoneStatus);

    [Fact]
    public void Physical_zones_come_from_the_subscription_mapping_and_unmapped_zones_are_not_guessed()
    {
        var r = Eval(new Dictionary<string, string> { ["1"] = "westus2-az3" }, Sku("a", ["1", "2"]));
        Assert.Equal("westus2-az3,?2", r.OfferedZonesPhysical);
    }

    [Fact]
    public void Evaluate_groups_skus_by_family_case_insensitively()
    {
        var skus = new[]
        {
            new SkuInfo("x1", "standardDSv5Family", "eastus2", ["1"], [], new Dictionary<string, string>()),
            new SkuInfo("x2", "STANDARDDSV5FAMILY", "eastus2", ["1"], [], new Dictionary<string, string>()),
        };
        var r = Assert.Single(ZoneEvaluator.Evaluate("sub", "eastus2", skus, Map));
        Assert.Equal(2, r.Family.SkusTotal);
    }
}
