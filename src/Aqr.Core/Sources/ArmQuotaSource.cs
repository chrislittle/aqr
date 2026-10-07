using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Core;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aqr.Core.Sources;

/// <summary>
/// Real data source: Azure Resource Manager APIs only (design doc §3). Every call is read-only and uses the
/// app's managed identity. Responses are optionally archived (gzipped) to blob storage for audit and replay.
/// </summary>
public sealed class ArmQuotaSource(
    HttpClient http,
    IRawArchive archive) : IQuotaSource
{
    public const string HttpClientName = "arm";
    private const string ArgApi = "2022-10-01";
    private const string QuotaApi = "2025-09-01";
    private const string LocationsApi = "2022-12-01";
    private const string SkusApi = "2021-07-01";
    private const string UsagesApi = "2024-07-01";
    private const int ArgSubscriptionBatch = 200;

    public SourceStats Stats { get; } = new();

    public async Task<IReadOnlyList<SubscriptionInfo>> GetSubscriptionsAsync(IReadOnlyList<string> managementGroupIds, CancellationToken ct)
    {
        const string query = """
            resourcecontainers
            | where type =~ 'microsoft.resources/subscriptions'
            | project subscriptionId, name, state = tostring(properties.state), chain = properties.managementGroupAncestorsChain
            """;
        var rows = await QueryResourceGraphAsync(query, managementGroupIds, null, "inventory", ct);
        return rows.Select(r =>
        {
            // managementGroupAncestorsChain is ordered from the immediate parent up to the root.
            var chain = r["chain"] is JsonArray a
                ? a.Select(x => x?["name"]?.GetValue<string>() ?? "").Where(x => x.Length > 0).Reverse().ToArray()
                : [];
            return new SubscriptionInfo(Str(r, "subscriptionId"), Str(r, "name"), Str(r, "state"), chain);
        }).ToList();
    }

    public async Task<IReadOnlyList<QuotaUsage>> GetQuotaUsagesAsync(IReadOnlyList<string> subscriptionIds, CancellationToken ct)
    {
        // Compute is the only provider with near-real-time quota data in QuotaResources (Learn: quotas monitoring).
        const string query = """
            QuotaResources
            | where type =~ 'microsoft.compute/locations/usages'
            | where isnotempty(properties)
            | mv-expand q = properties.value limit 2000
            | extend qname = tostring(q.name.value)
            | where qname =~ 'cores' or qname endswith 'Family'
            | project subscriptionId, location, qname, localized = tostring(q.name.localizedValue), usage = tolong(q.currentValue), qlimit = tolong(q['limit'])
            """;
        var result = new List<QuotaUsage>();
        foreach (var batch in subscriptionIds.Chunk(ArgSubscriptionBatch))
        {
            var rows = await QueryResourceGraphAsync(query, null, batch, "quota", ct);
            result.AddRange(rows.Select(r => new QuotaUsage(
                Str(r, "subscriptionId"), Str(r, "location"), Str(r, "qname"), Str(r, "localized"),
                Long(r, "usage"), Long(r, "qlimit"))));
        }
        return result;
    }

    public async Task<IReadOnlyList<QuotaUsage>> GetComputeUsagesAsync(string subscriptionId, string region, CancellationToken ct)
    {
        var url = $"/subscriptions/{subscriptionId}/providers/Microsoft.Compute/locations/{region}/usages?api-version={UsagesApi}";
        var items = await GetPagedAsync(url, "value", "nextLink", "usages", ct);
        return items
            .Select(i => new QuotaUsage(subscriptionId, region,
                i["name"]?["value"]?.GetValue<string>() ?? "", i["name"]?["localizedValue"]?.GetValue<string>() ?? "",
                Long(i, "currentValue"), Long(i, "limit")))
            .Where(u => u.Name.Equals("cores", StringComparison.OrdinalIgnoreCase) || u.Name.EndsWith("Family", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<IReadOnlyList<LocationInfo>> GetLocationsAsync(string subscriptionId, CancellationToken ct)
    {
        var items = await GetPagedAsync($"/subscriptions/{subscriptionId}/locations?api-version={LocationsApi}", "value", "nextLink", "locations", ct);
        return items
            .Where(i => string.Equals(i["metadata"]?["regionType"]?.GetValue<string>(), "Physical", StringComparison.OrdinalIgnoreCase))
            .Select(i =>
            {
                var map = (i["availabilityZoneMappings"] as JsonArray ?? [])
                    .Where(z => z is not null)
                    .ToDictionary(z => z!["logicalZone"]!.GetValue<string>(), z => z!["physicalZone"]!.GetValue<string>());
                return new LocationInfo(Str(i, "name"), Str(i, "displayName"), i["metadata"]?["geographyGroup"]?.GetValue<string>() ?? "", map);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<SkuInfo>> GetVmSkusAsync(string subscriptionId, string region, CancellationToken ct)
    {
        var filter = Uri.EscapeDataString($"location eq '{region}'");
        var url = $"/subscriptions/{subscriptionId}/providers/Microsoft.Compute/skus?api-version={SkusApi}&$filter={filter}";
        var items = await GetPagedAsync(url, "value", "nextLink", "skus", ct);
        return items
            .Where(i => string.Equals(Str(i, "resourceType"), "virtualMachines", StringComparison.OrdinalIgnoreCase))
            .Select(i => ParseSku(i, region))
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
    }

    internal static SkuInfo? ParseSku(JsonNode i, string region)
    {
        var loc = (i["locationInfo"] as JsonArray ?? [])
            .FirstOrDefault(l => string.Equals(l?["location"]?.GetValue<string>(), region, StringComparison.OrdinalIgnoreCase));
        var zones = (loc?["zones"] as JsonArray ?? []).Select(z => z!.GetValue<string>()).OrderBy(z => z).ToArray();

        var restrictions = (i["restrictions"] as JsonArray ?? []).Where(r => r is not null).Select(r =>
        {
            var type = Str(r!, "type");
            var rzones = (r!["restrictionInfo"]?["zones"] as JsonArray ?? []).Select(z => z!.GetValue<string>()).ToArray();
            var rlocs = (r["restrictionInfo"]?["locations"] as JsonArray ?? []).Select(z => z!.GetValue<string>()).ToArray();
            // A Location restriction for a different region doesn't apply to this one.
            if (type == "Location" && rlocs.Length > 0 && !rlocs.Contains(region, StringComparer.OrdinalIgnoreCase)) return null;
            return new SkuRestrictionInfo(type, rzones, Str(r, "reasonCode"));
        }).Where(r => r is not null).Select(r => r!).ToArray();

        var caps = (i["capabilities"] as JsonArray ?? [])
            .Where(c => c is not null)
            .GroupBy(c => Str(c!, "name"))
            .ToDictionary(g => g.Key, g => Str(g.First()!, "value"));

        var family = Str(i, "family");
        if (family.Length == 0) return null;
        return new SkuInfo(Str(i, "name"), family, region, zones, restrictions, caps);
    }

    public async Task<IReadOnlyList<QuotaGroupInfo>> GetQuotaGroupsAsync(string managementGroupId, CancellationToken ct)
    {
        var items = await GetPagedAsync($"{GroupRoot(managementGroupId)}?api-version={QuotaApi}", "value", "nextLink", "groups", ct);
        return items.Select(i => new QuotaGroupInfo(managementGroupId, Str(i, "name"),
            i["properties"]?["displayName"]?.GetValue<string>() ?? Str(i, "name"))).ToList();
    }

    public async Task<IReadOnlyList<string>> GetQuotaGroupSubscriptionsAsync(string managementGroupId, string groupName, CancellationToken ct)
    {
        var items = await GetPagedAsync($"{GroupRoot(managementGroupId)}/{groupName}/subscriptions?api-version={QuotaApi}", "value", "nextLink", "group-subs", ct);
        return items.Select(i => i["properties"]?["subscriptionId"]?.GetValue<string>() ?? Str(i, "name"))
                    .Where(s => s.Length > 0).ToList();
    }

    public async Task<IReadOnlyList<GroupQuotaLimitInfo>> GetGroupQuotaLimitsAsync(string managementGroupId, string groupName, string region, CancellationToken ct)
    {
        // This list nests its page inside properties: { properties: { value: [...], nextLink } }.
        var url = $"{GroupRoot(managementGroupId)}/{groupName}/resourceProviders/Microsoft.Compute/groupQuotaLimits/{region}?api-version={QuotaApi}";
        var items = await GetPagedAsync(url, "properties.value", "properties.nextLink", "group-limits", ct);
        return items.Select(i =>
        {
            var p = i["properties"] ?? i;
            var alloc = (p["allocatedToSubscriptions"]?["value"] as JsonArray ?? [])
                .Where(a => a is not null)
                .GroupBy(a => Str(a!, "subscriptionId"))
                .ToDictionary(g => g.Key, g => g.Sum(a => Long(a!, "quotaAllocated")), StringComparer.OrdinalIgnoreCase);
            var name = Str(p, "resourceName");
            if (name.Length == 0) name = p["name"]?["value"]?.GetValue<string>() ?? "";
            return new GroupQuotaLimitInfo(region, name, Long(p, "limit"), Long(p, "availableLimit"), alloc);
        }).Where(x => x.ResourceName.Length > 0).ToList();
    }

    public async Task<IReadOnlyList<GroupQuotaUsageInfo>> GetGroupQuotaUsagesAsync(string managementGroupId, string groupName, string region, CancellationToken ct)
    {
        var url = $"{GroupRoot(managementGroupId)}/{groupName}/resourceProviders/Microsoft.Compute/locationUsages/{region}?api-version={QuotaApi}";
        var items = await GetPagedAsync(url, "value", "nextLink", "group-usages", ct);
        return items.Select(i =>
        {
            var p = i["properties"] ?? i;
            var name = p["name"]?["value"]?.GetValue<string>() ?? Str(i, "name");
            return new GroupQuotaUsageInfo(region, name, Long(p, "limit"), Long(p, "usages"));
        }).Where(x => x.ResourceName.Length > 0).ToList();
    }

    private static string GroupRoot(string mg) =>
        $"/providers/Microsoft.Management/managementGroups/{Uri.EscapeDataString(mg)}/providers/Microsoft.Quota/groupQuotas";

    private async Task<List<JsonNode>> QueryResourceGraphAsync(string query, IReadOnlyList<string>? managementGroups,
        IReadOnlyList<string>? subscriptions, string tag, CancellationToken ct)
    {
        var rows = new List<JsonNode>();
        string? skipToken = null;
        do
        {
            var body = new JsonObject
            {
                ["query"] = query,
                ["options"] = new JsonObject { ["resultFormat"] = "objectArray", ["$top"] = 1000 },
            };
            if (skipToken is not null) body["options"]!["$skipToken"] = skipToken;
            if (managementGroups is { Count: > 0 }) body["managementGroups"] = new JsonArray(managementGroups.Select(x => (JsonNode)x).ToArray());
            if (subscriptions is { Count: > 0 }) body["subscriptions"] = new JsonArray(subscriptions.Select(x => (JsonNode)x).ToArray());

            using var resp = await http.PostAsJsonAsync($"/providers/Microsoft.ResourceGraph/resources?api-version={ArgApi}", body, ct);
            var json = await ReadAsync(resp, tag, ct);
            rows.AddRange((json["data"] as JsonArray ?? []).Where(x => x is not null).Select(x => x!));
            skipToken = json["$skipToken"]?.GetValue<string>();
        } while (!string.IsNullOrEmpty(skipToken));
        return rows;
    }

    private async Task<List<JsonNode>> GetPagedAsync(string url, string valuePath, string nextPath, string tag, CancellationToken ct)
    {
        var items = new List<JsonNode>();
        string? next = url;
        while (!string.IsNullOrEmpty(next))
        {
            using var resp = await http.GetAsync(next, ct);
            var json = await ReadAsync(resp, tag, ct);
            items.AddRange((Path(json, valuePath) as JsonArray ?? []).Where(x => x is not null).Select(x => x!));
            next = Path(json, nextPath)?.GetValue<string>();
        }
        return items;
    }

    private async Task<JsonNode> ReadAsync(HttpResponseMessage resp, string tag, CancellationToken ct)
    {
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (resp.StatusCode == HttpStatusCode.TooManyRequests) Stats.RecordThrottle();
        if (!resp.IsSuccessStatusCode)
        {
            var detail = text.Length > 600 ? text[..600] : text;
            throw new ArmRequestException(resp.StatusCode, $"{resp.RequestMessage?.Method} {resp.RequestMessage?.RequestUri?.AbsolutePath} → {(int)resp.StatusCode}: {detail}");
        }
        await archive.SaveAsync(tag, resp.RequestMessage?.RequestUri?.PathAndQuery ?? "", text, ct);
        return JsonNode.Parse(text) ?? new JsonObject();
    }

    private static JsonNode? Path(JsonNode node, string path)
    {
        JsonNode? cur = node;
        foreach (var part in path.Split('.')) cur = cur?[part];
        return cur;
    }

    private static string Str(JsonNode n, string key) =>
        n[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : n[key]?.ToString() ?? "";

    private static long Long(JsonNode n, string key)
    {
        var v = n[key];
        if (v is null) return 0;
        if (v is JsonValue jv)
        {
            if (jv.TryGetValue<long>(out var l)) return l;
            if (jv.TryGetValue<double>(out var d)) return (long)d;
            if (jv.TryGetValue<string>(out var s) && long.TryParse(s, out var p)) return p;
        }
        return 0;
    }
}

public sealed class ArmRequestException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

/// <summary>Adds an ARM bearer token from the app's credential (managed identity in Azure).</summary>
public sealed class ArmAuthHandler(TokenCredential credential) : DelegatingHandler
{
    private static readonly string[] Scopes = ["https://management.azure.com/.default"];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = await credential.GetTokenAsync(new TokenRequestContext(Scopes), ct);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return await base.SendAsync(request, ct);
    }
}

public interface IRawArchive
{
    Task SaveAsync(string tag, string requestPath, string body, CancellationToken ct);
}

public sealed class NoRawArchive : IRawArchive
{
    public Task SaveAsync(string tag, string requestPath, string body, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>Gzipped API responses under raw/{yyyy-MM-dd}/{tag}/… (lifecycle policy expires them).</summary>
public sealed class BlobRawArchive(BlobContainerClient container, ILogger<BlobRawArchive> logger) : IRawArchive
{
    public async Task SaveAsync(string tag, string requestPath, string body, CancellationToken ct)
    {
        try
        {
            var name = $"{DateTime.UtcNow:yyyy-MM-dd}/{tag}/{DateTime.UtcNow:HHmmss}-{Guid.NewGuid():N}.json.gz";
            using var ms = new MemoryStream();
            await using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            await using (var w = new StreamWriter(gz))
            {
                await w.WriteAsync(JsonSerializer.Serialize(new { requestPath, capturedUtc = DateTime.UtcNow, body }));
            }
            ms.Position = 0;
            await container.GetBlobClient(name).UploadAsync(ms, overwrite: false, ct);
        }
        catch (Exception ex)
        {
            // The archive is for audit/replay; it must never fail a sync.
            logger.LogWarning(ex, "Raw archive write failed for {Tag}", tag);
        }
    }
}
