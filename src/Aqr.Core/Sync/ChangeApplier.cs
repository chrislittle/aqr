using Aqr.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Aqr.Core.Sync;

public readonly record struct ApplyCounts(int Inserted, int Updated, int Deleted)
{
    public static ApplyCounts operator +(ApplyCounts a, ApplyCounts b) =>
        new(a.Inserted + b.Inserted, a.Updated + b.Updated, a.Deleted + b.Deleted);
    public int Changed => Inserted + Updated + Deleted;
}

/// <summary>
/// Write-on-change (design doc §9.1). Inserts new keys, updates only rows whose values differ, and deletes keys
/// that are gone — but only where <paramref name="deletable"/> says the scope was fully observed. EF Core's change
/// tracker only issues an UPDATE for properties that actually changed, so an unchanged row is never touched and
/// creates no temporal history (design doc §8 rule 1).
/// </summary>
public static class ChangeApplier
{
    public static async Task<ApplyCounts> ApplyAsync<T>(
        AqrDbContext db,
        IQueryable<T> existingScope,
        IEnumerable<T> incoming,
        Func<T, string> key,
        Func<T, bool> deletable,
        CancellationToken ct) where T : class
    {
        var existing = await existingScope.ToListAsync(ct);
        var byKey = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in existing) byKey[key(e)] = e;

        int inserted = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in incoming)
        {
            var k = key(item);
            if (!seen.Add(k)) continue; // first occurrence wins; APIs occasionally repeat rows across pages
            if (byKey.TryGetValue(k, out var current))
            {
                db.Entry(current).CurrentValues.SetValues(item);
            }
            else
            {
                db.Add(item);
                inserted++;
            }
        }

        int deleted = 0;
        foreach (var e in existing)
        {
            if (!seen.Contains(key(e)) && deletable(e))
            {
                db.Remove(e);
                deleted++;
            }
        }

        var updated = db.ChangeTracker.Entries<T>().Count(e => e.State == EntityState.Modified);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return new ApplyCounts(inserted, updated, deleted);
    }
}
