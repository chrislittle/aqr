using Aqr.Core;
using Aqr.Core.Access;
using Aqr.Core.Data;
using Aqr.Web.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aqr.Web.Pages;

/// <summary>Loads the caller and their report visibility before every page handler, and exposes shared view state.</summary>
public abstract class AqrPageModel : PageModel
{
    public UserContext User0 { get; private set; } = null!;
    public Visibility Visibility { get; private set; } = Visibility.Everything;
    public bool HistoryAvailable { get; private set; }
    public bool UseMock { get; private set; }

    /// <summary>Optional point in time (UTC) for "as of" reporting via the temporal tables.</summary>
    [BindProperty(SupportsGet = true)] public DateTime? AsOf { get; set; }

    public override async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var sp = HttpContext.RequestServices;
        var cu = sp.GetRequiredService<CurrentUser>();
        User0 = await cu.GetAsync(HttpContext.RequestAborted);
        Visibility = await cu.VisibilityAsync(HttpContext.RequestAborted);
        UseMock = sp.GetRequiredService<IOptions<AqrOptions>>().Value.UseMock;
        await using (var db = await sp.GetRequiredService<IDbContextFactory<AqrDbContext>>().CreateDbContextAsync(HttpContext.RequestAborted))
            HistoryAvailable = db.Database.IsSqlServer();
        if (AsOf is { } a) AsOf = DateTime.SpecifyKind(a, DateTimeKind.Utc);
        if (!HistoryAvailable) AsOf = null;

        ViewData["User"] = User0;
        ViewData["Visibility"] = Visibility;
        ViewData["HistoryAvailable"] = HistoryAvailable;
        ViewData["UseMock"] = UseMock;
        ViewData["AsOf"] = AsOf;
        await next();
    }
}
