using Aqr.Core.Reporting;
using Microsoft.AspNetCore.Mvc;

namespace Aqr.Web.Pages;

public sealed class ZonesModel(ReportService reports) : AqrPageModel
{
    [BindProperty(SupportsGet = true)] public string? Region { get; set; }
    [BindProperty(SupportsGet = true)] public string? Family { get; set; }
    [BindProperty(SupportsGet = true)] public string? ZoneMode { get; set; }
    public bool Physical => string.Equals(ZoneMode, "physical", StringComparison.OrdinalIgnoreCase);
    public ZonesResult Data { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken ct) => Data = await reports.ZonesAsync(Visibility, Region, Family, AsOf, ct);
}
