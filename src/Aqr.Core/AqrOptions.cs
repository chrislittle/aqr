namespace Aqr.Core;

/// <summary>Bound from the "Aqr" configuration section (app settings use Aqr__*).</summary>
public sealed class AqrOptions
{
    public const string Section = "Aqr";

    /// <summary>Use the deterministic synthetic data source instead of Azure APIs (local dev and demos).</summary>
    public bool UseMock { get; set; }

    /// <summary>Comma-separated management group IDs whose subscriptions are reported on.</summary>
    public string ManagementGroupIds { get; set; } = "";

    /// <summary>
    /// Optional comma-separated subscription IDs to report on, in addition to (or instead of) management groups.
    /// Resource Graph queries are always scoped to these and/or the management groups — never tenant-wide.
    /// </summary>
    public string SubscriptionIds { get; set; } = "";

    /// <summary>Optional comma-separated region allow-list. Empty = every region the APIs return (decision D2).</summary>
    public string Regions { get; set; } = "";

    /// <summary>Temporal history retention applied to every system-versioned table, e.g. "1 YEAR" (decision D3).</summary>
    public string HistoryRetention { get; set; } = "1 YEAR";

    /// <summary>
    /// Keep family quota rows with limit 0 and usage 0. Off by default: they carry no information and were ~25 % of
    /// rows observed live (10,284 rows for one subscription). "Total Regional vCPUs" rows are always kept.
    /// </summary>
    public bool IncludeEmptyQuota { get; set; }

    public SyncOptions Sync { get; set; } = new();
    public RawArchiveOptions RawArchive { get; set; } = new();

    public IReadOnlyList<string> ManagementGroupIdList => Split(ManagementGroupIds);
    public IReadOnlyList<string> RegionList => Split(Regions);
    public IReadOnlyList<string> SubscriptionIdList => Split(SubscriptionIds);

    private static IReadOnlyList<string> Split(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
             .Distinct(StringComparer.OrdinalIgnoreCase)
             .ToArray();
}

public sealed class SyncOptions
{
    public bool Enabled { get; set; } = true;
    public TimeSpan QuotaInterval { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan GroupInterval { get; set; } = TimeSpan.FromHours(4);
    public TimeSpan ZoneInterval { get; set; } = TimeSpan.FromDays(1);
    public int MaxParallel { get; set; } = 4;
    /// <summary>Subscriptions processed per database round (bounds memory during diffing; ~7.7k quota rows per subscription were observed live).</summary>
    public int SubscriptionBatchSize { get; set; } = 5;
}

public sealed class RawArchiveOptions
{
    /// <summary>Blob service endpoint, e.g. https://account.blob.core.windows.net/. Empty disables the archive.</summary>
    public string BlobServiceUri { get; set; } = "";
    public string Container { get; set; } = "raw";
}
