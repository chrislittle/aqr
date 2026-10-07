using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Aqr.Core.Model;

namespace Aqr.Core.Catalog;

/// <summary>
/// Classifies VM families: catalog/vm-families.json first (cited Learn facts), then the documented VM naming
/// convention for anything not listed (https://learn.microsoft.com/azure/virtual-machines/vm-naming-conventions).
/// Nothing is guessed beyond those two sources; unknowns stay "Unclassified".
/// </summary>
public sealed partial class FamilyCatalog
{
    private readonly Dictionary<string, CatalogEntry> _byToken;

    public string LifecycleSource { get; }
    public string NamingSource { get; }
    public IReadOnlyList<CatalogEntry> Entries { get; }

    public FamilyCatalog(CatalogDocument doc)
    {
        Entries = doc.Families;
        LifecycleSource = doc.LifecycleSource;
        NamingSource = doc.NamingSource;
        _byToken = new(StringComparer.OrdinalIgnoreCase);
        foreach (var e in doc.Families)
            foreach (var m in e.Match)
                _byToken[Normalize(m)] = e;
    }

    public static FamilyCatalog Load(string path)
    {
        using var s = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<CatalogDocument>(s, JsonOptions) ?? throw new InvalidDataException($"Empty catalog: {path}");
        return new FamilyCatalog(doc);
    }

    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [GeneratedRegex("[^a-z0-9]")] private static partial Regex NonAlnum();
    [GeneratedRegex(@"v(\d+)$")] private static partial Regex GenerationAtEnd();
    [GeneratedRegex(@"^([a-z]+?)(?:v\d+)?$")] private static partial Regex LettersThenVersion();

    /// <summary>standardDSv5Family → dsv5; standardNCASv3_T4Family → ncasv3t4.</summary>
    public static string Normalize(string familyOrToken)
    {
        var s = (familyOrToken ?? "").ToLowerInvariant();
        if (s.StartsWith("standard")) s = s["standard".Length..];
        if (s.EndsWith("family")) s = s[..^"family".Length];
        return NonAlnum().Replace(s, "");
    }

    /// <summary>Builds the dimension row for a family from the catalog or the naming convention.</summary>
    public VmFamily Classify(string quotaName, string localizedName = "")
    {
        var token = Normalize(quotaName);
        var f = new VmFamily
        {
            FamilyId = Keys.Family(quotaName),
            QuotaName = quotaName,
            LocalizedName = localizedName,
            Generation = Generation(token),
        };

        if (_byToken.TryGetValue(token, out var e))
        {
            f.Series = e.Series;
            f.Category = e.Category;
            f.CpuManufacturer = e.CpuManufacturer;
            f.AcceleratorType = e.Accelerator?.Type ?? "None";
            f.AcceleratorVendor = e.Accelerator?.Vendor ?? "";
            f.AcceleratorModel = e.Accelerator?.Model ?? "";
            f.Features = string.Join(',', e.Features ?? []);
            f.Lifecycle = e.Lifecycle;
            f.Classification = "Catalog";
            f.SourceUrl = e.Source;
            if (f.CpuManufacturer is "Ampere" or "Microsoft") f.Architecture = "Arm64";
            return f;
        }

        // Naming convention: [Family][Sub-family][#vCPU][Additive features][Accelerator][Version].
        // Quota names carry no vCPU count, so the letters before the version are family + additive features.
        f.Series = SeriesFromToken(token);
        f.Category = CategoryFromLetter(token);
        f.Classification = "Derived";
        f.Lifecycle = "Not in catalog";
        f.SourceUrl = NamingSource;
        var letters = LettersThenVersion().Match(token) is { Success: true } m ? m.Groups[1].Value : token;
        var additive = letters.Length > 1 ? letters[1..] : "";
        if (additive.Contains('p')) { f.CpuManufacturer = "Unclassified"; f.Architecture = "Arm64"; }
        else if (additive.Contains('a')) f.CpuManufacturer = "AMD";
        if (token.StartsWith('n')) f.AcceleratorType = "GPU";
        if (token.StartsWith('b')) f.Features = "burstable";
        return f;
    }

    private static string Generation(string token) =>
        GenerationAtEnd().Match(token) is { Success: true } m ? "v" + m.Groups[1].Value
        : Regex.Match(token, @"v(\d+)") is { Success: true } m2 ? "v" + m2.Groups[1].Value
        : "v1";

    private static string SeriesFromToken(string token) =>
        token.Length == 0 ? "?" : char.ToUpperInvariant(token[0]) + token[1..];

    /// <summary>Family letter → category, per the Learn "VM size families by type" list.</summary>
    private static string CategoryFromLetter(string token) => token.Length == 0 ? "Unclassified" : token[0] switch
    {
        'a' or 'b' or 'd' => "GeneralPurpose",
        'f' => "ComputeOptimized",
        'e' or 'm' or 'g' => "MemoryOptimized",
        'l' => "StorageOptimized",
        'n' => "GpuAccelerated",
        'h' => "HighPerformanceCompute",
        _ => "Unclassified",
    };
}

public sealed class CatalogDocument
{
    public string LifecycleSource { get; set; } = "";
    public string NamingSource { get; set; } = "";
    public List<CatalogEntry> Families { get; set; } = [];
}

public sealed class CatalogEntry
{
    public string[] Match { get; set; } = [];
    public string Series { get; set; } = "";
    public string Category { get; set; } = "";
    public string CpuManufacturer { get; set; } = "";
    public CatalogAccelerator? Accelerator { get; set; }
    public string[]? Features { get; set; }
    public string Lifecycle { get; set; } = "Current";
    public string Source { get; set; } = "";
}

public sealed class CatalogAccelerator
{
    public string Type { get; set; } = "";
    public string Vendor { get; set; } = "";
    public string Model { get; set; } = "";
}
