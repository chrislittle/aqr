using Aqr.Core.Catalog;
using Aqr.Core.Reporting;

namespace Aqr.Web.Pages;

public sealed class FamiliesModel(ReportService reports, FamilyCatalog catalog) : AqrPageModel
{
    public IReadOnlyList<FamilyRow> Rows { get; private set; } = [];
    public FamilyCatalog Catalog => catalog;

    public async Task OnGetAsync(CancellationToken ct) => Rows = await reports.FamiliesAsync(Visibility, ct);
}
