using Aqr.Core;
using Aqr.Core.Catalog;
using Aqr.Core.Data;
using Aqr.Core.Sources;
using Aqr.Core.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aqr.Tests;

public sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

public static class TestHost
{
    public static readonly string CatalogPath = Path.Combine(AppContext.BaseDirectory, "catalog", "vm-families.json");

    public static FamilyCatalog Catalog() => FamilyCatalog.Load(CatalogPath);

    public static IDbContextFactory<AqrDbContext> InMemoryFactory(string? name = null)
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<AqrDbContext>(o => o.UseInMemoryDatabase(name ?? Guid.NewGuid().ToString()));
        return services.BuildServiceProvider().GetRequiredService<IDbContextFactory<AqrDbContext>>();
    }

    public static IDbContextFactory<AqrDbContext> SqlFactory(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<AqrDbContext>(o => o.UseSqlServer(connectionString));
        return services.BuildServiceProvider().GetRequiredService<IDbContextFactory<AqrDbContext>>();
    }

    public static SyncService Sync(IDbContextFactory<AqrDbContext> factory, TimeProvider time, IQuotaSource? source = null, AqrOptions? options = null) =>
        new(factory, source ?? new MockQuotaSource(time), Catalog(),
            Options.Create(options ?? new AqrOptions { UseMock = true }), time, NullLogger<SyncService>.Instance);
}

/// <summary>Runs only when AQR_TEST_SQL holds a connection string (the Azure SQL Database container, or a dev database in Azure).</summary>
public sealed class SqlFactAttribute : FactAttribute
{
    public const string EnvVar = "AQR_TEST_SQL";
    public SqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvVar)))
            Skip = $"Set {EnvVar} to a connection string for the Azure SQL Database container to run SQL integration tests (see docs/LOCAL_DEV.md).";
    }
}
