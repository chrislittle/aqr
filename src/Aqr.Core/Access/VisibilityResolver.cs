using Aqr.Core.Data;
using Aqr.Core.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Aqr.Core.Access;

/// <summary>The signed-in caller as AQR sees it (from Easy Auth claims).</summary>
public sealed record UserContext(
    string ObjectId,
    string DisplayName,
    bool IsAdmin,
    bool IsReader,
    IReadOnlyList<string> GroupIds,
    bool GroupsIncomplete);

/// <summary>Which subscriptions a caller may see. Admins see everything; everyone else only what grants give them (deny by default).</summary>
public sealed class Visibility
{
    public static readonly Visibility Everything = new(true, new HashSet<string>(), []);

    public Visibility(bool all, HashSet<string> subscriptionIds, IReadOnlyList<AccessGrant> grants)
    {
        All = all;
        SubscriptionIds = subscriptionIds;
        Grants = grants;
    }

    public bool All { get; }
    public HashSet<string> SubscriptionIds { get; }
    public IReadOnlyList<AccessGrant> Grants { get; }
    public bool IsEmpty => !All && SubscriptionIds.Count == 0;
    public bool CanSee(string subscriptionId) => All || SubscriptionIds.Contains(subscriptionId);
}

/// <summary>
/// Resolves report visibility from admin-maintained grants (decision D6), independent of Azure RBAC.
/// Management-group grants resolve against current subscription placement, so moves are picked up automatically.
/// </summary>
public sealed class VisibilityResolver(IDbContextFactory<AqrDbContext> dbFactory, IMemoryCache cache)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(2);

    public async Task<Visibility> ResolveAsync(UserContext user, CancellationToken ct)
    {
        if (user.IsAdmin) return Visibility.Everything;
        if (!user.IsReader) return new Visibility(false, [], []);

        var cacheKey = "vis:" + user.ObjectId + ":" + string.Join(',', user.GroupIds.OrderBy(x => x));
        if (cache.TryGetValue(cacheKey, out Visibility? hit) && hit is not null) return hit;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var principals = user.GroupIds.Append(user.ObjectId).Select(x => x.ToLowerInvariant()).ToList();
        var grants = await db.AccessGrants.AsNoTracking().Where(g => principals.Contains(g.PrincipalObjectId)).ToListAsync(ct);

        var subs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mgTargets = grants.Where(g => g.TargetType == GrantTargetTypes.ManagementGroup).Select(g => "/" + g.TargetId.ToLowerInvariant() + "/").ToList();
        if (mgTargets.Count > 0)
        {
            var all = await db.Subscriptions.AsNoTracking().Select(s => new { s.SubscriptionId, s.ManagementGroupPath }).ToListAsync(ct);
            foreach (var s in all.Where(s => mgTargets.Any(t => s.ManagementGroupPath.Contains(t, StringComparison.OrdinalIgnoreCase))))
                subs.Add(s.SubscriptionId);
        }

        foreach (var g in grants.Where(g => g.TargetType == GrantTargetTypes.QuotaGroup))
        {
            var parts = g.TargetId.ToLowerInvariant().Split('/', 2);
            if (parts.Length != 2) continue;
            var members = await db.QuotaGroupMembers.AsNoTracking()
                .Where(m => m.ManagementGroupId == parts[0] && m.GroupName == parts[1]).Select(m => m.SubscriptionId).ToListAsync(ct);
            subs.UnionWith(members);
        }

        subs.UnionWith(grants.Where(g => g.TargetType == GrantTargetTypes.Subscription).Select(g => g.TargetId.ToLowerInvariant()));

        var vis = new Visibility(false, subs, grants);
        cache.Set(cacheKey, vis, CacheFor);
        return vis;
    }
}
