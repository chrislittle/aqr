using Aqr.Core.Data;
using Aqr.Core.Model;
using Aqr.Core.Reporting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Aqr.Web.Pages.Admin;

/// <summary>Report visibility grants (decision D6). The temporal history of access.Grant is the audit trail.</summary>
public sealed class AccessModel(IDbContextFactory<AqrDbContext> dbFactory, ReportService reports, IMemoryCache cache) : AqrPageModel
{
    public IReadOnlyList<AccessGrant> Grants { get; private set; } = [];
    public IReadOnlyList<(AccessGrant Grant, DateTime From, DateTime To)> History { get; private set; } = [];
    public IReadOnlyList<string> ManagementGroups { get; private set; } = [];
    public IReadOnlyList<(string Key, string Label)> QuotaGroups { get; private set; } = [];
    public IReadOnlyList<(string Id, string Name)> Subscriptions { get; private set; } = [];
    [TempData] public string? Message { get; set; }

    [BindProperty] public AccessInput Input { get; set; } = new();

    public sealed class AccessInput
    {
        public string PrincipalObjectId { get; set; } = "";
        public string PrincipalType { get; set; } = "Group";
        public string PrincipalDisplayName { get; set; } = "";
        public string TargetType { get; set; } = GrantTargetTypes.ManagementGroup;
        public string TargetId { get; set; } = "";
        public string Note { get; set; } = "";
    }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostAddAsync(CancellationToken ct)
    {
        var oid = Input.PrincipalObjectId.Trim().ToLowerInvariant();
        if (!Guid.TryParse(oid, out _)) ModelState.AddModelError("", "Principal object ID must be a GUID (Entra group or user object ID).");
        if (Input.PrincipalType is not ("Group" or "User")) ModelState.AddModelError("", "Principal type must be Group or User.");
        if (!GrantTargetTypes.All.Contains(Input.TargetType)) ModelState.AddModelError("", "Unknown target type.");
        if (string.IsNullOrWhiteSpace(Input.TargetId)) ModelState.AddModelError("", "Pick a target.");
        if (!ModelState.IsValid) { await LoadAsync(ct); return Page(); }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var target = Input.TargetId.Trim().ToLowerInvariant();
        if (await db.AccessGrants.AnyAsync(g => g.PrincipalObjectId == oid && g.TargetType == Input.TargetType && g.TargetId == target, ct))
        {
            Message = "That grant already exists.";
            return RedirectToPage();
        }
        db.AccessGrants.Add(new AccessGrant
        {
            GrantId = Guid.NewGuid(), PrincipalObjectId = oid, PrincipalType = Input.PrincipalType,
            PrincipalDisplayName = Input.PrincipalDisplayName.Trim(), TargetType = Input.TargetType, TargetId = target,
            Note = Input.Note.Trim(), ChangedBy = User0.DisplayName + " (" + User0.ObjectId + ")",
        });
        await db.SaveChangesAsync(ct);
        InvalidateVisibilityCache();
        Message = "Grant added. It applies within two minutes.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var g = await db.AccessGrants.FindAsync([id], ct);
        if (g is not null)
        {
            // Stamp who removed it first, so the history row carries the remover.
            g.ChangedBy = User0.DisplayName + " (" + User0.ObjectId + ") [removed]";
            await db.SaveChangesAsync(ct);
            db.AccessGrants.Remove(g);
            await db.SaveChangesAsync(ct);
            InvalidateVisibilityCache();
            Message = "Grant removed.";
        }
        return RedirectToPage();
    }

    private void InvalidateVisibilityCache()
    {
        if (cache is MemoryCache mc) mc.Clear();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        Grants = await db.AccessGrants.AsNoTracking().OrderBy(g => g.PrincipalDisplayName).ThenBy(g => g.TargetType).ToListAsync(ct);
        var paths = await db.Subscriptions.AsNoTracking().Select(s => s.ManagementGroupPath).ToListAsync(ct);
        ManagementGroups = paths.SelectMany(p => p.Split('/', StringSplitOptions.RemoveEmptyEntries)).Distinct().OrderBy(x => x).ToList();
        QuotaGroups = (await db.QuotaGroups.AsNoTracking().ToListAsync(ct)).Select(g => (g.ManagementGroupId + "/" + g.GroupName, $"{g.GroupName} ({g.ManagementGroupId})")).ToList();
        Subscriptions = (await db.Subscriptions.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct)).Select(s => (s.SubscriptionId, s.Name)).ToList();
        History = await reports.GrantHistoryAsync(ct);
    }
}
