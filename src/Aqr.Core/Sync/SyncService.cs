using System.Collections.Concurrent;
using Aqr.Core.Catalog;
using Aqr.Core.Data;
using Aqr.Core.Model;
using Aqr.Core.Sources;
using Aqr.Core.Zones;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aqr.Core.Sync;

public sealed record SyncRequest(string Trigger, bool Force = false);

public sealed record SyncOutcome(Guid RunId, bool Ran, IReadOnlyList<SyncRun> Stages, string? SkippedReason = null);

/// <summary>
/// The sync pipeline (design doc §9): Inventory → SubQuota → Groups → Zones → Catalog. Each stage records a
/// sync.Run row with coverage counts. Partial coverage is a Warning, and data for an unobserved scope is
/// never deleted.
/// </summary>
public sealed class SyncService(
    IDbContextFactory<AqrDbContext> dbFactory,
    IQuotaSource source,
    FamilyCatalog catalog,
    IOptions<AqrOptions> options,
    TimeProvider time,
    ILogger<SyncService> logger)
{
    private readonly AqrOptions _o = options.Value;

    /// <summary>Per-family attributes observed in the SKUs API during this process lifetime (fed to the catalog stage).</summary>
    private readonly ConcurrentDictionary<string, SkuFamilyFacts> _skuFacts = new(StringComparer.OrdinalIgnoreCase);

    public async Task<SyncOutcome> RunAsync(SyncRequest request, CancellationToken ct)
    {
        var runId = Guid.NewGuid();
        await using var appLock = await SqlAppLock.TryAcquireAsync(dbFactory, SqlAppLock.SyncResource, logger, 0, ct);
        if (appLock is null)
            return new SyncOutcome(runId, false, [], "Another instance is running the sync.");

        var due = await DueStagesAsync(request.Force, ct);
        if (due.Count == 0) return new SyncOutcome(runId, false, [], "Nothing due.");

        var stages = new List<SyncRun>();
        var inventory = await RunStageAsync(runId, SyncStages.Inventory, request.Trigger, InventoryAsync, ct);
        stages.Add(inventory);
        if (inventory.Status == "Failed")
            return new SyncOutcome(runId, true, stages);

        var inventoryComplete = inventory.Status == "Succeeded";
        if (due.Contains(SyncStages.SubQuota))
            stages.Add(await RunStageAsync(runId, SyncStages.SubQuota, request.Trigger, (r, c) => SubQuotaAsync(r, inventoryComplete, c), ct));
        if (due.Contains(SyncStages.Groups))
            stages.Add(await RunStageAsync(runId, SyncStages.Groups, request.Trigger, GroupsAsync, ct));
        if (due.Contains(SyncStages.Zones))
            stages.Add(await RunStageAsync(runId, SyncStages.Zones, request.Trigger, ZonesAsync, ct));
        stages.Add(await RunStageAsync(runId, SyncStages.Catalog, request.Trigger, CatalogAsync, ct));
        return new SyncOutcome(runId, true, stages);
    }

    private async Task<HashSet<string>> DueStagesAsync(bool force, CancellationToken ct)
    {
        var all = new HashSet<string> { SyncStages.SubQuota, SyncStages.Groups, SyncStages.Zones };
        if (force) return all;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var last = await db.SyncRuns.Where(r => r.Status != "Running" && r.Status != "Failed")
            .GroupBy(r => r.Stage).Select(g => new { Stage = g.Key, Last = g.Max(r => r.StartedUtc) }).ToListAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        bool Due(string stage, TimeSpan every) =>
            last.FirstOrDefault(l => l.Stage == stage) is not { } l || now - l.Last >= every - TimeSpan.FromMinutes(2);
        return all.Where(s => s switch
        {
            SyncStages.SubQuota => Due(s, _o.Sync.QuotaInterval),
            SyncStages.Groups => Due(s, _o.Sync.GroupInterval),
            SyncStages.Zones => Due(s, _o.Sync.ZoneInterval),
            _ => false,
        }).ToHashSet();
    }

    private async Task<SyncRun> RunStageAsync(Guid runId, string stage, string trigger,
        Func<SyncRun, CancellationToken, Task> body, CancellationToken ct)
    {
        var run = new SyncRun { RunId = runId, Stage = stage, Trigger = trigger, StartedUtc = time.GetUtcNow().UtcDateTime };
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            db.SyncRuns.Add(run);
            await db.SaveChangesAsync(ct);
        }

        var throttledBefore = source.Stats.Throttled;
        try
        {
            await body(run, ct);
            if (run.Status == "Running")
                run.Status = run.SubsCovered < run.SubsExpected ? "Warning" : "Succeeded";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Sync stage {Stage} failed", stage);
            run.Status = "Failed";
            run.Message = Trim(ex.Message, 4000);
        }
        run.Throttled = source.Stats.Throttled - throttledBefore;
        run.EndedUtc = time.GetUtcNow().UtcDateTime;
        await using (var db = await dbFactory.CreateDbContextAsync(CancellationToken.None))
        {
            db.SyncRuns.Update(run);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        logger.LogInformation("Sync {Stage}: {Status} covered {Covered}/{Expected} seen {Seen} +{Ins} ~{Upd} -{Del}",
            stage, run.Status, run.SubsCovered, run.SubsExpected, run.KeysSeen, run.Inserted, run.Updated, run.Deleted);
        return run;
    }

    // ------------------------------------------------------------------ Inventory (S6)
    private async Task InventoryAsync(SyncRun run, CancellationToken ct)
    {
        var mgs = _o.ManagementGroupIdList;
        if (mgs.Count == 0 && _o.SubscriptionIdList.Count == 0 && !_o.UseMock)
            throw new InvalidOperationException("No scope configured: set Aqr:ManagementGroupIds (AQR_MANAGEMENT_GROUP_IDS) and/or Aqr:SubscriptionIds (AQR_SUBSCRIPTION_IDS).");

        var subs = await source.GetSubscriptionsAsync(mgs, _o.SubscriptionIdList, ct);
        var incoming = subs.Select(s => new Subscription
        {
            SubscriptionId = s.SubscriptionId.ToLowerInvariant(),
            Name = s.Name,
            State = s.State,
            ManagementGroupPath = "/" + string.Concat(s.ManagementGroupAncestors.Select(a => a.ToLowerInvariant() + "/")),
        }).ToList();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var c = await ChangeApplier.ApplyAsync(db, db.Subscriptions, incoming, x => x.SubscriptionId, _ => true, ct);
        Record(run, c, incoming.Count, incoming.Count, incoming.Count);
    }

    // ------------------------------------------------------------------ SubQuota (S1, S2 fallback)
    private async Task SubQuotaAsync(SyncRun run, bool inventoryComplete, CancellationToken ct)
    {
        List<string> subIds;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            subIds = await db.Subscriptions.Select(s => s.SubscriptionId).ToListAsync(ct);

        var regionFilter = _o.RegionList.Count > 0 ? new HashSet<string>(_o.RegionList, StringComparer.OrdinalIgnoreCase) : null;
        var usages = (await source.GetQuotaUsagesAsync(subIds, _o.IncludeEmptyQuota, ct))
            .Where(u => regionFilter is null || regionFilter.Contains(u.Region))
            .Where(u => _o.IncludeEmptyQuota || u.Limit > 0 || u.CurrentValue > 0 || u.Name.Equals("cores", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var bySub = usages.GroupBy(u => u.SubscriptionId.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.ToList());

        // S2 fallback for subscriptions Resource Graph returned nothing for.
        var coverage = new List<SyncCoverage>();
        var missing = subIds.Where(s => !bySub.ContainsKey(s)).ToList();
        await Parallel.ForEachAsync(missing, new ParallelOptions { MaxDegreeOfParallelism = _o.Sync.MaxParallel, CancellationToken = ct }, async (sub, token) =>
        {
            try
            {
                var regions = (await source.GetLocationsAsync(sub, token)).Select(l => l.Name)
                    .Where(r => regionFilter is null || regionFilter.Contains(r)).ToList();
                var rows = new List<QuotaUsage>();
                foreach (var r in regions)
                {
                    try { rows.AddRange((await source.GetComputeUsagesAsync(sub, r, token)).Where(u => _o.IncludeEmptyQuota || u.Limit > 0 || u.CurrentValue > 0 || u.Name.Equals("cores", StringComparison.OrdinalIgnoreCase))); }
                    catch (ArmRequestException ex) when ((int)ex.Status is 400 or 404 or 409) { /* region not enabled for this subscription */ }
                }
                lock (bySub) bySub[sub] = rows;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lock (coverage) coverage.Add(Cov(run, sub, "Failed", ex.Message));
            }
        });

        var covered = bySub.Keys.Intersect(subIds, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var total = new ApplyCounts();
        int seen = 0;
        foreach (var batch in subIds.Chunk(_o.Sync.SubscriptionBatchSize))
        {
            var incoming = batch.Where(covered.Contains).SelectMany(s => bySub[s]).Select(ToEntity).ToList();
            seen += incoming.Count;
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var ids = batch.ToList();
            await EnsureRegionsAsync(db, incoming.Select(i => i.Region), ct);
            total += await ChangeApplier.ApplyAsync(db,
                db.SubscriptionQuotas.Where(q => ids.Contains(q.SubscriptionId)),
                incoming, Key, q => covered.Contains(q.SubscriptionId), ct);
        }

        // Subscriptions that left the inventory: their quota rows end (history keeps them).
        if (inventoryComplete)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            total += await ChangeApplier.ApplyAsync(db,
                db.SubscriptionQuotas.Where(q => !subIds.Contains(q.SubscriptionId)), [], Key, _ => true, ct);
        }

        await SaveCoverageAsync(coverage, ct);
        Record(run, total, subIds.Count, covered.Count, seen);

        static string Key(SubscriptionQuota q) => $"{q.SubscriptionId}|{q.Region}|{q.QuotaName}";
    }

    private static SubscriptionQuota ToEntity(QuotaUsage u)
    {
        var name = Keys.Family(u.Name);
        var isCores = name == "cores";
        return new SubscriptionQuota
        {
            SubscriptionId = u.SubscriptionId.ToLowerInvariant(),
            Region = u.Region.ToLowerInvariant(),
            QuotaName = name,
            RawName = u.Name,
            Kind = isCores ? QuotaKinds.RegionalTotal : QuotaKinds.Family,
            FamilyId = isCores ? null : name,
            LocalizedName = u.LocalizedName,
            Usage = u.CurrentValue,
            Limit = u.Limit,
        };
    }

    private static async Task EnsureRegionsAsync(AqrDbContext db, IEnumerable<string> regions, CancellationToken ct)
    {
        var names = regions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var known = await db.Regions.Where(r => names.Contains(r.Name)).Select(r => r.Name).ToListAsync(ct);
        foreach (var n in names.Except(known, StringComparer.OrdinalIgnoreCase))
            db.Regions.Add(new Region { Name = n, DisplayName = n });
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    // ------------------------------------------------------------------ Quota Groups (S3)
    private async Task GroupsAsync(SyncRun run, CancellationToken ct)
    {
        // Quota Groups live under management groups; with only a subscription scope there are none to read.
        var mgs = _o.ManagementGroupIdList.Count > 0 ? _o.ManagementGroupIdList : _o.UseMock ? ["mg-contoso"] : [];
        var regionFilter = _o.RegionList.Count > 0 ? new HashSet<string>(_o.RegionList, StringComparer.OrdinalIgnoreCase) : null;

        var groups = new List<QuotaGroup>();
        var members = new List<QuotaGroupMember>();
        var quotas = new List<GroupQuota>();
        var allocs = new List<GroupAllocation>();
        var completeGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var completeMgs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // "{mg}|{group}"
        var coverage = new List<SyncCoverage>();
        int groupCount = 0;

        foreach (var mg in mgs)
        {
            IReadOnlyList<QuotaGroupInfo> found;
            try { found = await source.GetQuotaGroupsAsync(mg, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                coverage.Add(Cov(run, "mg:" + mg, "Failed", ex.Message));
                continue;
            }
            completeMgs.Add(mg.ToLowerInvariant());

            foreach (var g in found)
            {
                groupCount++;
                var mgId = g.ManagementGroupId.ToLowerInvariant();
                var gKey = $"{mgId}|{g.GroupName.ToLowerInvariant()}";
                try
                {
                    var subs = (await source.GetQuotaGroupSubscriptionsAsync(g.ManagementGroupId, g.GroupName, ct))
                        .Select(s => s.ToLowerInvariant()).ToList();
                    List<string> regions;
                    await using (var db = await dbFactory.CreateDbContextAsync(ct))
                        regions = await db.SubscriptionQuotas.Where(q => subs.Contains(q.SubscriptionId))
                            .Select(q => q.Region).Distinct().ToListAsync(ct);
                    if (regionFilter is not null) regions = regions.Where(regionFilter.Contains).ToList();

                    var perRegion = new ConcurrentBag<(string Region, IReadOnlyList<GroupQuotaLimitInfo> Limits, IReadOnlyList<GroupQuotaUsageInfo> Usages)>();
                    await Parallel.ForEachAsync(regions, new ParallelOptions { MaxDegreeOfParallelism = _o.Sync.MaxParallel, CancellationToken = ct }, async (r, token) =>
                    {
                        var limits = await source.GetGroupQuotaLimitsAsync(g.ManagementGroupId, g.GroupName, r, token);
                        var uses = await source.GetGroupQuotaUsagesAsync(g.ManagementGroupId, g.GroupName, r, token);
                        perRegion.Add((r, limits, uses));
                    });

                    groups.Add(new QuotaGroup { ManagementGroupId = mgId, GroupName = g.GroupName.ToLowerInvariant(), DisplayName = g.DisplayName });
                    members.AddRange(subs.Select(s => new QuotaGroupMember { ManagementGroupId = mgId, GroupName = g.GroupName.ToLowerInvariant(), SubscriptionId = s }));
                    foreach (var (region, limits, uses) in perRegion)
                    {
                        var usageByFamily = uses.GroupBy(u => Keys.Family(u.ResourceName)).ToDictionary(x => x.Key, x => x.First());
                        foreach (var l in limits)
                        {
                            var fam = Keys.Family(l.ResourceName);
                            quotas.Add(new GroupQuota
                            {
                                ManagementGroupId = mgId, GroupName = g.GroupName.ToLowerInvariant(), Region = region.ToLowerInvariant(), FamilyId = fam,
                                GroupLimit = l.Limit, AvailableLimit = l.AvailableLimit,
                                AllocatedTotal = l.AllocatedToSubscriptions.Values.Sum(),
                                GroupUsage = usageByFamily.TryGetValue(fam, out var u) ? u.Usages : null,
                            });
                            allocs.AddRange(l.AllocatedToSubscriptions.Select(a => new GroupAllocation
                            {
                                ManagementGroupId = mgId, GroupName = g.GroupName.ToLowerInvariant(), SubscriptionId = a.Key.ToLowerInvariant(),
                                Region = region.ToLowerInvariant(), FamilyId = fam, QuotaAllocated = a.Value,
                            }));
                        }
                    }
                    completeGroups.Add(gKey);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failedGroups.Add(gKey);
                    coverage.Add(Cov(run, "group:" + g.GroupName, "Failed", ex.Message));
                }
            }
        }

        bool GroupDone(string mg, string group) => completeGroups.Contains($"{mg}|{group}");
        var total = new ApplyCounts();
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            total += await ChangeApplier.ApplyAsync(db, db.QuotaGroups, groups, x => $"{x.ManagementGroupId}|{x.GroupName}",
                x => GroupDone(x.ManagementGroupId, x.GroupName) || IsRemovedGroup(x.ManagementGroupId, x.GroupName), ct);
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            total += await ChangeApplier.ApplyAsync(db, db.QuotaGroupMembers, members, x => $"{x.ManagementGroupId}|{x.GroupName}|{x.SubscriptionId}",
                x => GroupDone(x.ManagementGroupId, x.GroupName) || IsRemovedGroup(x.ManagementGroupId, x.GroupName), ct);
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            total += await ChangeApplier.ApplyAsync(db, db.GroupQuotas, quotas, x => $"{x.ManagementGroupId}|{x.GroupName}|{x.Region}|{x.FamilyId}",
                x => GroupDone(x.ManagementGroupId, x.GroupName) || IsRemovedGroup(x.ManagementGroupId, x.GroupName), ct);
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            total += await ChangeApplier.ApplyAsync(db, db.GroupAllocations, allocs, x => $"{x.ManagementGroupId}|{x.GroupName}|{x.SubscriptionId}|{x.Region}|{x.FamilyId}",
                x => GroupDone(x.ManagementGroupId, x.GroupName) || IsRemovedGroup(x.ManagementGroupId, x.GroupName), ct);

        await SaveCoverageAsync(coverage, ct);
        Record(run, total, groupCount, completeGroups.Count, groups.Count + members.Count + quotas.Count + allocs.Count);
        if (completeMgs.Count < mgs.Count && run.Status == "Running") run.Status = "Warning";

        // A group whose MG listed fine, that wasn't returned, and that didn't merely fail this run.
        bool IsRemovedGroup(string mg, string group) =>
            completeMgs.Contains(mg) && !failedGroups.Contains($"{mg}|{group}")
            && !groups.Any(g => g.ManagementGroupId == mg && g.GroupName == group);
    }

    // ------------------------------------------------------------------ Zones / SKUs (S4, S5)
    private async Task ZonesAsync(SyncRun run, CancellationToken ct)
    {
        List<string> subIds;
        List<(string Sub, string Region, string Family)> quotaKeys;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            subIds = await db.Subscriptions.Select(s => s.SubscriptionId).ToListAsync(ct);
            quotaKeys = (await db.SubscriptionQuotas.Where(q => q.Kind == QuotaKinds.Family && (q.Limit > 0 || q.Usage > 0))
                .Select(q => new { q.SubscriptionId, q.Region, q.FamilyId }).ToListAsync(ct))
                .Select(x => (x.SubscriptionId, x.Region, x.FamilyId!)).ToList();
        }
        var famBySubRegion = quotaKeys.GroupBy(k => (k.Sub, k.Region))
            .ToDictionary(g => g.Key, g => g.Select(x => x.Family).ToHashSet(StringComparer.OrdinalIgnoreCase));

        var results = new ConcurrentDictionary<string, SubZoneResult>(StringComparer.OrdinalIgnoreCase);
        var coverage = new ConcurrentBag<SyncCoverage>();
        var regionsMeta = new ConcurrentDictionary<string, LocationInfo>(StringComparer.OrdinalIgnoreCase);
        var options = new ParallelOptions { MaxDegreeOfParallelism = _o.Sync.MaxParallel, CancellationToken = ct };

        // 1) Locations (zone mappings) per subscription.
        var locationsBySub = new ConcurrentDictionary<string, IReadOnlyList<LocationInfo>>(StringComparer.OrdinalIgnoreCase);
        var failedSubs = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await Parallel.ForEachAsync(subIds, options, async (sub, token) =>
        {
            try
            {
                var locations = await source.GetLocationsAsync(sub, token);
                foreach (var l in locations) regionsMeta.TryAdd(l.Name, l);
                locationsBySub[sub] = locations;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { failedSubs.TryAdd(sub, ex.Message); }
        });

        // 2) SKUs per (subscription, region), all pairs in parallel. Live: ~8 s per call and ~48 regions per subscription,
        //    so per-subscription sequencing took ~7 min/subscription. The unfiltered SKUs call returned 247 MB in 50 s,
        //    so per-region calls it is.
        var perPair = new ConcurrentDictionary<(string Sub, string Region), (List<FamilyZoneAccess> Fams, List<SkuRestriction> Restr)>();
        var pairs = famBySubRegion.Keys.Where(k => locationsBySub.ContainsKey(k.Sub)).ToList();
        await Parallel.ForEachAsync(pairs, options, async (pair, token) =>
        {
            var (sub, region) = pair;
            if (failedSubs.ContainsKey(sub)) return;
            try
            {
                var wanted = famBySubRegion[pair];
                var skus = await source.GetVmSkusAsync(sub, region, token);
                foreach (var s in skus) Observe(s);
                var map = locationsBySub[sub].FirstOrDefault(l => l.Name.Equals(region, StringComparison.OrdinalIgnoreCase))?.ZoneMappings
                          ?? new Dictionary<string, string>();
                var evaluated = ZoneEvaluator.Evaluate(sub, region, skus.Where(s => wanted.Contains(Keys.Family(s.Family))).ToList(), map);
                var fams = evaluated.Select(r => r.Family).ToList();
                // Families that hold quota here but have no SKU offered to this subscription in this region.
                var offered = fams.Select(f => f.FamilyId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                fams.AddRange(wanted.Where(w => !offered.Contains(w)).Select(w => ZoneEvaluator.NotOffered(sub, region, w)));
                perPair[pair] = (fams, evaluated.SelectMany(r => r.Restrictions).ToList());
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { failedSubs.TryAdd(sub, $"{region}: {ex.Message}"); }
        });

        foreach (var (sub, error) in failedSubs) coverage.Add(Cov(run, sub, "Failed", error));
        foreach (var sub in subIds.Where(s => locationsBySub.ContainsKey(s) && !failedSubs.ContainsKey(s)))
        {
            var mappings = locationsBySub[sub].SelectMany(l => l.ZoneMappings.Select(z => new ZoneMapping
            {
                SubscriptionId = sub, Region = l.Name.ToLowerInvariant(), LogicalZone = z.Key, PhysicalZone = z.Value,
            })).ToList();
            var mine = perPair.Where(kv => kv.Key.Sub == sub).Select(kv => kv.Value).ToList();
            results[sub] = new SubZoneResult(mappings, mine.SelectMany(m => m.Fams).ToList(), mine.SelectMany(m => m.Restr).ToList());
        }
        var total = new ApplyCounts();
        int seen = 0;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            foreach (var l in regionsMeta.Values)
            {
                var name = l.Name.ToLowerInvariant();
                var r = await db.Regions.FindAsync([name], ct);
                if (r is null) db.Regions.Add(r = new Region { Name = name });
                r.DisplayName = l.DisplayName;
                r.Geography = l.Geography;
                r.HasZones = l.ZoneMappings.Count > 0;
            }
            await db.SaveChangesAsync(ct);
        }

        foreach (var batch in subIds.Chunk(_o.Sync.SubscriptionBatchSize))
        {
            var ids = batch.ToList();
            bool Done(string s) => results.ContainsKey(s);
            var maps = ids.Where(Done).SelectMany(s => results[s].Mappings).ToList();
            var fams = ids.Where(Done).SelectMany(s => results[s].Families).ToList();
            var restr = ids.Where(Done).SelectMany(s => results[s].Restrictions).ToList();
            seen += maps.Count + fams.Count + restr.Count;

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            total += await ChangeApplier.ApplyAsync(db, db.ZoneMappings.Where(x => ids.Contains(x.SubscriptionId)), maps,
                x => $"{x.SubscriptionId}|{x.Region}|{x.LogicalZone}", x => Done(x.SubscriptionId), ct);
            total += await ChangeApplier.ApplyAsync(db, db.FamilyZoneAccess.Where(x => ids.Contains(x.SubscriptionId)), fams,
                x => $"{x.SubscriptionId}|{x.Region}|{x.FamilyId}", x => Done(x.SubscriptionId), ct);
            total += await ChangeApplier.ApplyAsync(db, db.SkuRestrictions.Where(x => ids.Contains(x.SubscriptionId)), restr,
                x => $"{x.SubscriptionId}|{x.Region}|{x.Sku}", x => Done(x.SubscriptionId), ct);
        }

        await SaveCoverageAsync(coverage.ToList(), ct);
        Record(run, total, subIds.Count, results.Count, seen);
    }

    private sealed record SubZoneResult(List<ZoneMapping> Mappings, List<FamilyZoneAccess> Families, List<SkuRestriction> Restrictions);

    private void Observe(SkuInfo s)
    {
        var facts = _skuFacts.GetOrAdd(Keys.Family(s.Family), _ => new SkuFamilyFacts());
        lock (facts)
        {
            facts.Skus.Add(s.Name);
            if (s.IntCapability("vCPUs") is int v)
            {
                facts.MinVcpu = facts.MinVcpu == 0 ? v : Math.Min(facts.MinVcpu, v);
                facts.MaxVcpu = Math.Max(facts.MaxVcpu, v);
            }
            if (s.IntCapability("GPUs") is > 0) facts.HasGpu = true;
            if (s.Capabilities.TryGetValue("CpuArchitectureType", out var arch)) facts.Architecture = arch.Equals("Arm64", StringComparison.OrdinalIgnoreCase) ? "Arm64" : "x64";
            if (Is(s, "PremiumIO")) facts.Features.Add("premiumSsd");
            if (Is(s, "RdmaEnabled")) facts.Features.Add("rdma");
            if (s.IntCapability("MaxResourceVolumeMB") is > 0) facts.Features.Add("localDisk");
            if (s.Capabilities.ContainsKey("ConfidentialComputingType")) facts.Features.Add("confidential");
            if (s.Capabilities.TryGetValue("RetirementDateUtc", out var rd)
                && DateTime.TryParse(rd, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var when)
                && when.Year < 9999)
                facts.Retirement = facts.Retirement is { } prior && prior < when ? prior : when;
            facts.RawName ??= s.Family;
        }
        static bool Is(SkuInfo s, string cap) => s.Capabilities.TryGetValue(cap, out var v) && v.Equals("True", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class SkuFamilyFacts
    {
        public HashSet<string> Skus { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int MinVcpu { get; set; }
        public int MaxVcpu { get; set; }
        public bool HasGpu { get; set; }
        public string? Architecture { get; set; }
        public HashSet<string> Features { get; } = new(StringComparer.OrdinalIgnoreCase);
        public DateTime? Retirement { get; set; }
        public string? RawName { get; set; }
    }

    // ------------------------------------------------------------------ Catalog (S7 ⨝ observed)
    private async Task CatalogAsync(SyncRun run, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var observed = await db.SubscriptionQuotas.Where(q => q.Kind == QuotaKinds.Family)
            .GroupBy(q => q.FamilyId!).Select(g => new { FamilyId = g.Key, Localized = g.Max(x => x.LocalizedName), Raw = g.Max(x => x.RawName) })
            .ToListAsync(ct);
        var groupFams = await db.GroupQuotas.Select(g => g.FamilyId).Distinct().ToListAsync(ct);
        var existing = await db.VmFamilies.AsNoTracking().ToDictionaryAsync(f => f.FamilyId, StringComparer.OrdinalIgnoreCase, ct);

        var ids = observed.Select(o => o.FamilyId).Concat(groupFams).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var incoming = ids.Select(id =>
        {
            var obs = observed.FirstOrDefault(o => o.FamilyId.Equals(id, StringComparison.OrdinalIgnoreCase));
            var localized = obs?.Localized ?? "";
            existing.TryGetValue(id, out var prev);
            _skuFacts.TryGetValue(id, out var seen);
            // Prefer the name exactly as Azure returned it; fall back to the stored one, then the key.
            var quotaName = obs?.Raw is { Length: > 0 } raw ? raw : seen?.RawName ?? (prev?.QuotaName is { Length: > 0 } p ? p : QuotaNameFromLocalized(id, localized));
            var f = catalog.Classify(quotaName, localized);
            f.FamilyId = Keys.Family(id);

            if (_skuFacts.TryGetValue(id, out var facts))
            {
                lock (facts)
                {
                    f.MinVcpu = facts.MinVcpu; f.MaxVcpu = facts.MaxVcpu; f.SkuCount = facts.Skus.Count;
                    f.RetirementDate = facts.Retirement;
                    if (facts.Architecture is not null) f.Architecture = facts.Architecture;
                    if (facts.HasGpu && f.AcceleratorType == "None") f.AcceleratorType = "GPU";
                    f.Features = string.Join(',', f.Features.Split(',', StringSplitOptions.RemoveEmptyEntries).Concat(facts.Features)
                        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x));
                }
            }
            else if (prev is not null)
            {
                // No SKU data this process lifetime: keep what an earlier Zones stage learned.
                f.MinVcpu = prev.MinVcpu; f.MaxVcpu = prev.MaxVcpu; f.SkuCount = prev.SkuCount; f.RetirementDate = prev.RetirementDate;
                f.Architecture = prev.Architecture; f.Features = prev.Features.Length > 0 ? prev.Features : f.Features;
                if (prev.AcceleratorType != "None" && f.AcceleratorType == "None") f.AcceleratorType = prev.AcceleratorType;
            }
            return f;
        }).ToList();

        var c = await ChangeApplier.ApplyAsync(db, db.VmFamilies, incoming, f => f.FamilyId, _ => true, ct);
        Record(run, c, 0, 0, incoming.Count);
    }

    /// <summary>
    /// QuotaResources reports names like "standardDSv5Family"; we store them lower-cased as keys. Keep the
    /// original casing when the localized name gives it away ("Standard DSv5 Family vCPUs"); otherwise use the key.
    /// </summary>
    internal static string QuotaNameFromLocalized(string familyId, string localized)
    {
        var l = localized.Trim();
        if (l.StartsWith("Standard ", StringComparison.OrdinalIgnoreCase) && l.EndsWith(" Family vCPUs", StringComparison.OrdinalIgnoreCase))
        {
            var core = l["Standard ".Length..^" Family vCPUs".Length].Replace(" ", "");
            var candidate = "standard" + core + "Family";
            if (candidate.Equals(familyId, StringComparison.OrdinalIgnoreCase)) return candidate;
        }
        return familyId;
    }

    // ------------------------------------------------------------------ helpers
    private static void Record(SyncRun run, ApplyCounts c, int expected, int covered, int seen)
    {
        run.Inserted = c.Inserted; run.Updated = c.Updated; run.Deleted = c.Deleted;
        run.SubsExpected = expected; run.SubsCovered = covered; run.KeysSeen = seen;
    }

    private static SyncCoverage Cov(SyncRun run, string scope, string status, string error) =>
        new() { RunId = run.RunId, Stage = run.Stage, SubscriptionId = Trim(scope, 64), Status = status, Error = Trim(error, 2000) };

    private async Task SaveCoverageAsync(IReadOnlyCollection<SyncCoverage> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.SyncCoverage.AddRange(rows.GroupBy(r => r.SubscriptionId).Select(g => g.First()));
        await db.SaveChangesAsync(ct);
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max];
}
