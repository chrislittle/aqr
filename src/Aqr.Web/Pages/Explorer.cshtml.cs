using Aqr.Core.Reporting;

namespace Aqr.Web.Pages;

public sealed class ExplorerModel(ReportService reports) : AqrPageModel
{
    public ExplorerFilter Filter { get; private set; } = new();
    public ExplorerResult Result { get; private set; } = null!;
    public ReportService.FilterOptions Options { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken ct)
    {
        Filter = ExplorerFilter.Parse(k => Request.Query[k]);
        if (!HistoryAvailable && Filter.AsOf is not null) Filter = ExplorerFilter.Parse(k => k == "asOf" ? [] : Request.Query[k]);
        Result = await reports.ExplorerAsync(Visibility, Filter, ct);
        Options = await reports.FilterOptionsAsync(Visibility, ct);
    }

    public bool On(string key, string value) => Request.Query[key].SelectMany(v => (v ?? "").Split(',')).Contains(value, StringComparer.OrdinalIgnoreCase);
}
