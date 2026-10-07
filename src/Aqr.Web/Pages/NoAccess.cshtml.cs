using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Aqr.Web.Pages;

public sealed class NoAccessModel : PageModel
{
    public void OnGet() { }
}

public sealed class ErrorModel : PageModel
{
    public string? RequestId { get; private set; }
    public void OnGet() => RequestId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier;
}
