using Aqr.Core;
using Aqr.Core.Model;
using Aqr.Core.Reporting;
using Aqr.Core.Sync;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Aqr.Web.Pages.Admin;

public sealed class IndexModel(ReportService reports, SyncTrigger trigger, IOptions<AqrOptions> options, DatabaseState dbState) : AqrPageModel
{
    public IReadOnlyList<SyncRun> Runs { get; private set; } = [];
    public IReadOnlyList<SyncCoverage> Gaps { get; private set; } = [];
    public AqrOptions Options => options.Value;
    public DatabaseState Db => dbState;
    [TempData] public string? Message { get; set; }

    public async Task OnGetAsync(CancellationToken ct) => (Runs, Gaps) = await reports.SyncHealthAsync(ct);

    public IActionResult OnPostSync()
    {
        Message = trigger.TryQueue(User0.DisplayName) ? "Sync queued. Refresh in a minute to see the run." : "A sync is already queued.";
        return RedirectToPage();
    }
}
