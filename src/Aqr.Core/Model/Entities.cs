namespace Aqr.Core.Model;

// Fact and dimension entities. Every type marked [Temporal] in AqrDbContext is a system-versioned
// temporal table: SQL keeps the prior version of a row in its history table whenever the row changes
// or is deleted. Volatile columns ("last seen") must never live on these types (design doc §8 rule 2).

public sealed class Subscription
{
    public string SubscriptionId { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Management group ancestry as "/root/.../parent/" (IDs, trailing slash) for prefix matching.</summary>
    public string ManagementGroupPath { get; set; } = "/";
    public string State { get; set; } = "";
}

public sealed class Region
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Geography { get; set; } = "";
    public bool HasZones { get; set; }
}

public sealed class VmFamily
{
    /// <summary>Quota name, lower-cased (e.g. "standarddsv5family"). Join key across all tables.</summary>
    public string FamilyId { get; set; } = "";
    /// <summary>Quota name as Azure reports it (e.g. "standardDSv5Family").</summary>
    public string QuotaName { get; set; } = "";
    public string LocalizedName { get; set; } = "";
    public string Series { get; set; } = "";
    public string Category { get; set; } = "Unclassified";
    public string CpuManufacturer { get; set; } = "Unclassified";
    public string Architecture { get; set; } = "x64";
    public string AcceleratorType { get; set; } = "None";
    public string AcceleratorVendor { get; set; } = "";
    public string AcceleratorModel { get; set; } = "";
    public string Generation { get; set; } = "";
    /// <summary>Comma-separated feature tags: premiumSsd, localDisk, rdma, confidential, burstable.</summary>
    public string Features { get; set; } = "";
    public string Lifecycle { get; set; } = "Current";
    /// <summary>Earliest SKU retirement date for the family, from the Resource SKUs "RetirementDateUtc" capability (9999-01-01 = none announced).</summary>
    public DateTime? RetirementDate { get; set; }
    public int MinVcpu { get; set; }
    public int MaxVcpu { get; set; }
    public int SkuCount { get; set; }
    /// <summary>"Catalog" when the attributes come from catalog/vm-families.json, "Derived" when inferred from the name.</summary>
    public string Classification { get; set; } = "Derived";
    public string SourceUrl { get; set; } = "";
}

public static class QuotaKinds
{
    public const string Family = "Family";
    public const string RegionalTotal = "RegionalTotal";
}

public sealed class SubscriptionQuota
{
    public string SubscriptionId { get; set; } = "";
    public string Region { get; set; } = "";
    /// <summary>Lower-cased quota name ("cores" or a family quota name).</summary>
    public string QuotaName { get; set; } = "";
    /// <summary>The name exactly as Azure returned it (casing/spacing vary), for display.</summary>
    public string RawName { get; set; } = "";
    public string Kind { get; set; } = QuotaKinds.Family;
    public string? FamilyId { get; set; }
    public string LocalizedName { get; set; } = "";
    public long Usage { get; set; }
    public long Limit { get; set; }
}

public sealed class QuotaGroup
{
    public string ManagementGroupId { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public sealed class QuotaGroupMember
{
    public string ManagementGroupId { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string SubscriptionId { get; set; } = "";
}

public sealed class GroupQuota
{
    public string ManagementGroupId { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string Region { get; set; } = "";
    public string FamilyId { get; set; } = "";
    public long GroupLimit { get; set; }
    /// <summary>Unallocated pool (API "availableLimit"). Not deployable until allocated to a subscription.</summary>
    public long AvailableLimit { get; set; }
    public long AllocatedTotal { get; set; }
    public long? GroupUsage { get; set; }
}

public sealed class GroupAllocation
{
    public string ManagementGroupId { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string SubscriptionId { get; set; } = "";
    public string Region { get; set; } = "";
    public string FamilyId { get; set; } = "";
    public long QuotaAllocated { get; set; }
}

public static class ZoneStatuses
{
    public const string Regional = "Regional";
    public const string AllZones = "AllZones";
    public const string PartialZones = "PartialZones";
    public const string NoZones = "NoZones";
    public const string RegionBlocked = "RegionBlocked";
    /// <summary>Quota exists but no SKU of the family is offered to the subscription in the region (observed live for ~30 % of family quotas).</summary>
    public const string NotOffered = "NotOffered";
    public static readonly string[] All = [AllZones, PartialZones, NoZones, RegionBlocked, Regional, NotOffered];
}

public sealed class FamilyZoneAccess
{
    public string SubscriptionId { get; set; } = "";
    public string Region { get; set; } = "";
    public string FamilyId { get; set; } = "";
    public string ZoneStatus { get; set; } = ZoneStatuses.Regional;
    public int SkusTotal { get; set; }
    public int SkusRegionOpen { get; set; }
    /// <summary>Logical zones (for this subscription) where at least one SKU of the family is offered, e.g. "1,2,3".</summary>
    public string OfferedZonesLogical { get; set; } = "";
    /// <summary>Logical zones where every offered SKU is open.</summary>
    public string OpenZonesLogical { get; set; } = "";
    /// <summary>Logical zones where some, but not all, offered SKUs are open.</summary>
    public string PartialZonesLogical { get; set; } = "";
    /// <summary>Logical zones where no offered SKU is open for this subscription.</summary>
    public string RestrictedZonesLogical { get; set; } = "";
    public string OfferedZonesPhysical { get; set; } = "";
    public string OpenZonesPhysical { get; set; } = "";
    public string PartialZonesPhysical { get; set; } = "";
    public string RestrictedZonesPhysical { get; set; } = "";
    public string ReasonCodes { get; set; } = "";
}

public sealed class SkuRestriction
{
    public string SubscriptionId { get; set; } = "";
    public string Region { get; set; } = "";
    public string Sku { get; set; } = "";
    public string FamilyId { get; set; } = "";
    /// <summary>"Location" (whole region blocked) or "Zone".</summary>
    public string RestrictionType { get; set; } = "";
    public string ZonesLogical { get; set; } = "";
    public string ZonesPhysical { get; set; } = "";
    public string ReasonCode { get; set; } = "";
}

public sealed class ZoneMapping
{
    public string SubscriptionId { get; set; } = "";
    public string Region { get; set; } = "";
    public string LogicalZone { get; set; } = "";
    public string PhysicalZone { get; set; } = "";
}

public static class GrantTargetTypes
{
    public const string ManagementGroup = "ManagementGroup";
    public const string QuotaGroup = "QuotaGroup";
    public const string Subscription = "Subscription";
    public static readonly string[] All = [ManagementGroup, QuotaGroup, Subscription];
}

/// <summary>Report-visibility grant (decision D6). The temporal history of this table is the audit trail.</summary>
public sealed class AccessGrant
{
    public Guid GrantId { get; set; }
    public string PrincipalObjectId { get; set; } = "";
    /// <summary>"Group" or "User".</summary>
    public string PrincipalType { get; set; } = "Group";
    public string PrincipalDisplayName { get; set; } = "";
    public string TargetType { get; set; } = GrantTargetTypes.ManagementGroup;
    /// <summary>MG ID, subscription ID, or "{mgId}/{groupName}" for a quota group.</summary>
    public string TargetId { get; set; } = "";
    public string Note { get; set; } = "";
    public string ChangedBy { get; set; } = "";
}

public static class SyncStages
{
    public const string Inventory = "Inventory";
    public const string SubQuota = "SubQuota";
    public const string Groups = "Groups";
    public const string Zones = "Zones";
    public const string Catalog = "Catalog";
}

public sealed class SyncRun
{
    public Guid RunId { get; set; }
    public string Stage { get; set; } = "";
    public string Trigger { get; set; } = "Schedule";
    public DateTime StartedUtc { get; set; }
    public DateTime? EndedUtc { get; set; }
    /// <summary>Running, Succeeded, Warning, Failed.</summary>
    public string Status { get; set; } = "Running";
    public int SubsExpected { get; set; }
    public int SubsCovered { get; set; }
    public int KeysSeen { get; set; }
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Deleted { get; set; }
    public int Throttled { get; set; }
    public string Message { get; set; } = "";
}

public sealed class SyncCoverage
{
    public Guid RunId { get; set; }
    public string Stage { get; set; } = "";
    public string SubscriptionId { get; set; } = "";
    public string Status { get; set; } = "";
    public string Error { get; set; } = "";
}
