using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Web.Auth;
using BarbershopCrm.Web.Pages;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarbershopCrm.Web.Pages.Owner.Branches;

[AuthorizePage(RoleCode.Owner)]
public class EditModel : AppPageModel
{
    private readonly AppDbContext _db;

    public EditModel(AppDbContext db, ICurrentUserAccessor currentUser) : base(currentUser)
    {
        _db = db;
    }

    [BindProperty]
    public BranchEditInput Input { get; set; } = new();

    public int BranchId { get; set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
    {
        var branch = await _db.Branches.FirstOrDefaultAsync(b => b.BranchId == id, ct);
        if (branch is null) return NotFound();

        BranchId = id;
        Input = new BranchEditInput
        {
            Name = branch.Name,
            Address = branch.Address,
            Phone = branch.Phone,
            OpeningTime = branch.OpeningTime.ToString("HH:mm"),
            ClosingTime = branch.ClosingTime.ToString("HH:mm"),
            IsActive = branch.IsActive,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken ct)
    {
        var branch = await _db.Branches.FirstOrDefaultAsync(b => b.BranchId == id, ct);
        if (branch is null) return NotFound();

        BranchId = id;

        if (!ModelState.IsValid)
            return Page();

        branch.Name = Input.Name.Trim();
        branch.Address = Input.Address.Trim();
        branch.Phone = string.IsNullOrWhiteSpace(Input.Phone) ? null : Input.Phone.Trim();
        branch.OpeningTime = TimeOnly.Parse(Input.OpeningTime);
        branch.ClosingTime = TimeOnly.Parse(Input.ClosingTime);
        branch.IsActive = Input.IsActive;

        await _db.SaveChangesAsync(ct);

        TempData["Success"] = $"Филиал «{branch.Name}» обновлён.";
        return RedirectToPage("Index");
    }

    public class BranchEditInput
    {
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string OpeningTime { get; set; } = "10:00";
        public string ClosingTime { get; set; } = "22:00";
        public bool IsActive { get; set; } = true;
    }
}
