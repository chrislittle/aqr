namespace Aqr.Core.Sources;

// DTOs returned by the data sources, shaped like the Azure APIs they come from (design doc §3).

/// <summary>S6: a subscription in scope, from Resource Graph ResourceContainers.</summary>
public sealed record SubscriptionInfo(string SubscriptionId, string Name, string State, IReadOnlyList<string> ManagementGroupAncestors);

/// <summary>S1/S2: one row of microsoft.compute/locations/usages.</summary>
public sealed record QuotaUsage(string SubscriptionId, string Region, string Name, string LocalizedName, long CurrentValue, long Limit);

/// <summary>S5: region metadata and this subscription's logical→physical zone mapping.</summary>
public sealed record LocationInfo(string Name, string DisplayName, string Geography, IReadOnlyDictionary<string, string> ZoneMappings);

/// <summary>S4: a VM SKU from the Compute Resource SKUs API, already filtered to one region.</summary>
public sealed record SkuInfo(
    string Name,
    string Family,
    string Region,
    IReadOnlyList<string> OfferedZones,
    IReadOnlyList<SkuRestrictionInfo> Restrictions,
    IReadOnlyDictionary<string, string> Capabilities)
{
    public int? IntCapability(string name) =>
        Capabilities.TryGetValue(name, out var v) && double.TryParse(v, System.Globalization.CultureInfo.InvariantCulture, out var d) ? (int)d : null;
}

/// <param name="Type">"Location" or "Zone".</param>
/// <param name="Zones">Logical zones restricted (Zone type only).</param>
public sealed record SkuRestrictionInfo(string Type, IReadOnlyList<string> Zones, string ReasonCode);

/// <summary>S3: a quota group under a management group.</summary>
public sealed record QuotaGroupInfo(string ManagementGroupId, string GroupName, string DisplayName);

/// <summary>S3: GroupQuotaLimits_List row (one family in one region).</summary>
public sealed record GroupQuotaLimitInfo(string Region, string ResourceName, long Limit, long AvailableLimit, IReadOnlyDictionary<string, long> AllocatedToSubscriptions);

/// <summary>S3: GroupQuotaUsages_List row.</summary>
public sealed record GroupQuotaUsageInfo(string Region, string ResourceName, long Limit, long Usages);

/// <summary>Raised by sources for throttling accounting.</summary>
public sealed class SourceStats
{
    private int _throttled;
    public int Throttled => _throttled;
    public void RecordThrottle() => Interlocked.Increment(ref _throttled);
}

public interface IQuotaSource
{
    SourceStats Stats { get; }

    Task<IReadOnlyList<SubscriptionInfo>> GetSubscriptionsAsync(IReadOnlyList<string> managementGroupIds, CancellationToken ct);

    /// <summary>Compute quota usages (cores + *Family) for the given subscriptions, from Resource Graph QuotaResources.</summary>
    Task<IReadOnlyList<QuotaUsage>> GetQuotaUsagesAsync(IReadOnlyList<string> subscriptionIds, CancellationToken ct);

    /// <summary>Fallback (S2): Compute Usages API for one subscription and region.</summary>
    Task<IReadOnlyList<QuotaUsage>> GetComputeUsagesAsync(string subscriptionId, string region, CancellationToken ct);

    Task<IReadOnlyList<LocationInfo>> GetLocationsAsync(string subscriptionId, CancellationToken ct);

    Task<IReadOnlyList<SkuInfo>> GetVmSkusAsync(string subscriptionId, string region, CancellationToken ct);

    Task<IReadOnlyList<QuotaGroupInfo>> GetQuotaGroupsAsync(string managementGroupId, CancellationToken ct);

    Task<IReadOnlyList<string>> GetQuotaGroupSubscriptionsAsync(string managementGroupId, string groupName, CancellationToken ct);

    Task<IReadOnlyList<GroupQuotaLimitInfo>> GetGroupQuotaLimitsAsync(string managementGroupId, string groupName, string region, CancellationToken ct);

    Task<IReadOnlyList<GroupQuotaUsageInfo>> GetGroupQuotaUsagesAsync(string managementGroupId, string groupName, string region, CancellationToken ct);
}
