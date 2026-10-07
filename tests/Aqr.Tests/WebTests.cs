using System.Net;
using System.Text;
using Aqr.Core.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aqr.Tests;

/// <summary>Development host: synthetic data, in-memory database, local developer signed in as AQR.Admin.</summary>
public sealed class DevAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development");

    public async Task WaitForDataAsync()
    {
        var factory = Services.GetRequiredService<IDbContextFactory<AqrDbContext>>();
        for (var i = 0; i < 100; i++)
        {
            await using var db = await factory.CreateDbContextAsync();
            if (await db.VmFamilies.AnyAsync() && await db.FamilyZoneAccess.AnyAsync()) return;
            await Task.Delay(200);
        }
        throw new TimeoutException("Initial mock sync didn't complete.");
    }
}

public sealed class WebTests(DevAppFactory factory) : IClassFixture<DevAppFactory>
{
    [Theory]
    [InlineData("/")]
    [InlineData("/Explorer")]
    [InlineData("/Explorer?zoneMode=physical&cat=GpuAccelerated&zstat=PartialZones&zstat=AllZones")]
    [InlineData("/Explorer?kind=RegionalTotal")]
    [InlineData("/Groups")]
    [InlineData("/Zones")]
    [InlineData("/Zones?zoneMode=physical")]
    [InlineData("/Trends")]
    [InlineData("/Families")]
    [InlineData("/Admin")]
    [InlineData("/Admin/Access")]
    public async Task Pages_render(string url)
    {
        await factory.WaitForDataAsync();
        var resp = await factory.CreateClient().GetAsync(url);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.True(resp.StatusCode == HttpStatusCode.OK, $"{url} → {(int)resp.StatusCode}: {body[..Math.Min(body.Length, 2000)]}");
        Assert.Contains("Azure Quota Reporting", body);
    }

    [Fact]
    public async Task Explorer_api_returns_rows_and_csv()
    {
        await factory.WaitForDataAsync();
        var client = factory.CreateClient();
        var json = await client.GetStringAsync("/api/v1/quota/subscriptions?cpu=AMD&pageSize=10");
        Assert.Contains("\"rows\"", json);
        Assert.Contains("AMD", json);

        var csv = await client.GetAsync("/api/v1/quota/subscriptions?format=csv");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("subscriptionId,", await csv.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_and_openapi()
    {
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        await factory.WaitForDataAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Contains("QuotaSubscriptions", await client.GetStringAsync("/openapi/v1.json"));
    }
}

/// <summary>Development host with Easy Auth semantics on (no dev user): identity only from X-MS-CLIENT-PRINCIPAL.</summary>
public sealed class EasyAuthAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Auth:EasyAuthEnabled", "true");
        builder.UseSetting("Auth:DevUser:Enabled", "false");
    }

    public static string Principal(params string[] roles)
    {
        var claims = string.Join(',', roles.Select(r => $"{{\"typ\":\"roles\",\"val\":\"{r}\"}}")
            .Append("{\"typ\":\"name\",\"val\":\"Test User\"}")
            .Append("{\"typ\":\"http://schemas.microsoft.com/identity/claims/objectidentifier\",\"val\":\"11111111-1111-1111-1111-111111111111\"}"));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{{\"auth_typ\":\"aad\",\"name_typ\":\"name\",\"role_typ\":\"roles\",\"claims\":[{claims}]}}"));
    }
}

public sealed class AuthTests(EasyAuthAppFactory factory) : IClassFixture<EasyAuthAppFactory>
{
    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Anonymous_browser_is_sent_to_easy_auth_login_and_api_gets_401()
    {
        var page = await Client().GetAsync("/Explorer");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.StartsWith("/.auth/login/aad", page.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client().GetAsync("/api/v1/quota/subscriptions")).StatusCode);
    }

    [Fact]
    public async Task Signed_in_user_without_an_app_role_is_forbidden()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/quota/subscriptions");
        req.Headers.Add("X-MS-CLIENT-PRINCIPAL", EasyAuthAppFactory.Principal());
        Assert.Equal(HttpStatusCode.Forbidden, (await Client().SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task Reader_without_grants_sees_no_rows_and_cannot_reach_admin()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/quota/subscriptions");
        req.Headers.Add("X-MS-CLIENT-PRINCIPAL", EasyAuthAppFactory.Principal("AQR.Reader"));
        var resp = await Client().SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("\"total\":0", await resp.Content.ReadAsStringAsync());

        var admin = new HttpRequestMessage(HttpMethod.Get, "/api/v1/sync/runs");
        admin.Headers.Add("X-MS-CLIENT-PRINCIPAL", EasyAuthAppFactory.Principal("AQR.Reader"));
        Assert.Equal(HttpStatusCode.Forbidden, (await Client().SendAsync(admin)).StatusCode);
    }
}

/// <summary>Outside Development with Easy Auth not configured, the app refuses traffic instead of trusting a forgeable header.</summary>
public sealed class FailClosedFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("Auth:EasyAuthEnabled", "false");
        builder.UseSetting("Aqr:UseMock", "true");
        builder.UseSetting("Aqr:Sync:Enabled", "false");
        builder.UseSetting("ConnectionStrings:AqrDb", "Server=tcp:127.0.0.1,1;Database=none;User Id=x;Password=y;Connect Timeout=1;Encrypt=False");
    }
}

public sealed class FailClosedTests(FailClosedFactory factory) : IClassFixture<FailClosedFactory>
{
    [Fact]
    public async Task Forged_principal_is_refused_when_easy_auth_is_off()
    {
        var client = factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/quota/subscriptions");
        req.Headers.Add("X-MS-CLIENT-PRINCIPAL", EasyAuthAppFactory.Principal("AQR.Admin"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.SendAsync(req)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }
}
