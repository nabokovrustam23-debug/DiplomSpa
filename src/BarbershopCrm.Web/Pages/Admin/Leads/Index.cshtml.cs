using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Web.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarbershopCrm.Web.Pages.Admin.Leads;

[AuthorizePage(RoleCode.Admin, RoleCode.Owner)]
public class IndexModel : AppPageModel
{
    private readonly AppDbContext _db;

    public IndexModel(ICurrentUserAccessor cu, AppDbContext db) : base(cu)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)] public string? Status { get; set; }

    public List<Domain.Entities.Lead> Items { get; private set; } = new();
    public List<string> StatusOptions { get; } = LeadStatus.All.ToList();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Current is null) return Forbid();

        var q = _db.Leads.AsNoTracking()
            .Include(l => l.PreferredBranch)
            .AsQueryable();

        if (Current.RoleCode == RoleCode.Admin && Current.BranchId.HasValue)
        {
            var bid = Current.BranchId.Value;
            q = q.Where(l => l.PreferredBranchId == null || l.PreferredBranchId == bid);
        }

        if (!string.IsNullOrWhiteSpace(Status) && LeadStatus.All.Contains(Status))
            q = q.Where(l => l.Status == Status);

        Items = await q.OrderByDescending(l => l.CreatedAt).Take(200).ToListAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostTakeAsync(int id, CancellationToken ct)
    {
        if (Current is null) return Forbid();
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.LeadId == id, ct);
        if (lead is null) return NotFound();
        if (lead.Status != LeadStatus.New)
        {
            TempData["Error"] = "Заявка уже не в статусе «Новая».";
            return RedirectToPage(new { Status });
        }
        lead.Status = LeadStatus.InProgress;
        lead.ProcessedByUserId = Current.UserId;
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "Заявка взята в работу.";
        return RedirectToPage(new { Status });
    }

    public async Task<IActionResult> OnPostRejectAsync(int id, CancellationToken ct)
    {
        if (Current is null) return Forbid();
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.LeadId == id, ct);
        if (lead is null) return NotFound();
        if (lead.Status is LeadStatus.Done or LeadStatus.Rejected)
        {
            TempData["Error"] = "Заявка уже закрыта.";
            return RedirectToPage(new { Status });
        }
        lead.Status = LeadStatus.Rejected;
        lead.ProcessedByUserId = Current.UserId;
        lead.ProcessedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "Заявка отклонена.";
        return RedirectToPage(new { Status });
    }

    public async Task<IActionResult> OnPostDoneAsync(int id, CancellationToken ct)
    {
        if (Current is null) return Forbid();
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.LeadId == id, ct);
        if (lead is null) return NotFound();
        if (lead.Status is LeadStatus.Done or LeadStatus.Rejected)
        {
            TempData["Error"] = "Заявка уже закрыта.";
            return RedirectToPage(new { Status });
        }
        lead.Status = LeadStatus.Done;
        lead.ProcessedByUserId = Current.UserId;
        lead.ProcessedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "Заявка закрыта как обработанная.";
        return RedirectToPage(new { Status });
    }
}
