using System.Threading.Channels;
using Aqr.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aqr.Core.Sync;

/// <summary>Queue for admin-triggered syncs (POST /api/v1/sync).</summary>
public sealed class SyncTrigger
{
    private readonly Channel<SyncRequest> _channel = Channel.CreateBounded<SyncRequest>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    public bool TryQueue(string requestedBy) => _channel.Writer.TryWrite(new SyncRequest("Manual:" + requestedBy, Force: true));
    public ChannelReader<SyncRequest> Reader => _channel.Reader;
}

/// <summary>Tracks database readiness for /health/ready and the UI.</summary>
public sealed class DatabaseState
{
    public volatile bool Ready;
    public volatile string? LastError;
}

/// <summary>
/// Applies EF Core migrations (under an app lock, so only one instance migrates), sets temporal retention, then
/// runs the sync on its schedule. Every instance runs this loop; the sync's own app lock makes one of them the writer.
/// </summary>
public sealed class SyncWorker(
    IServiceProvider services,
    IDbContextFactory<AqrDbContext> dbFactory,
    SyncTrigger trigger,
    DatabaseState state,
    IOptions<AqrOptions> options,
    ILogger<SyncWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await InitializeDatabaseAsync(stoppingToken);
        if (!options.Value.Sync.Enabled)
        {
            logger.LogWarning("Sync is disabled (Aqr:Sync:Enabled=false).");
            return;
        }

        var next = new SyncRequest("Startup");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = services.CreateScope();
                var sync = scope.ServiceProvider.GetRequiredService<SyncService>();
                var outcome = await sync.RunAsync(next, stoppingToken);
                if (!outcome.Ran) logger.LogDebug("Sync skipped: {Reason}", outcome.SkippedReason);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Sync cycle failed");
            }

            next = new SyncRequest("Schedule");
            using var delay = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            delay.CancelAfter(Tick);
            try
            {
                next = await trigger.Reader.ReadAsync(delay.Token);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { }
        }
    }

    private async Task InitializeDatabaseAsync(CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using (var db = await dbFactory.CreateDbContextAsync(ct))
                {
                    if (db.Database.IsRelational())
                    {
                        await using var migrateLock = await SqlAppLock.TryAcquireAsync(dbFactory, SqlAppLock.MigrateResource, logger, 120_000, ct)
                            ?? throw new InvalidOperationException("Timed out waiting for the migration lock.");
                        await db.Database.MigrateAsync(ct);
                        await TemporalRetention.ApplyAsync(db, options.Value.HistoryRetention, logger, ct);
                    }
                    else
                    {
                        await db.Database.EnsureCreatedAsync(ct);
                    }
                }
                state.Ready = true;
                state.LastError = null;
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                state.LastError = ex.Message;
                var wait = TimeSpan.FromSeconds(Math.Min(300, 10 * Math.Pow(2, Math.Min(attempt++, 5))));
                logger.LogError(ex, "Database initialization failed; retrying in {Wait}", wait);
                await Task.Delay(wait, ct);
            }
        }
    }
}
