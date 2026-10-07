using Aqr.Core;
using Aqr.Core.Access;
using Aqr.Core.Catalog;
using Aqr.Core.Data;
using Aqr.Core.Reporting;
using Aqr.Core.Sources;
using Aqr.Core.Sync;
using Aqr.Web.Api;
using Aqr.Web.Auth;
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.AddOptions<AqrOptions>().Bind(config.GetSection(AqrOptions.Section))
    .Validate(o => { TemporalRetention.Normalize(o.HistoryRetention); return true; }, "Invalid Aqr:HistoryRetention")
    .ValidateOnStart();
var aqr = config.GetSection(AqrOptions.Section).Get<AqrOptions>() ?? new AqrOptions();

// ---- Data: Azure SQL (managed identity) in Azure; the Azure SQL Database container locally; in-memory only as a last-resort UI preview.
var conn = config.GetConnectionString("AqrDb");
if (string.IsNullOrWhiteSpace(conn))
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("ConnectionStrings:AqrDb is required outside Development.");
    var memoryDb = "aqr-local-" + Guid.NewGuid().ToString("N"); // one per host, so parallel hosts (tests) never share
    builder.Services.AddDbContextFactory<AqrDbContext>(o => o.UseInMemoryDatabase(memoryDb));
}
else
{
    builder.Services.AddDbContextFactory<AqrDbContext>(o => o.UseSqlServer(conn, sql =>
    {
        sql.EnableRetryOnFailure(6, TimeSpan.FromSeconds(20), null);
        sql.CommandTimeout(180);
    }));
}

// ---- Azure identity: the user-assigned managed identity (AZURE_CLIENT_ID) in Azure; az login locally.
builder.Services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
builder.Services.AddSingleton(TimeProvider.System);

// ---- Data source: Azure Resource Manager APIs, or deterministic synthetic data.
if (aqr.UseMock)
{
    builder.Services.AddSingleton<IQuotaSource, MockQuotaSource>();
}
else
{
    builder.Services.AddTransient<ArmAuthHandler>();
    var arm = builder.Services.AddHttpClient<IQuotaSource, ArmQuotaSource>(c =>
    {
        c.BaseAddress = new Uri("https://management.azure.com");
        c.Timeout = Timeout.InfiniteTimeSpan; // the resilience pipeline owns timeouts
    });
    // Outermost: retries (honours Retry-After on 429), so every attempt gets a fresh token from the inner handler.
    arm.AddStandardResilienceHandler(o =>
    {
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(100);
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(200);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(6);
        o.Retry.MaxRetryAttempts = 6;
    });
    arm.AddHttpMessageHandler<ArmAuthHandler>();
}

if (!string.IsNullOrWhiteSpace(aqr.RawArchive.BlobServiceUri))
{
    builder.Services.AddSingleton<IRawArchive>(sp => new BlobRawArchive(
        new BlobServiceClient(new Uri(aqr.RawArchive.BlobServiceUri), sp.GetRequiredService<TokenCredential>())
            .GetBlobContainerClient(aqr.RawArchive.Container),
        sp.GetRequiredService<ILogger<BlobRawArchive>>()));
}
else
{
    builder.Services.AddSingleton<IRawArchive, NoRawArchive>();
}

builder.Services.AddSingleton(_ => FamilyCatalog.Load(Path.Combine(AppContext.BaseDirectory, "catalog", "vm-families.json")));
builder.Services.AddScoped<SyncService>();
builder.Services.AddSingleton<SyncTrigger>();
builder.Services.AddSingleton<DatabaseState>();
builder.Services.AddHostedService<SyncWorker>();
builder.Services.AddSingleton<ReportService>();
builder.Services.AddSingleton<VisibilityResolver>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient("graph");
builder.Services.AddSingleton<GraphGroupResolver>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();

// ---- Auth: Easy Auth in front; app roles AQR.Reader / AQR.Admin; everything requires a role by default.
builder.Services.AddAuthentication(EasyAuthAuthenticationHandler.SchemeName)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, EasyAuthAuthenticationHandler>(EasyAuthAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Reader, p => p.RequireAuthenticatedUser().RequireRole(Roles.Reader, Roles.Admin))
    .AddPolicy(Policies.Admin, p => p.RequireAuthenticatedUser().RequireRole(Roles.Admin))
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser().RequireRole(Roles.Reader, Roles.Admin).Build());

builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/Admin", Policies.Admin);
    o.Conventions.AllowAnonymousToPage("/NoAccess");
    o.Conventions.AllowAnonymousToPage("/Error");
});
builder.Services.AddOpenApi();
// Telemetry only when App Insights is configured (Azure); local runs and tests have no connection string.
if (!string.IsNullOrWhiteSpace(config["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
    builder.Services.AddApplicationInsightsTelemetry();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseMiddleware<EasyAuthClaimsMiddleware>();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
app.MapGet("/health/ready", (DatabaseState s) => s.Ready
        ? Results.Ok(new { status = "ready" })
        : Results.Json(new { status = "starting", error = s.LastError }, statusCode: 503))
    .AllowAnonymous();
app.MapOpenApi().RequireAuthorization(Policies.Reader);
app.MapAqrApi();
app.MapRazorPages();

app.Run();

public static class Policies
{
    public const string Reader = "AqrReader";
    public const string Admin = "AqrAdmin";
}

public partial class Program;
