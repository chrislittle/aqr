using Aqr.Core.Reporting;

namespace Aqr.Web.Pages;

public sealed class IndexModel(ReportService reports) : AqrPageModel
{
    public OverviewResult Data { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken ct) => Data = await reports.OverviewAsync(Visibility, AsOf, ct);
}
