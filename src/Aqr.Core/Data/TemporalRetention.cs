using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aqr.Core.Data;

/// <summary>
/// Applies the configured temporal history retention (decision D3) to every system-versioned table, and
/// makes sure the database-level cleanup switch is on. Idempotent; runs after migrations on startup.
/// EF Core migrations can't express HISTORY_RETENTION_PERIOD, and the value is configurable, so it lives here.
/// </summary>
public static partial class TemporalRetention
{
    [GeneratedRegex(@"^(INFINITE|\d{1,4} (DAY|DAYS|WEEK|WEEKS|MONTH|MONTHS|YEAR|YEARS))$", RegexOptions.IgnoreCase)]
    private static partial Regex RetentionPattern();

    /// <summary>Validates a retention value against SQL's grammar. It's interpolated into DDL, so it must be allow-listed.</summary>
    public static string Normalize(string value)
    {
        var v = (value ?? "").Trim().ToUpperInvariant();
        if (!RetentionPattern().IsMatch(v))
            throw new ArgumentException($"Invalid history retention '{value}'. Use e.g. '1 YEAR', '18 MONTHS' or 'INFINITE'.");
        return v;
    }

    public static async Task ApplyAsync(AqrDbContext db, string retention, ILogger logger, CancellationToken ct)
    {
        if (!db.Database.IsSqlServer()) return;
        var period = Normalize(retention);

        await db.Database.ExecuteSqlRawAsync("ALTER DATABASE CURRENT SET TEMPORAL_HISTORY_RETENTION ON;", ct);
        foreach (var (schema, table) in AqrDbContext.TemporalTables)
        {
            // Table names come from a compile-time list and the period is allow-listed above.
            var sql = $"ALTER TABLE [{schema}].[{table}] SET (SYSTEM_VERSIONING = ON (HISTORY_RETENTION_PERIOD = {period}));";
            await db.Database.ExecuteSqlRawAsync(sql, ct);
        }
        logger.LogInformation("Temporal history retention set to {Retention} on {Count} tables.", period, AqrDbContext.TemporalTables.Count);
    }
}
