using Aqr.Core.Data;
using Aqr.Core.Model;
using Aqr.Core.Reporting;
using Microsoft.Extensions.Primitives;

namespace Aqr.Tests;

public sealed class FilterAndRetentionTests
{
    private static ExplorerFilter Parse(string query)
    {
        var q = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(query);
        return ExplorerFilter.Parse(k => q.TryGetValue(k, out var v) ? v : StringValues.Empty);
    }

    [Fact]
    public void Parses_lists_from_repeated_and_comma_separated_values()
    {
        var f = Parse("?reg=EastUS2&reg=westus3,centralus&cpu=AMD&zopen=1&zopen=az2&minUtil=80&kind=RegionalTotal");
        Assert.Equal(["eastus2", "westus3", "centralus"], f.Regions);
        Assert.Equal(["AMD"], f.CpuManufacturers);
        Assert.Equal(["1", "az2"], f.OpenInZones);
        Assert.Equal(80, f.MinUtil);
        Assert.Equal(QuotaKinds.RegionalTotal, f.Kind);
    }

    [Fact]
    public void Rejects_unknown_enum_values_and_clamps_numbers()
    {
        var f = Parse("?zstat=Bogus&zstat=NoZones&zopen=9&minUtil=400&pageSize=999999&sort=drop table");
        Assert.Equal([ZoneStatuses.NoZones], f.ZoneStatuses);
        Assert.Empty(f.OpenInZones);
        Assert.Equal(100, f.MinUtil);
        Assert.Equal(5000, f.PageSize);
        Assert.Equal("util", f.Sort);
    }

    [Fact]
    public void Round_trips_through_its_query_string()
    {
        var f = Parse("?reg=eastus2&cat=GpuAccelerated&gpu=H100&zoneMode=physical&sort=available&dir=asc&asOf=2026-09-01T00:00:00Z");
        var again = Parse("?" + f.ToQueryString());
        Assert.Equal(f.Regions, again.Regions);
        Assert.Equal(f.GpuModels, again.GpuModels);
        Assert.Equal(f.AsOf, again.AsOf);
        Assert.True(again.IsPhysical);
        Assert.False(again.Descending);
        Assert.Equal("available", again.Sort);
    }

    [Theory]
    [InlineData("1 YEAR", "1 YEAR")]
    [InlineData("18 months", "18 MONTHS")]
    [InlineData("INFINITE", "INFINITE")]
    public void Retention_accepts_sql_periods(string input, string expected) => Assert.Equal(expected, TemporalRetention.Normalize(input));

    [Theory]
    [InlineData("1 YEAR); DROP TABLE x;--")]
    [InlineData("forever")]
    [InlineData("")]
    public void Retention_rejects_anything_else(string input) => Assert.Throws<ArgumentException>(() => TemporalRetention.Normalize(input));
}
