using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Aqr.Core.Access;
using Azure.Core;
using Microsoft.Extensions.Caching.Memory;

namespace Aqr.Web.Auth;

/// <summary>Builds the <see cref="UserContext"/> and resolved <see cref="Visibility"/> for the current request.</summary>
public sealed class CurrentUser(IHttpContextAccessor http, VisibilityResolver resolver, GraphGroupResolver graph)
{
    private UserContext? _user;
    private Visibility? _visibility;

    public async Task<UserContext> GetAsync(CancellationToken ct)
    {
        if (_user is not null) return _user;
        var p = http.HttpContext?.User ?? new ClaimsPrincipal();
        var oid = (p.FindFirst(ClaimTypesAqr.ObjectId)?.Value ?? p.FindFirst("oid")?.Value ?? "").ToLowerInvariant();
        var name = p.FindFirst("name")?.Value ?? p.Identity?.Name ?? "Signed-in user";
        var groups = p.FindAll("groups").Select(c => c.Value.ToLowerInvariant()).ToList();

        // Group overage: the token couldn't list every group and points to Graph instead.
        var overage = p.FindAll("_claim_names").Any(c => c.Value.Contains("groups", StringComparison.OrdinalIgnoreCase));
        var incomplete = false;
        if (overage && oid.Length > 0)
        {
            var resolved = await graph.TryGetGroupsAsync(oid, ct);
            if (resolved is null) incomplete = true; else groups = resolved.ToList();
        }

        _user = new UserContext(oid, name, p.IsInRole(Roles.Admin), p.IsInRole(Roles.Reader) || p.IsInRole(Roles.Admin), groups, incomplete);
        return _user;
    }

    public async Task<Visibility> VisibilityAsync(CancellationToken ct) =>
        _visibility ??= await resolver.ResolveAsync(await GetAsync(ct), ct);
}

/// <summary>
/// Resolves transitive security-group membership through Microsoft Graph with the app's managed identity, for
/// users whose token hit the group-overage limit. Needs the GroupMember.Read.All application permission
/// (infra: AQR_GRANT_GRAPH_GROUPMEMBER_READ). Returns null when that isn't available, so callers can say so.
/// </summary>
public sealed class GraphGroupResolver(IHttpClientFactory httpFactory, TokenCredential credential, IMemoryCache cache, ILogger<GraphGroupResolver> logger)
{
    private static readonly string[] Scopes = ["https://graph.microsoft.com/.default"];

    public async Task<IReadOnlyList<string>?> TryGetGroupsAsync(string objectId, CancellationToken ct)
    {
        if (cache.TryGetValue("groups:" + objectId, out IReadOnlyList<string>? hit)) return hit;
        try
        {
            var token = await credential.GetTokenAsync(new TokenRequestContext(Scopes), ct);
            var client = httpFactory.CreateClient("graph");
            var ids = new List<string>();
            string? url = $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(objectId)}/transitiveMemberOf/microsoft.graph.group?$select=id&$top=999";
            while (url is not null)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                using var resp = await client.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    logger.LogWarning("Graph group lookup failed ({Status}); user's groups are incomplete.", (int)resp.StatusCode);
                    return null;
                }
                var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct))!;
                ids.AddRange((json["value"] as JsonArray ?? []).Select(v => v?["id"]?.GetValue<string>()?.ToLowerInvariant() ?? "").Where(x => x.Length > 0));
                url = json["@odata.nextLink"]?.GetValue<string>();
            }
            cache.Set("groups:" + objectId, (IReadOnlyList<string>)ids, TimeSpan.FromMinutes(10));
            return ids;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Graph group lookup failed; user's groups are incomplete.");
            return null;
        }
    }
}
