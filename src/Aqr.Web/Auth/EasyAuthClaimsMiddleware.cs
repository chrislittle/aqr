using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aqr.Web.Auth;

/// <summary>
/// App Service Easy Auth signs the user in at the platform and forwards the identity in X-MS-CLIENT-PRINCIPAL.
/// This hydrates HttpContext.User from it. Fails closed when Easy Auth isn't in front (outside Development), since
/// the header would then be untrusted client input. Adapted from ghcp-credit-visibility-azure.
/// </summary>
public sealed class EasyAuthClaimsMiddleware(RequestDelegate next, ILogger<EasyAuthClaimsMiddleware> logger, IConfiguration config, IWebHostEnvironment env)
{
    public const string RoleClaimType = "roles";
    private static readonly string[] AlwaysAllowed = ["/health/live", "/health/ready"];
    private readonly bool _easyAuth = config.GetValue("Auth:EasyAuthEnabled", true);
    private readonly bool _devUser = env.IsDevelopment() && config.GetValue("Auth:DevUser:Enabled", false);
    private readonly string _devRole = config.GetValue("Auth:DevUser:Role", Roles.Admin)!;

    public async Task Invoke(HttpContext ctx)
    {
        if (!_easyAuth && !env.IsDevelopment())
        {
            if (!AlwaysAllowed.Contains(ctx.Request.Path.Value, StringComparer.OrdinalIgnoreCase))
            {
                ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await ctx.Response.WriteAsync("Authentication is not configured for this deployment; refusing to serve requests.");
                return;
            }
            await next(ctx);
            return;
        }

        var header = ctx.Request.Headers["X-MS-CLIENT-PRINCIPAL"].FirstOrDefault();
        if (!string.IsNullOrEmpty(header) && _easyAuth)
        {
            try
            {
                var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(header));
                var p = JsonSerializer.Deserialize<ClientPrincipal>(json);
                if (p?.Claims is { Count: > 0 })
                {
                    // The claims array always tags roles with the short "roles" type, whatever role_typ says.
                    var id = new ClaimsIdentity(p.AuthenticationType ?? "aad", p.NameClaimType ?? ClaimTypes.Name, RoleClaimType);
                    id.AddClaims(p.Claims.Select(c => new Claim(c.Type, c.Value)));
                    ctx.User = new ClaimsPrincipal(id);
                }
                else
                {
                    logger.LogWarning("X-MS-CLIENT-PRINCIPAL present but had no claims; treating request as anonymous.");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to parse X-MS-CLIENT-PRINCIPAL; treating request as anonymous.");
            }
        }
        else if (_devUser && ctx.User.Identity?.IsAuthenticated != true)
        {
            var id = new ClaimsIdentity("dev", "name", RoleClaimType);
            id.AddClaim(new Claim("name", "Local developer"));
            id.AddClaim(new Claim(ClaimTypesAqr.ObjectId, "00000000-0000-0000-0000-00000000dev1"));
            id.AddClaim(new Claim(RoleClaimType, _devRole));
            foreach (var g in config.GetSection("Auth:DevUser:Groups").Get<string[]>() ?? []) id.AddClaim(new Claim("groups", g));
            ctx.User = new ClaimsPrincipal(id);
        }

        await next(ctx);
    }

    private sealed class ClientPrincipal
    {
        [JsonPropertyName("auth_typ")] public string? AuthenticationType { get; set; }
        [JsonPropertyName("name_typ")] public string? NameClaimType { get; set; }
        [JsonPropertyName("claims")] public List<ClientClaim> Claims { get; set; } = [];
    }

    private sealed class ClientClaim
    {
        [JsonPropertyName("typ")] public string Type { get; set; } = "";
        [JsonPropertyName("val")] public string Value { get; set; } = "";
    }
}

public static class Roles
{
    public const string Reader = "AQR.Reader";
    public const string Admin = "AQR.Admin";
}

public static class ClaimTypesAqr
{
    public const string ObjectId = "http://schemas.microsoft.com/identity/claims/objectidentifier";
}
