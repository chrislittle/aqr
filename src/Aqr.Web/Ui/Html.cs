using System.Globalization;
using System.Net;
using System.Text;
using Aqr.Core.Model;
using Aqr.Core.Reporting;
using Microsoft.AspNetCore.Html;

namespace Aqr.Web.Ui;

/// <summary>Small HTML fragments shared by the pages (design system from docs/mockups).</summary>
public static class U
{
    public const string CapacityRestrictedUrl = "https://learn.microsoft.com/azure/virtual-machines/sizes/lifecycle/retirements-and-capacity-restrictions";

    public static string N(long v) => v.ToString("N0", CultureInfo.InvariantCulture);
    public static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    public static readonly IReadOnlyDictionary<string, string> CategoryLabels = new Dictionary<string, string>
    {
        ["GeneralPurpose"] = "General purpose", ["ComputeOptimized"] = "Compute optimized", ["MemoryOptimized"] = "Memory optimized",
        ["StorageOptimized"] = "Storage optimized", ["GpuAccelerated"] = "GPU accelerated", ["FpgaAccelerated"] = "FPGA accelerated",
        ["HighPerformanceCompute"] = "HPC", ["Unclassified"] = "Unclassified",
    };
    public static string Cat(string c) => CategoryLabels.TryGetValue(c, out var l) ? l : c;

    public static readonly IReadOnlyDictionary<string, string> CpuLabels = new Dictionary<string, string>
    {
        ["Intel"] = "Intel", ["AMD"] = "AMD", ["Microsoft"] = "Microsoft Cobalt", ["Ampere"] = "Ampere", ["Unclassified"] = "Unclassified",
    };
    public static readonly IReadOnlyDictionary<string, string> CpuColors = new Dictionary<string, string>
    {
        ["Intel"] = "#2563eb", ["AMD"] = "#e0533d", ["Microsoft"] = "#0f9d58", ["Ampere"] = "#7c3aed", ["Unclassified"] = "#94a3b8",
    };
    private static readonly IReadOnlyDictionary<string, string> CpuClass = new Dictionary<string, string>
    {
        ["Intel"] = "c-intel", ["AMD"] = "c-amd", ["Microsoft"] = "c-msft", ["Ampere"] = "c-ampere",
    };
    public static readonly IReadOnlyDictionary<string, string> FeatureLabels = new Dictionary<string, string>
    {
        ["premiumSsd"] = "Premium SSD", ["localDisk"] = "Local disk", ["rdma"] = "RDMA", ["confidential"] = "Confidential", ["burstable"] = "Burstable",
    };

    public static string FillClass(double p) => p >= 100 ? "f-over" : p >= 90 ? "f-crit" : p >= 75 ? "f-warn" : "f-ok";

    public static IHtmlContent Badge(double p, long limit) => new HtmlString(limit <= 0 ? "<span class=\"badge neutral\">no quota</span>"
        : p >= 100 ? "<span class=\"badge over\">at limit</span>"
        : $"<span class=\"badge {(p >= 90 ? "crit" : p >= 75 ? "warnb" : "ok")}\">{p:0}%</span>");

    public static IHtmlContent Meter(long use, long limit)
    {
        var p = limit > 0 ? 100.0 * use / limit : 0;
        return new HtmlString($"<div class=\"mcell\"><div class=\"t\"><b>{N(use)}</b> / {N(limit)} vCPU</div><div class=\"meter\"><i class=\"{FillClass(p)}\" style=\"width:{Math.Min(100, p).ToString("0.#", CultureInfo.InvariantCulture)}%\"></i></div></div>");
    }

    public static IHtmlContent FamilyChips(ExplorerRow r) => FamilyChips(r.CpuManufacturer, r.Architecture, r.AcceleratorType, r.AcceleratorVendor, r.AcceleratorModel, r.Features, r.Lifecycle, r.Classification);

    public static IHtmlContent FamilyChips(string cpu, string arch, string accType, string accVendor, string accModel, string features, string lifecycle, string classification)
    {
        var sb = new StringBuilder();
        sb.Append($"<span class=\"chip {CpuClass.GetValueOrDefault(cpu, "c-feat")}\">{E(CpuLabels.GetValueOrDefault(cpu, cpu))}</span>");
        if (arch == "Arm64") sb.Append("<span class=\"chip c-arm\">Arm64</span>");
        if (accType != "None" && accType.Length > 0) sb.Append($"<span class=\"chip c-gpu\">{E((accVendor + " " + accModel).Trim() is { Length: > 0 } m ? m : accType)}</span>");
        foreach (var f in features.Split(',', StringSplitOptions.RemoveEmptyEntries).Where(f => f is "rdma" or "confidential"))
            sb.Append($"<span class=\"chip c-feat\">{FeatureLabels[f]}</span>");
        if (lifecycle == "Capacity-restricted")
            sb.Append($"<a class=\"chip c-restricted\" href=\"{CapacityRestrictedUrl}\" target=\"_blank\" rel=\"noopener\" title=\"Capacity growth restrictions — Microsoft Learn\">growth-restricted</a>");
        if (classification == "Derived")
            sb.Append("<span class=\"chip c-feat\" title=\"Not in catalog/vm-families.json — attributes inferred from the documented VM naming convention\">derived</span>");
        return new HtmlString(sb.ToString());
    }

    /// <summary>
    /// Zone pills. Quota is regional; these show where the family's SKUs can actually be deployed for this subscription.
    /// Logical mode labels zones as the subscription sees them; physical mode uses datacenter zone IDs (comparable across subscriptions).
    /// </summary>
    public static IHtmlContent ZonePills(string? status, bool physical,
        string offeredL, string openL, string partialL, string restrictedL,
        string offeredP, string openP, string partialP, string restrictedP)
    {
        if (status is null) return new HtmlString("<span class=\"zstat\" title=\"No SKU data for this subscription/region yet (zones sync runs daily)\">not synced</span>");
        if (status == ZoneStatuses.Regional) return new HtmlString("<span class=\"regional\" title=\"Region has no availability zones, or the SKUs aren't offered zonally — regional (non-zonal) deployment only\">regional only</span>");
        if (status == ZoneStatuses.RegionBlocked) return new HtmlString("<span class=\"blocked\" title=\"restrictions[type=Location] for this subscription\">region blocked</span>");
        if (status == ZoneStatuses.NotOffered) return new HtmlString("<span class=\"blocked\" style=\"background:#64748b\" title=\"Quota exists, but the Resource SKUs API offers no SKU of this family to this subscription in this region\">no SKUs offered</span>");

        var offered = Split(physical ? offeredP : offeredL);
        var open = Split(physical ? openP : openL).ToHashSet();
        var partial = Split(physical ? partialP : partialL).ToHashSet();
        var sb = new StringBuilder("<span class=\"zones\">");
        foreach (var z in offered.OrderBy(z => z, StringComparer.Ordinal))
        {
            var label = physical ? z[(z.LastIndexOf('-') + 1)..] : z;
            var (cls, style, tip) = open.Contains(z) ? ("open", "", "all offered SKUs open")
                : partial.Contains(z) ? ("open", " style=\"background:#fff7e6;border-color:#fcd9a8;color:#b45309\"", "some SKUs restricted")
                : ("restr", "", "restricted for this subscription");
            sb.Append($"<span class=\"z {cls}{(physical ? " wide" : "")}\"{style} title=\"{E(z)}: {tip}\">{E(label)}{(partial.Contains(z) ? "½" : "")}</span>");
        }
        sb.Append("</span>");
        var lbl = status switch { ZoneStatuses.AllZones => "all zones", ZoneStatuses.PartialZones => "partial", ZoneStatuses.NoZones => "no zones", _ => status };
        sb.Append($"<span class=\"zstat\">{lbl}</span>");
        return new HtmlString(sb.ToString());
    }

    public static IHtmlContent ZonePills(ExplorerRow r, bool physical) => ZonePills(r.ZoneStatus, physical,
        r.OfferedZonesLogical, r.OpenZonesLogical, r.PartialZonesLogical, r.RestrictedZonesLogical,
        r.OfferedZonesPhysical, r.OpenZonesPhysical, r.PartialZonesPhysical, r.RestrictedZonesPhysical);

    public static IHtmlContent ZonePills(FamilyZoneAccess? z, bool physical) => z is null ? ZonePills((string?)null, physical, "", "", "", "", "", "", "", "")
        : ZonePills(z.ZoneStatus, physical, z.OfferedZonesLogical, z.OpenZonesLogical, z.PartialZonesLogical, z.RestrictedZonesLogical,
            z.OfferedZonesPhysical, z.OpenZonesPhysical, z.PartialZonesPhysical, z.RestrictedZonesPhysical);

    public static string[] Split(string s) => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string Ago(DateTime? utc)
    {
        if (utc is null) return "never";
        var d = DateTime.UtcNow - utc.Value;
        return d.TotalMinutes < 1 ? "just now" : d.TotalHours < 1 ? $"{(int)d.TotalMinutes} min ago" : d.TotalDays < 1 ? $"{(int)d.TotalHours} h ago" : $"{(int)d.TotalDays} d ago";
    }

    public static IHtmlContent StatusBadge(string status) => new HtmlString(status switch
    {
        "Succeeded" => "<span class=\"badge ok\">succeeded</span>",
        "Warning" => "<span class=\"badge warnb\">warning</span>",
        "Failed" => "<span class=\"badge crit\">failed</span>",
        _ => $"<span class=\"badge neutral\">{E(status.ToLowerInvariant())}</span>",
    });
}

/// <summary>Server-rendered SVG charts (no client libraries).</summary>
public static class Svg
{
    public static IHtmlContent Donut(IReadOnlyList<(string Label, long Value, string Color)> data, int size = 170)
    {
        var total = Math.Max(1, data.Sum(d => d.Value));
        double a0 = -Math.PI / 2, r = size / 2.0 - 8, cx = size / 2.0, cy = size / 2.0;
        var sb = new StringBuilder($"<svg width=\"{size}\" height=\"{size}\" viewBox=\"0 0 {size} {size}\" role=\"img\">");
        if (data.Sum(d => d.Value) == 0) sb.Append($"<circle cx=\"{cx}\" cy=\"{cy}\" r=\"{r}\" stroke=\"#eef1f6\" stroke-width=\"22\" fill=\"none\"/>");
        foreach (var d in data.Where(d => d.Value > 0))
        {
            var a1 = a0 + 2 * Math.PI * d.Value / total;
            if (a1 - a0 >= 2 * Math.PI - 1e-6) a1 = a0 + 2 * Math.PI - 1e-4;
            var large = a1 - a0 > Math.PI ? 1 : 0;
            sb.Append(FormattableString.Invariant($"<path d=\"M{cx + r * Math.Cos(a0):0.##} {cy + r * Math.Sin(a0):0.##} A{r} {r} 0 {large} 1 {cx + r * Math.Cos(a1):0.##} {cy + r * Math.Sin(a1):0.##}\" stroke=\"{d.Color}\" stroke-width=\"22\" fill=\"none\"><title>{U.E(d.Label)}: {U.N(d.Value)}</title></path>"));
            a0 = a1;
        }
        sb.Append(FormattableString.Invariant($"<text x=\"{cx}\" y=\"{cy - 2}\" text-anchor=\"middle\" font-size=\"19\" font-weight=\"800\" fill=\"#141a22\">{U.N(data.Sum(d => d.Value))}</text>"));
        sb.Append(FormattableString.Invariant($"<text x=\"{cx}\" y=\"{cy + 15}\" text-anchor=\"middle\" font-size=\"11\" fill=\"#66707d\">vCPUs in use</text></svg>"));
        return new HtmlString(sb.ToString());
    }

    /// <summary>Step chart of a temporal history: each row is a value valid over [From, To).</summary>
    public static IHtmlContent StepChart(IReadOnlyList<TrendPoint> points, DateTime fromUtc, DateTime toUtc)
    {
        const int W = 760, H = 300, P = 44;
        if (points.Count == 0) return new HtmlString("<div class=\"note\">No history in this range.</div>");
        var max = Math.Max(1, points.Max(p => Math.Max(p.Limit, p.Usage)) * 1.1);
        double X(DateTime t) => P + (W - P - 24) * Math.Clamp((t - fromUtc).TotalSeconds / Math.Max(1, (toUtc - fromUtc).TotalSeconds), 0, 1);
        double Y(double v) => H - P - v * (H - 2 * P) / max;

        string Path(Func<TrendPoint, long> sel)
        {
            var sb = new StringBuilder();
            foreach (var p in points.Where(p => p.To > fromUtc))
            {
                var x0 = X(p.From < fromUtc ? fromUtc : p.From);
                var x1 = X(p.To > toUtc ? toUtc : p.To);
                var y = Y(sel(p));
                sb.Append(FormattableString.Invariant($"{(sb.Length == 0 ? "M" : "L")}{x0:0.#} {y:0.#} L{x1:0.#} {y:0.#} "));
            }
            return sb.ToString();
        }

        var svg = new StringBuilder($"<svg viewBox=\"0 0 {W} {H}\" width=\"100%\" role=\"img\">");
        foreach (var t in new[] { 0, .25, .5, .75, 1 })
        {
            var v = max * t;
            svg.Append(FormattableString.Invariant($"<line x1=\"{P}\" x2=\"{W - 24}\" y1=\"{Y(v):0.#}\" y2=\"{Y(v):0.#}\" stroke=\"#eef1f6\"/><text x=\"{P - 6}\" y=\"{Y(v) + 4:0.#}\" text-anchor=\"end\" font-size=\"10\" fill=\"#66707d\">{U.N((long)v)}</text>"));
        }
        foreach (var t in new[] { fromUtc, fromUtc + (toUtc - fromUtc) / 2, toUtc })
            svg.Append(FormattableString.Invariant($"<text x=\"{X(t):0.#}\" y=\"{H - P + 16}\" text-anchor=\"middle\" font-size=\"10\" fill=\"#66707d\">{t:MM-dd}</text>"));
        svg.Append($"<path d=\"{Path(p => p.Usage)}\" stroke=\"#2563eb\" stroke-width=\"2.2\" fill=\"none\"/>");
        svg.Append($"<path d=\"{Path(p => p.Limit)}\" stroke=\"#d93025\" stroke-width=\"2\" fill=\"none\" stroke-dasharray=\"6 4\"/>");
        svg.Append("</svg>");
        return new HtmlString(svg.ToString());
    }
}
