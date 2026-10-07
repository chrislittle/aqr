using System.Text.Json;
using Aqr.Core.Catalog;

namespace Aqr.Tests;

public sealed class FamilyCatalogTests
{
    private readonly FamilyCatalog _catalog = TestHost.Catalog();

    [Theory]
    [InlineData("standardDSv5Family", "dsv5")]
    [InlineData("standardNCASv3_T4Family", "ncasv3t4")]
    [InlineData("standardMSMediumMemoryv3Family", "msmediummemoryv3")]
    [InlineData("DSv5", "dsv5")]
    public void Normalize_strips_prefix_suffix_and_punctuation(string input, string expected) =>
        Assert.Equal(expected, FamilyCatalog.Normalize(input));

    [Fact]
    public void Catalog_entry_wins_with_its_learn_source()
    {
        var f = _catalog.Classify("standardDSv5Family");
        Assert.Equal("Catalog", f.Classification);
        Assert.Equal("Intel", f.CpuManufacturer);
        Assert.Equal("GeneralPurpose", f.Category);
        Assert.Equal("v5", f.Generation);
        Assert.Equal("standarddsv5family", f.FamilyId);
        Assert.StartsWith("https://learn.microsoft.com/", f.SourceUrl);
    }

    [Fact]
    public void Cobalt_and_ampere_are_arm64()
    {
        Assert.Equal(("Microsoft", "Arm64"), Pair(_catalog.Classify("standardDPSv6Family")));
        Assert.Equal(("Ampere", "Arm64"), Pair(_catalog.Classify("standardDPSv5Family")));
        static (string, string) Pair(Aqr.Core.Model.VmFamily f) => (f.CpuManufacturer, f.Architecture);
    }

    [Fact]
    public void Gpu_family_carries_accelerator_and_host_cpu()
    {
        var f = _catalog.Classify("standardNCASv3_T4Family");
        Assert.Equal(("GPU", "Nvidia", "T4", "AMD"), (f.AcceleratorType, f.AcceleratorVendor, f.AcceleratorModel, f.CpuManufacturer));
    }

    [Theory]
    [InlineData("standardDSv3Family")]
    [InlineData("standardESv3Family")]
    [InlineData("standardFSv2Family")]
    [InlineData("standardBSFamily")]
    [InlineData("standardLSv2Family")]
    public void Learn_capacity_restricted_series_are_flagged(string family) =>
        Assert.Equal("Capacity-restricted", _catalog.Classify(family).Lifecycle);

    [Theory]
    [InlineData("standardDSv4Family")]
    [InlineData("standardESv4Family")]
    public void V4_is_not_capacity_restricted(string family) =>
        Assert.Equal("Current", _catalog.Classify(family).Lifecycle);

    [Fact]
    public void Unknown_amd_family_is_derived_from_the_naming_convention()
    {
        var f = _catalog.Classify("standardDASv9Family");
        Assert.Equal("Derived", f.Classification);
        Assert.Equal("AMD", f.CpuManufacturer);
        Assert.Equal("v9", f.Generation);
        Assert.Equal("GeneralPurpose", f.Category);
        Assert.Equal(_catalog.NamingSource, f.SourceUrl);
    }

    [Fact]
    public void Unknown_arm_family_is_arm64_but_manufacturer_is_not_guessed()
    {
        var f = _catalog.Classify("standardEPSv9Family");
        Assert.Equal(("Arm64", "Unclassified"), (f.Architecture, f.CpuManufacturer));
    }

    [Fact]
    public void Unknown_family_without_a_vendor_letter_stays_unclassified()
    {
        var f = _catalog.Classify("standardDSv9Family");
        Assert.Equal("Unclassified", f.CpuManufacturer);
        Assert.Equal("Not in catalog", f.Lifecycle);
    }

    [Fact]
    public void Catalog_file_is_internally_consistent()
    {
        var doc = JsonSerializer.Deserialize<CatalogDocument>(File.ReadAllText(TestHost.CatalogPath), FamilyCatalog.JsonOptions)!;
        string[] cats = ["GeneralPurpose", "ComputeOptimized", "MemoryOptimized", "StorageOptimized", "GpuAccelerated", "FpgaAccelerated", "HighPerformanceCompute"];
        string[] cpus = ["Intel", "AMD", "Microsoft", "Ampere"];
        var tokens = doc.Families.SelectMany(f => f.Match.Select(FamilyCatalog.Normalize)).ToList();
        Assert.Equal(tokens.Count, tokens.Distinct().Count());
        Assert.All(doc.Families, f =>
        {
            Assert.StartsWith("https://learn.microsoft.com/azure/virtual-machines/", f.Source);
            Assert.Contains(f.Category, cats);
            Assert.Contains(f.CpuManufacturer, cpus);
            Assert.Contains(f.Lifecycle, new[] { "Current", "Capacity-restricted" });
        });
    }
}
