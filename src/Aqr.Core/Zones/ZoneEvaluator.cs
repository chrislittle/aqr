using Aqr.Core.Model;
using Aqr.Core.Sources;

namespace Aqr.Core.Zones;

/// <summary>
/// Turns the Resource SKUs view of one subscription + region into per-family zonal access (design doc §6).
/// Quota stays regional; this only answers "can this subscription deploy the family's SKUs, and in which zones".
/// Zones in the SKUs API are logical for the calling subscription; physical names come from its zone mapping.
/// </summary>
public static class ZoneEvaluator
{
    public sealed record Result(FamilyZoneAccess Family, IReadOnlyList<SkuRestriction> Restrictions);

    public static IReadOnlyList<Result> Evaluate(
        string subscriptionId,
        string region,
        IReadOnlyList<SkuInfo> skus,
        IReadOnlyDictionary<string, string> logicalToPhysical)
    {
        var results = new List<Result>();
        foreach (var group in skus.GroupBy(s => s.Family.ToLowerInvariant()))
            results.Add(EvaluateFamily(subscriptionId, region, group.Key, group.ToList(), logicalToPhysical));
        return results;
    }

    public static Result EvaluateFamily(
        string subscriptionId, string region, string familyId,
        IReadOnlyList<SkuInfo> skus, IReadOnlyDictionary<string, string> map)
    {
        var restrictions = new List<SkuRestriction>();
        int regionOpen = 0;
        // zone -> (offered SKU count, open SKU count)
        var perZone = new SortedDictionary<string, (int Offered, int Open)>(StringComparer.Ordinal);
        var reasons = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var sku in skus)
        {
            var locationBlocked = sku.Restrictions.FirstOrDefault(r => r.Type == "Location");
            var zoneRestricted = sku.Restrictions.Where(r => r.Type == "Zone").SelectMany(r => r.Zones).ToHashSet(StringComparer.Ordinal);

            if (locationBlocked is not null)
            {
                reasons.Add(locationBlocked.ReasonCode);
                restrictions.Add(new SkuRestriction
                {
                    SubscriptionId = subscriptionId, Region = region, Sku = sku.Name, FamilyId = familyId,
                    RestrictionType = "Location", ReasonCode = locationBlocked.ReasonCode,
                });
            }
            else
            {
                regionOpen++;
            }

            foreach (var z in sku.OfferedZones)
            {
                var (offeredCount, openCount) = perZone.TryGetValue(z, out var c) ? c : (0, 0);
                var isOpen = locationBlocked is null && !zoneRestricted.Contains(z);
                perZone[z] = (offeredCount + 1, openCount + (isOpen ? 1 : 0));
            }

            if (locationBlocked is null && zoneRestricted.Count > 0)
            {
                var zr = sku.Restrictions.First(r => r.Type == "Zone");
                reasons.Add(zr.ReasonCode);
                var zones = zoneRestricted.OrderBy(z => z, StringComparer.Ordinal).ToArray();
                restrictions.Add(new SkuRestriction
                {
                    SubscriptionId = subscriptionId, Region = region, Sku = sku.Name, FamilyId = familyId,
                    RestrictionType = "Zone", ReasonCode = zr.ReasonCode,
                    ZonesLogical = string.Join(',', zones),
                    ZonesPhysical = string.Join(',', zones.Select(z => Physical(map, z))),
                });
            }
        }

        var open = perZone.Where(kv => kv.Value.Offered > 0 && kv.Value.Open == kv.Value.Offered).Select(kv => kv.Key).ToArray();
        var partial = perZone.Where(kv => kv.Value.Open > 0 && kv.Value.Open < kv.Value.Offered).Select(kv => kv.Key).ToArray();
        var restricted = perZone.Where(kv => kv.Value.Offered > 0 && kv.Value.Open == 0).Select(kv => kv.Key).ToArray();

        string status;
        if (skus.Count > 0 && regionOpen == 0) status = ZoneStatuses.RegionBlocked;
        else if (perZone.Count == 0) status = ZoneStatuses.Regional;
        else if (open.Length == 0 && partial.Length == 0) status = ZoneStatuses.NoZones;
        else if (partial.Length > 0 || restricted.Length > 0) status = ZoneStatuses.PartialZones;
        else status = ZoneStatuses.AllZones;

        var fza = new FamilyZoneAccess
        {
            SubscriptionId = subscriptionId,
            Region = region,
            FamilyId = familyId,
            ZoneStatus = status,
            SkusTotal = skus.Count,
            SkusRegionOpen = regionOpen,
            OfferedZonesLogical = Join(perZone.Keys),
            OpenZonesLogical = Join(open),
            PartialZonesLogical = Join(partial),
            RestrictedZonesLogical = Join(restricted),
            OfferedZonesPhysical = Join(perZone.Keys.Select(z => Physical(map, z))),
            OpenZonesPhysical = Join(open.Select(z => Physical(map, z))),
            PartialZonesPhysical = Join(partial.Select(z => Physical(map, z))),
            RestrictedZonesPhysical = Join(restricted.Select(z => Physical(map, z))),
            ReasonCodes = Join(reasons.Where(r => r.Length > 0)),
        };
        return new Result(fza, restrictions);
    }

    /// <summary>Unmapped zones are shown as "?n" rather than guessed.</summary>
    private static string Physical(IReadOnlyDictionary<string, string> map, string logical) =>
        map.TryGetValue(logical, out var p) ? p : "?" + logical;

    private static string Join(IEnumerable<string> values) => string.Join(',', values);
}
