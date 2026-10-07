using Aqr.Core.Reporting;
using Microsoft.AspNetCore.Mvc;

namespace Aqr.Web.Pages;

public sealed class TrendsModel(ReportService reports) : AqrPageModel
{
    [BindProperty(SupportsGet = true)] public string? Sub { get; set; }
    [BindProperty(SupportsGet = true)] public string? Region { get; set; }
    [BindProperty(SupportsGet = true)] public string? Quota { get; set; }
    [BindProperty(SupportsGet = true)] public int Days { get; set; } = 30;

    public IReadOnlyList<ExplorerRow> Candidates { get; private set; } = [];
    public TrendResult? Trend { get; private set; }
    public DateTime From { get; private set; }
    public DateTime To { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Days = Days is 7 or 30 or 90 or 365 ? Days : 30;
        Candidates = await reports.TrendCandidatesAsync(Visibility, ct);
        if (string.IsNullOrEmpty(Sub) && Candidates.Count > 0)
        {
            Sub = Candidates[0].SubscriptionId; Region = Candidates[0].Region; Quota = Candidates[0].QuotaName;
        }
        To = DateTime.UtcNow;
        From = To.AddDays(-Days);
        if (!string.IsNullOrEmpty(Sub) && !string.IsNullOrEmpty(Region) && !string.IsNullOrEmpty(Quota))
            Trend = await reports.TrendAsync(Visibility, Sub, Region, Quota, From, ct);
    }

    public string Key(string sub, string region, string quota) => $"{sub}|{region}|{quota}";
}
