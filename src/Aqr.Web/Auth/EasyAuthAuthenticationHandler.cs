using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Aqr.Web.Auth;

/// <summary>
/// Registered so the authorization fallback policy has a scheme to challenge. Identity hydration happens in
/// <see cref="EasyAuthClaimsMiddleware"/>; this just defers to it. API calls get 401/403, browsers are redirected
/// to the Easy Auth login endpoint.
/// </summary>
public sealed class EasyAuthAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "EasyAuth";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
        Task.FromResult(Context.User.Identity?.IsAuthenticated == true
            ? AuthenticateResult.Success(new AuthenticationTicket(Context.User, SchemeName))
            : AuthenticateResult.NoResult());

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (Request.Path.StartsWithSegments("/api"))
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }
        var target = string.IsNullOrEmpty(properties.RedirectUri) ? Request.Path + Request.QueryString : properties.RedirectUri;
        Response.Redirect("/.auth/login/aad?post_login_redirect_uri=" + Uri.EscapeDataString(target));
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        if (Request.Path.StartsWithSegments("/api"))
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
        Response.Redirect("/NoAccess");
        return Task.CompletedTask;
    }
}
