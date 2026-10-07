using Aqr.Core.Reporting;
using Microsoft.AspNetCore.Mvc;

namespace Aqr.Web.Pages;

public sealed class GroupsModel(ReportService reports) : AqrPageModel
{
    [BindProperty(SupportsGet = true)] public string? Group { get; set; }
    [BindProperty(SupportsGet = true)] public string? Region { get; set; }
    public GroupsResult Data { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken ct) => Data = await reports.GroupsAsync(Visibility, Group, Region ?? "*", AsOf, ct);
}
