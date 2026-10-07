namespace Aqr.Core.Sources;

/// <summary>
/// Deterministic synthetic data (Contoso tenant) for local development and demos. Shapes match the real APIs,
/// so the full sync, diff and temporal-history pipeline runs unchanged. Usage drifts a little every hour so
/// history accumulates; everything else is stable for a given day.
/// </summary>
public sealed class MockQuotaSource(TimeProvider time) : IQuotaSource
{
    public SourceStats Stats { get; } = new();

    private sealed record Sub(string Id, string Name, string[] MgChain, string[] Regions, string[] Families, string? Group);

    private static readonly (string Name, string Display, string Geo)[] Regions =
    [
        ("eastus", "East US", "US"), ("eastus2", "East US 2", "US"), ("centralus", "Central US", "US"),
        ("westus3", "West US 3", "US"), ("westcentralus", "West Central US", "US"),
        ("northeurope", "North Europe", "Europe"), ("westeurope", "West Europe", "Europe"),
        ("swedencentral", "Sweden Central", "Europe"), ("uksouth", "UK South", "UK"),
        ("southeastasia", "Southeast Asia", "Asia Pacific"), ("japaneast", "Japan East", "Asia Pacific"),
    ];
    private static readonly HashSet<string> NoZoneRegions = ["westcentralus"];

    // family quota name → SKU names (vCPU parsed from name for capabilities)
    private static readonly Dictionary<string, (string Sku, int Vcpu, int Gpu)[]> Skus = new()
    {
        ["standardDSv5Family"] = S("Standard_D{0}s_v5", 2, 4, 8, 16, 32, 48, 64, 96),
        ["standardDDSv5Family"] = S("Standard_D{0}ds_v5", 2, 4, 8, 16, 32, 48, 64, 96),
        ["standardDASv5Family"] = S("Standard_D{0}as_v5", 2, 4, 8, 16, 32, 48, 64, 96),
        ["standardDSv6Family"] = S("Standard_D{0}s_v6", 2, 4, 8, 16, 32, 48, 64, 96, 128),
        ["standardDASv6Family"] = S("Standard_D{0}as_v6", 2, 4, 8, 16, 32, 48, 64, 96),
        ["standardDPSv6Family"] = S("Standard_D{0}ps_v6", 2, 4, 8, 16, 32, 48, 64, 96),
        ["standardDPSv5Family"] = S("Standard_D{0}ps_v5", 2, 4, 8, 16, 32, 48, 64),
        ["standardDCASv5Family"] = S("Standard_DC{0}as_v5", 2, 4, 8, 16, 32, 48, 64, 96),
        ["standardBsv2Family"] = S("Standard_B{0}s_v2", 2, 4, 8, 16, 32),
        ["standardDSv3Family"] = S("Standard_D{0}s_v3", 2, 4, 8, 16, 32, 48, 64),
        ["standardBSFamily"] = S("Standard_B{0}ms", 1, 2, 4, 8, 12, 16, 20),
        ["standardESv5Family"] = S("Standard_E{0}s_v5", 2, 4, 8, 16, 20, 32, 48, 64, 96, 104),
        ["standardEDSv5Family"] = S("Standard_E{0}ds_v5", 2, 4, 8, 16, 20, 32, 48, 64, 96, 104),
        ["standardEASv5Family"] = S("Standard_E{0}as_v5", 2, 4, 8, 16, 20, 32, 48, 64, 96),
        ["standardESv6Family"] = S("Standard_E{0}s_v6", 2, 4, 8, 16, 20, 32, 48, 64, 96, 128),
        ["standardESv3Family"] = S("Standard_E{0}s_v3", 2, 4, 8, 16, 20, 32, 48, 64),
        ["standardMSFamily"] = S("Standard_M{0}s", 8, 16, 32, 64, 128),
        ["standardFSv2Family"] = S("Standard_F{0}s_v2", 2, 4, 8, 16, 32, 48, 64, 72),
        ["standardFASv6Family"] = S("Standard_F{0}as_v6", 2, 4, 8, 16, 32, 48, 64),
        ["standardLSv3Family"] = S("Standard_L{0}s_v3", 8, 16, 32, 48, 64, 80),
        ["standardLASv3Family"] = S("Standard_L{0}as_v3", 8, 16, 32, 48, 64, 80),
        ["standardHBv4Family"] = [("Standard_HB176rs_v4", 176, 0), ("Standard_HB176-96rs_v4", 96, 0), ("Standard_HB176-48rs_v4", 48, 0)],
        ["standardNCADSH100v5Family"] = [("Standard_NC40ads_H100_v5", 40, 1), ("Standard_NC80adis_H100_v5", 80, 2)],
        ["standardNDSH100v5Family"] = [("Standard_ND96isr_H100_v5", 96, 8)],
        ["standardNDAMSv4_A100Family"] = [("Standard_ND96amsr_A100_v4", 96, 8)],
        ["standardNCASv3_T4Family"] = [("Standard_NC4as_T4_v3", 4, 1), ("Standard_NC8as_T4_v3", 8, 1), ("Standard_NC16as_T4_v3", 16, 1), ("Standard_NC64as_T4_v3", 64, 4)],
        ["standardNVADSA10v5Family"] = [("Standard_NV6ads_A10_v5", 6, 1), ("Standard_NV12ads_A10_v5", 12, 1), ("Standard_NV36ads_A10_v5", 36, 1), ("Standard_NV72ads_A10_v5", 72, 2)],
    };

    private static (string, int, int)[] S(string fmt, params int[] sizes) => sizes.Select(n => (string.Format(fmt, n), n, 0)).ToArray();

    private static readonly Sub[] Subs =
    [
        new("a1f30000-0000-4000-8000-000000000001", "sub-prod-core-01", ["mg-contoso", "mg-platform", "mg-prod"], ["eastus2", "centralus", "westeurope", "northeurope"],
            ["standardDSv5Family", "standardDDSv5Family", "standardDASv5Family", "standardDSv6Family", "standardDASv6Family", "standardESv5Family", "standardEASv5Family", "standardDSv3Family", "standardFASv6Family", "standardDCASv5Family"], "qg-prod-compute"),
        new("b27c0000-0000-4000-8000-000000000002", "sub-prod-data-02", ["mg-contoso", "mg-platform", "mg-prod"], ["eastus2", "westus3"],
            ["standardESv5Family", "standardEDSv5Family", "standardESv6Family", "standardLSv3Family", "standardLASv3Family", "standardMSFamily", "standardESv3Family", "standardDSv5Family"], "qg-prod-compute"),
        new("c90e0000-0000-4000-8000-000000000003", "sub-sap-prod-01", ["mg-contoso", "mg-platform", "mg-sap"], ["eastus2", "westeurope"],
            ["standardMSFamily", "standardESv5Family", "standardEDSv5Family", "standardDSv5Family"], "qg-prod-compute"),
        new("d4b10000-0000-4000-8000-000000000004", "sub-dr-01", ["mg-contoso", "mg-platform", "mg-prod"], ["centralus", "northeurope"],
            ["standardDSv5Family", "standardESv5Family", "standardDASv5Family", "standardFSv2Family"], "qg-prod-compute"),
        new("e6a20000-0000-4000-8000-000000000005", "sub-ai-train-01", ["mg-contoso", "mg-ai"], ["eastus2", "swedencentral", "westus3"],
            ["standardNDSH100v5Family", "standardNDAMSv4_A100Family", "standardNCADSH100v5Family", "standardHBv4Family", "standardDSv5Family"], "qg-ai-gpu"),
        new("f81d0000-0000-4000-8000-000000000006", "sub-ai-infer-02", ["mg-contoso", "mg-ai"], ["eastus2", "eastus", "swedencentral", "japaneast"],
            ["standardNCASv3_T4Family", "standardNVADSA10v5Family", "standardNCADSH100v5Family", "standardDASv5Family", "standardDSv6Family"], "qg-ai-gpu"),
        new("0c5e0000-0000-4000-8000-000000000007", "sub-dev-shared-01", ["mg-contoso", "mg-nonprod"], ["eastus", "westcentralus", "uksouth"],
            ["standardBsv2Family", "standardBSFamily", "standardDSv5Family", "standardDPSv6Family", "standardDPSv5Family", "standardFSv2Family", "standardDSv3Family"], null),
        new("19aa0000-0000-4000-8000-000000000008", "sub-sandbox-01", ["mg-contoso", "mg-nonprod", "mg-sandbox"], ["southeastasia", "eastus"],
            ["standardDSv5Family", "standardDPSv6Family", "standardBsv2Family", "standardFASv6Family", "standardEASv5Family"], null),
    ];

    private static readonly (string Mg, string Name, string Display)[] Groups =
    [
        ("mg-platform", "qg-prod-compute", "Production compute"),
        ("mg-ai", "qg-ai-gpu", "AI GPU pool"),
    ];

    private static int Hash(params object[] parts)
    {
        unchecked
        {
            var h = 17;
            foreach (var p in parts) foreach (var c in p.ToString()!) h = h * 31 + c;
            return h & 0x7fffffff;
        }
    }

    private static long FamilyLimit(Sub s, string region, string family)
    {
        var limits = Skus[family][0].Gpu > 0 ? new long[] { 96, 192, 384, 768, 1152 } : [20, 50, 100, 200, 350, 500, 1000, 1500, 2000];
        return limits[Hash(s.Id, region, family, "lim") % limits.Length];
    }

    private long FamilyUsage(Sub s, string region, string family, long limit)
    {
        var bucket = Hash(s.Id, region, family, "use") % 100;
        if (bucket < 12) return 0;
        if (bucket < 20) return limit;
        var hour = time.GetUtcNow().ToUnixTimeSeconds() / 3600;
        var baseFrac = 0.15 + (bucket % 70) / 100.0;
        var drift = ((Hash(s.Id, family, hour) % 9) - 4) / 100.0;   // ±4 % per hour
        var step = Math.Max(1, Skus[family][0].Vcpu);
        var use = (long)Math.Round(Math.Clamp(baseFrac + drift, 0, 1) * limit / step) * step;
        return Math.Clamp(use, 0, limit);
    }

    private bool Present(Sub s, string region, string family) => Hash(s.Id, region, family, "present") % 100 >= 15;

    public Task<IReadOnlyList<SubscriptionInfo>> GetSubscriptionsAsync(IReadOnlyList<string> managementGroupIds, IReadOnlyList<string> subscriptionIds, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SubscriptionInfo>>(Subs.Select(s => new SubscriptionInfo(s.Id, s.Name, "Enabled", s.MgChain)).ToList());

    public Task<IReadOnlyList<QuotaUsage>> GetQuotaUsagesAsync(IReadOnlyList<string> subscriptionIds, bool includeEmpty, CancellationToken ct)
    {
        var ids = subscriptionIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = new List<QuotaUsage>();
        foreach (var s in Subs.Where(x => ids.Contains(x.Id)))
            foreach (var r in s.Regions)
                rows.AddRange(UsagesFor(s, r));
        return Task.FromResult<IReadOnlyList<QuotaUsage>>(rows);
    }

    private IEnumerable<QuotaUsage> UsagesFor(Sub s, string region)
    {
        long coresUse = 0, coresLimit = 0;
        foreach (var fam in s.Families.Where(f => Present(s, region, f)))
        {
            var limit = FamilyLimit(s, region, fam);
            var use = FamilyUsage(s, region, fam, limit);
            coresUse += use; coresLimit += limit;
            yield return new QuotaUsage(s.Id, region, fam, Localized(fam), use, limit);
        }
        var regional = Math.Max(coresUse, (long)Math.Ceiling(coresLimit * 0.8 / 50) * 50);
        yield return new QuotaUsage(s.Id, region, "cores", "Total Regional vCPUs", coresUse, regional);
    }

    private static string Localized(string fam) => "Standard " + FamilyCatalogName(fam) + " Family vCPUs";
    private static string FamilyCatalogName(string fam) => fam["standard".Length..^"Family".Length];

    public Task<IReadOnlyList<QuotaUsage>> GetComputeUsagesAsync(string subscriptionId, string region, CancellationToken ct)
    {
        var s = Subs.FirstOrDefault(x => x.Id == subscriptionId);
        return Task.FromResult<IReadOnlyList<QuotaUsage>>(s is null ? [] : UsagesFor(s, region).ToList());
    }

    public Task<IReadOnlyList<LocationInfo>> GetLocationsAsync(string subscriptionId, CancellationToken ct)
    {
        var s = Subs.First(x => x.Id == subscriptionId);
        int[][] perms = [[1, 2, 3], [2, 3, 1], [3, 1, 2], [1, 3, 2], [2, 1, 3], [3, 2, 1]];
        var list = Regions.Select(r =>
        {
            var map = new Dictionary<string, string>();
            if (!NoZoneRegions.Contains(r.Name))
            {
                var p = perms[Hash(s.Id, r.Name, "zmap") % perms.Length];
                for (var i = 0; i < 3; i++) map[(i + 1).ToString()] = $"{r.Name}-az{p[i]}";
            }
            return new LocationInfo(r.Name, r.Display, r.Geo, map);
        }).ToList();
        return Task.FromResult<IReadOnlyList<LocationInfo>>(list);
    }

    public async Task<IReadOnlyList<SkuInfo>> GetVmSkusAsync(string subscriptionId, string region, CancellationToken ct)
    {
        var s = Subs.First(x => x.Id == subscriptionId);
        var locations = await GetLocationsAsync(subscriptionId, ct);
        var map = locations.First(l => l.Name == region).ZoneMappings;
        var physToLogical = map.ToDictionary(kv => kv.Value, kv => kv.Key);
        var result = new List<SkuInfo>();

        foreach (var (family, skus) in Skus)
        {
            var isGpu = skus[0].Gpu > 0;
            var roll = Hash(s.Id, region, family, "restrict") % 100;
            var regionBlocked = (isGpu && roll < 15) || (family is "standardDSv3Family" or "standardBSFamily" or "standardESv3Family" or "standardFSv2Family" && roll < 30);
            // Zone restriction applies to one physical zone so the same datacenter is affected for every subscription.
            var restrictedPhysical = roll is >= 30 and < 55 && !NoZoneRegions.Contains(region) ? $"{region}-az{1 + Hash(region, family) % 3}" : null;

            for (var i = 0; i < skus.Length; i++)
            {
                var (name, vcpu, gpu) = skus[i];
                var offeredPhysical = NoZoneRegions.Contains(region) ? Array.Empty<int>()
                    : isGpu && Hash(region, family, "gpuzones") % 2 == 0 ? [1 + Hash(region, family) % 3, 1 + (Hash(region, family) + 1) % 3]
                    : i == skus.Length - 1 && Hash(region, name) % 3 == 0 ? [1, 2]
                    : [1, 2, 3];
                var offeredLogical = offeredPhysical.Select(p => physToLogical.GetValueOrDefault($"{region}-az{p}", p.ToString())).OrderBy(z => z).ToArray();

                var restrictions = new List<SkuRestrictionInfo>();
                if (regionBlocked) restrictions.Add(new SkuRestrictionInfo("Location", [], "NotAvailableForSubscription"));
                else if (restrictedPhysical is not null && physToLogical.TryGetValue(restrictedPhysical, out var rl) && offeredLogical.Contains(rl))
                    restrictions.Add(new SkuRestrictionInfo("Zone", [rl], "NotAvailableForSubscription"));

                var caps = new Dictionary<string, string>
                {
                    ["vCPUs"] = vcpu.ToString(),
                    ["GPUs"] = gpu.ToString(),
                    ["CpuArchitectureType"] = family.Contains("PS", StringComparison.Ordinal) ? "Arm64" : "x64",
                    ["PremiumIO"] = "True",
                    ["RdmaEnabled"] = name.Contains("r_", StringComparison.Ordinal) || name.Contains("rs_", StringComparison.Ordinal) || name.Contains("sr_", StringComparison.Ordinal) ? "True" : "False",
                };
                result.Add(new SkuInfo(name, family, region, offeredLogical, restrictions, caps));
            }
        }
        return result;
    }

    public Task<IReadOnlyList<QuotaGroupInfo>> GetQuotaGroupsAsync(string managementGroupId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<QuotaGroupInfo>>(Groups.Where(g => g.Mg == managementGroupId || managementGroupId == "mg-contoso")
            .Select(g => new QuotaGroupInfo(g.Mg, g.Name, g.Display)).ToList());

    public Task<IReadOnlyList<string>> GetQuotaGroupSubscriptionsAsync(string managementGroupId, string groupName, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(Subs.Where(s => s.Group == groupName).Select(s => s.Id).ToList());

    public Task<IReadOnlyList<GroupQuotaLimitInfo>> GetGroupQuotaLimitsAsync(string managementGroupId, string groupName, string region, CancellationToken ct)
    {
        var members = Subs.Where(s => s.Group == groupName && s.Regions.Contains(region)).ToArray();
        var result = new List<GroupQuotaLimitInfo>();
        foreach (var fam in members.SelectMany(m => m.Families).Distinct())
        {
            // Members' family quota is fully sourced from the group in this demo tenant.
            var alloc = members.Where(m => m.Families.Contains(fam) && Present(m, region, fam))
                .ToDictionary(m => m.Id, m => FamilyLimit(m, region, fam));
            if (alloc.Count == 0) continue;
            var pool = new long[] { 0, 0, 0, 50, 100, 200 }[Hash(groupName, region, fam) % 6];
            result.Add(new GroupQuotaLimitInfo(region, fam, alloc.Values.Sum() + pool, pool, alloc));
        }
        return Task.FromResult<IReadOnlyList<GroupQuotaLimitInfo>>(result);
    }

    public Task<IReadOnlyList<GroupQuotaUsageInfo>> GetGroupQuotaUsagesAsync(string managementGroupId, string groupName, string region, CancellationToken ct)
    {
        var members = Subs.Where(s => s.Group == groupName && s.Regions.Contains(region)).ToArray();
        var result = members.SelectMany(m => m.Families.Where(f => Present(m, region, f)).Select(f => (m, f)))
            .GroupBy(x => x.f)
            .Select(g => new GroupQuotaUsageInfo(region, g.Key,
                g.Sum(x => FamilyLimit(x.m, region, x.f)),
                g.Sum(x => FamilyUsage(x.m, region, x.f, FamilyLimit(x.m, region, x.f)))))
            .ToList();
        return Task.FromResult<IReadOnlyList<GroupQuotaUsageInfo>>(result);
    }
}
