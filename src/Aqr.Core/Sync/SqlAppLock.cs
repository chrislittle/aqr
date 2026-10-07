using System.Data;
using System.Data.Common;
using Aqr.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aqr.Core.Sync;

/// <summary>
/// Single-writer guarantee across App Service instances using a session-scoped SQL application lock
/// (sp_getapplock). Released when disposed or when the connection drops, so there's no stuck-lease recovery.
/// Pattern carried over from ghcp-credit-visibility-azure's SqlDistributedLease. In-memory (dev) is a no-op.
/// </summary>
public sealed class SqlAppLock : IAsyncDisposable
{
    public const string SyncResource = "Aqr:Sync";
    public const string MigrateResource = "Aqr:Migrate";

    private readonly AqrDbContext? _db;
    private readonly string _resource;
    private readonly ILogger _logger;

    private SqlAppLock(AqrDbContext? db, string resource, ILogger logger) { _db = db; _resource = resource; _logger = logger; }

    /// <param name="waitMs">0 = fail fast (another instance holds it); &gt;0 waits, used for migrations.</param>
    public static async Task<SqlAppLock?> TryAcquireAsync(IDbContextFactory<AqrDbContext> factory, string resource, ILogger logger, int waitMs, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct);
        try
        {
            if (!db.Database.IsRelational())
            {
                await db.DisposeAsync();
                return new SqlAppLock(null, resource, logger);
            }

            await db.Database.OpenConnectionAsync(ct);
            await using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "sp_getapplock";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandTimeout = Math.Max(30, waitMs / 1000 + 30);
            var rv = Add(cmd, "@Result", DbType.Int32, null, ParameterDirection.ReturnValue);
            Add(cmd, "@Resource", DbType.String, resource);
            Add(cmd, "@LockMode", DbType.String, "Exclusive");
            Add(cmd, "@LockOwner", DbType.String, "Session");
            Add(cmd, "@LockTimeout", DbType.Int32, waitMs);
            await cmd.ExecuteNonQueryAsync(ct);

            if (rv.Value is int r && r >= 0) return new SqlAppLock(db, resource, logger);
            await db.DisposeAsync();
            return null;
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_db is null) return;
        try
        {
            var conn = _db.Database.GetDbConnection();
            if (conn.State == ConnectionState.Open)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "sp_releaseapplock";
                cmd.CommandType = CommandType.StoredProcedure;
                Add(cmd, "@Resource", DbType.String, _resource);
                Add(cmd, "@LockOwner", DbType.String, "Session");
                await cmd.ExecuteNonQueryAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Releasing app lock {Resource} failed; closing the connection releases it.", _resource);
        }
        finally
        {
            await _db.DisposeAsync();
        }
    }

    private static DbParameter Add(DbCommand cmd, string name, DbType type, object? value, ParameterDirection dir = ParameterDirection.Input)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.DbType = type;
        p.Direction = dir;
        if (value is not null) p.Value = value;
        cmd.Parameters.Add(p);
        return p;
    }
}
