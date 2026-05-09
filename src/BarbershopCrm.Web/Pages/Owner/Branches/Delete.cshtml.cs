using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Web.Auth;
using BarbershopCrm.Web.Pages;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarbershopCrm.Web.Pages.Owner.Branches;

[AuthorizePage(RoleCode.Owner)]
public class DeleteModel : AppPageModel
{
    private readonly AppDbContext _db;

    public DeleteModel(AppDbContext db, ICurrentUserAccessor currentUser) : base(currentUser)
    {
        _db = db;
    }

    public string BranchName { get; set; } = string.Empty;
    public int BranchId { get; set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
    {
        var branch = await _db.Branches
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.BranchId == id && b.IsActive, ct);

        if (branch is null) return NotFound();

        BranchId = id;
        BranchName = branch.Name;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken ct)
    {
        var branch = await _db.Branches.FirstOrDefaultAsync(b => b.BranchId == id, ct);
        if (branch is null) return NotFound();

        branch.IsActive = false;
        await _db.SaveChangesAsync(ct);

        TempData["Success"] = $"Филиал «{branch.Name}» деактивирован.";
        return RedirectToPage("Index");
    }
}
