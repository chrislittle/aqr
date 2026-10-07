using Aqr.Core.Model;

namespace Aqr.Core.Reporting;

/// <summary>
/// Explorer / API filter set (design doc §7). One parser for the UI query string and /api/v1, so the UI's
/// "API" line is always the exact call that reproduces the view.
/// </summary>
public sealed class ExplorerFilter
{
    public string Kind { get; init; } = QuotaKinds.Family;
    public string[] Subscriptions { get; init; } = [];
    public string[] QuotaGroups { get; init; } = [];      // group names; "(none)" = not in any group
    public string[] Geographies { get; init; } = [];
    public string[] Regions { get; init; } = [];
    public string[] ZoneStatuses { get; init; } = [];
    public string[] OpenInZones { get; init; } = [];      // "1","2","3" (logical) or "az1".. (physical)
    public string[] Categories { get; init; } = [];
    public string[] CpuManufacturers { get; init; } = [];
    public string[] Architectures { get; init; } = [];
    public string[] Accelerators { get; init; } = [];     // None, GPU, FPGA
    public string[] GpuModels { get; init; } = [];
    public string[] Generations { get; init; } = [];
    public string[] Features { get; init; } = [];         // all-of
    public string[] Lifecycles { get; init; } = [];
    public int MinUtil { get; init; }
    public long MinAvailable { get; init; }
    public string Search { get; init; } = "";
    public DateTime? AsOf { get; init; }
    public string ZoneMode { get; init; } = "logical";
    public string Sort { get; init; } = "util";
    public bool Descending { get; init; } = true;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 200;

    public bool IsPhysical => ZoneMode.Equals("physical", StringComparison.OrdinalIgnoreCase);

    public static readonly string[] Keys =
        ["kind", "sub", "group", "geo", "reg", "zstat", "zopen", "cat", "cpu", "arch", "acc", "gpu", "gen", "feat", "life", "minUtil", "minAvailable", "q", "asOf", "zoneMode", "sort", "dir", "page", "pageSize"];

    /// <summary>Parses from any key→values source (ASP.NET query collection, tests).</summary>
    public static ExplorerFilter Parse(Func<string, IEnumerable<string?>> get)
    {
        string[] List(string k) => get(k).Where(v => !string.IsNullOrWhiteSpace(v))
            .SelectMany(v => v!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(v => v.Length > 128 ? v[..128] : v)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToArray();
        string One(string k) => get(k).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";

        var kind = One("kind").Equals(QuotaKinds.RegionalTotal, StringComparison.OrdinalIgnoreCase) || One("kind").Equals("cores", StringComparison.OrdinalIgnoreCase)
            ? QuotaKinds.RegionalTotal : QuotaKinds.Family;
        var sort = One("sort").ToLowerInvariant() is var s && AllowedSorts.Contains(s) ? s : "util";
        DateTime? asOf = DateTime.TryParse(One("asOf"), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : null;

        return new ExplorerFilter
        {
            Kind = kind,
            Subscriptions = List("sub").Select(x => x.ToLowerInvariant()).ToArray(),
            QuotaGroups = List("group").Select(x => x.ToLowerInvariant()).ToArray(),
            Geographies = List("geo"),
            Regions = List("reg").Select(x => x.ToLowerInvariant()).ToArray(),
            ZoneStatuses = List("zstat").Where(x => Model.ZoneStatuses.All.Contains(x, StringComparer.OrdinalIgnoreCase)).ToArray(),
            OpenInZones = List("zopen").Where(z => z is "1" or "2" or "3" or "az1" or "az2" or "az3").ToArray(),
            Categories = List("cat"),
            CpuManufacturers = List("cpu"),
            Architectures = List("arch"),
            Accelerators = List("acc"),
            GpuModels = List("gpu"),
            Generations = List("gen"),
            Features = List("feat"),
            Lifecycles = List("life"),
            MinUtil = int.TryParse(One("minUtil"), out var mu) ? Math.Clamp(mu, 0, 100) : 0,
            MinAvailable = long.TryParse(One("minAvailable"), out var ma) ? Math.Max(0, ma) : 0,
            Search = One("q") is { Length: > 100 } q ? q[..100] : One("q"),
            AsOf = asOf,
            ZoneMode = One("zoneMode").Equals("physical", StringComparison.OrdinalIgnoreCase) ? "physical" : "logical",
            Sort = sort,
            Descending = !One("dir").Equals("asc", StringComparison.OrdinalIgnoreCase),
            Page = int.TryParse(One("page"), out var p) ? Math.Max(1, p) : 1,
            PageSize = int.TryParse(One("pageSize"), out var ps) ? Math.Clamp(ps, 1, 5000) : 200,
        };
    }

    public static readonly HashSet<string> AllowedSorts = ["util", "available", "limit", "usage", "sub", "region", "family"];

    /// <summary>Query string reproducing this filter (for links, the API line and CSV).</summary>
    public string ToQueryString(params (string Key, string? Value)[] overrides)
    {
        var pairs = new List<(string, string)>();
        void Add(string k, IEnumerable<string> v) { foreach (var x in v) pairs.Add((k, x)); }
        if (Kind != QuotaKinds.Family) pairs.Add(("kind", Kind));
        Add("sub", Subscriptions); Add("group", QuotaGroups); Add("geo", Geographies); Add("reg", Regions);
        Add("zstat", ZoneStatuses); Add("zopen", OpenInZones); Add("cat", Categories); Add("cpu", CpuManufacturers);
        Add("arch", Architectures); Add("acc", Accelerators); Add("gpu", GpuModels); Add("gen", Generations);
        Add("feat", Features); Add("life", Lifecycles);
        if (MinUtil > 0) pairs.Add(("minUtil", MinUtil.ToString()));
        if (MinAvailable > 0) pairs.Add(("minAvailable", MinAvailable.ToString()));
        if (Search.Length > 0) pairs.Add(("q", Search));
        if (AsOf is { } a) pairs.Add(("asOf", a.ToString("yyyy-MM-ddTHH:mm:ssZ")));
        if (IsPhysical) pairs.Add(("zoneMode", "physical"));
        if (Sort != "util") pairs.Add(("sort", Sort));
        if (!Descending) pairs.Add(("dir", "asc"));
        foreach (var (k, v) in overrides)
        {
            pairs.RemoveAll(p => p.Item1 == k);
            if (v is not null) pairs.Add((k, v));
        }
        return string.Join('&', pairs.Select(p => $"{p.Item1}={Uri.EscapeDataString(p.Item2)}"));
    }
}
