using Aqr.Core.Reporting;
using Aqr.Core.Sync;
using Aqr.Web.Auth;

namespace Aqr.Web.Api;

/// <summary>
/// /api/v1 — the same queries the UI uses, with the caller's report visibility applied server-side.
/// Callers authenticate with an Entra bearer token validated by Easy Auth (service principals need AQR.Reader).
/// </summary>
public static class ApiEndpoints
{
    public static void MapAqrApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization(Policies.Reader);

        api.MapGet("/quota/subscriptions", async (HttpRequest req, CurrentUser user, ReportService reports, CancellationToken ct) =>
        {
            var filter = ExplorerFilter.Parse(k => req.Query[k]);
            var vis = await user.VisibilityAsync(ct);
            if (string.Equals(req.Query["format"], "csv", StringComparison.OrdinalIgnoreCase))
            {
                // Every matching row, streamed (no paging), so large scopes are never silently truncated.
                return Results.Stream(async body =>
                {
                    await using var w = new StreamWriter(body, new System.Text.UTF8Encoding(false));
                    await w.WriteLineAsync(Csv.Header);
                    await foreach (var r in reports.ExplorerStreamAsync(vis, filter, ct))
                        await w.WriteLineAsync(Csv.Line(r));
                }, "text/csv", "aqr-quota.csv");
            }
            var result = await reports.ExplorerAsync(vis, filter, ct);
            return Results.Ok(new
            {
                result.Total, page = filter.Page, filter.PageSize, result.TotalUsage, result.TotalLimit, result.NotDeployable,
                asOf = filter.AsOf, historyAvailable = result.HistoryAvailable,
                rows = result.Rows.Select(r => new
                {
                    r.SubscriptionId, r.SubscriptionName, r.Region, r.Geography, r.QuotaName, r.Kind, r.FamilyId, r.Series,
                    r.Category, r.CpuManufacturer, r.Architecture, r.AcceleratorType, r.AcceleratorVendor, r.AcceleratorModel,
                    r.Generation, r.Features, r.Lifecycle, r.Usage, r.Limit, r.Available, utilPct = Math.Round(r.UtilPct, 1),
                    r.QuotaGroup, r.ZoneStatus,
                    zones = new
                    {
                        logical = new { offered = r.OfferedZonesLogical, open = r.OpenZonesLogical, partial = r.PartialZonesLogical, restricted = r.RestrictedZonesLogical },
                        physical = new { offered = r.OfferedZonesPhysical, open = r.OpenZonesPhysical, partial = r.PartialZonesPhysical, restricted = r.RestrictedZonesPhysical },
                    },
                }),
            });
        }).WithName("QuotaSubscriptions").WithSummary("Subscription × region × family quota with zonal access (all explorer filters as query parameters).");

        api.MapGet("/overview", async (DateTime? asOf, CurrentUser user, ReportService reports, CancellationToken ct) =>
            Results.Ok(await reports.OverviewAsync(await user.VisibilityAsync(ct), asOf, ct)))
            .WithName("Overview");

        api.MapGet("/quota/groups", async (string? group, string? region, DateTime? asOf, CurrentUser user, ReportService reports, CancellationToken ct) =>
            Results.Ok(await reports.GroupsAsync(await user.VisibilityAsync(ct), group, region, asOf, ct)))
            .WithName("QuotaGroups").WithSummary("Group limit, unallocated pool, allocations and usage. group = {managementGroupId}/{groupName}.");

        api.MapGet("/zones", async (string? region, string? family, DateTime? asOf, CurrentUser user, ReportService reports, CancellationToken ct) =>
            Results.Ok(await reports.ZonesAsync(await user.VisibilityAsync(ct), region, family, asOf, ct)))
            .WithName("Zones");

        api.MapGet("/families", async (CurrentUser user, ReportService reports, CancellationToken ct) =>
            Results.Ok(await reports.FamiliesAsync(await user.VisibilityAsync(ct), ct)))
            .WithName("Families");

        api.MapGet("/trends", async (string subscriptionId, string region, string quota, int? days, CurrentUser user, ReportService reports, CancellationToken ct) =>
            Results.Ok(await reports.TrendAsync(await user.VisibilityAsync(ct), subscriptionId, region, quota, DateTime.UtcNow.AddDays(-Math.Clamp(days ?? 30, 1, 730)), ct)))
            .WithName("Trends");

        var admin = app.MapGroup("/api/v1").RequireAuthorization(Policies.Admin);
        admin.MapGet("/sync/runs", async (ReportService reports, CancellationToken ct) =>
        {
            var (runs, gaps) = await reports.SyncHealthAsync(ct);
            return Results.Ok(new { runs, gaps });
        }).WithName("SyncRuns");
        admin.MapPost("/sync", async (CurrentUser user, SyncTrigger trigger, CancellationToken ct) =>
        {
            var u = await user.GetAsync(ct);
            return trigger.TryQueue(u.DisplayName) ? Results.Accepted() : Results.Conflict(new { message = "A sync is already queued." });
        }).WithName("TriggerSync");
    }
}

public static class Csv
{
    public const string Header = "subscriptionId,subscription,region,quota,series,category,cpuManufacturer,architecture,accelerator,quotaGroup,usage,limit,available,utilPct,zoneStatus,openZonesLogical,openZonesPhysical,restrictedZonesLogical,restrictedZonesPhysical";

    public static string Line(ExplorerRow r) => string.Join(',', new object?[]
    {
        r.SubscriptionId, r.SubscriptionName, r.Region, r.QuotaName, r.Series, r.Category, r.CpuManufacturer, r.Architecture,
        r.AcceleratorType == "None" ? "" : $"{r.AcceleratorVendor} {r.AcceleratorModel}".Trim(), r.QuotaGroup,
        r.Usage, r.Limit, r.Available, r.UtilPct.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), r.ZoneStatus,
        r.OpenZonesLogical, r.OpenZonesPhysical, r.RestrictedZonesLogical, r.RestrictedZonesPhysical,
    }.Select(Cell));

    // Quote every cell and neutralise spreadsheet formula injection.
    private static string Cell(object? v)
    {
        var s = v?.ToString() ?? "";
        if (s.Length > 0 && "=+-@\t\r".Contains(s[0])) s = "'" + s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
