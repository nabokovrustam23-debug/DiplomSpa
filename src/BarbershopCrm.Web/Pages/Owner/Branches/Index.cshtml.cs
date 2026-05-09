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
public class IndexModel : AppPageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db, ICurrentUserAccessor currentUser) : base(currentUser)
    {
        _db = db;
    }

    public IList<Branch> Branches { get; private set; } = Array.Empty<Branch>();

    [BindProperty]
    public BranchInput Input { get; set; } = new();

    public string? SuccessMessage { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadBranches(ct);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await LoadBranches(ct);
            return Page();
        }

        var branch = new Branch
        {
            Name = Input.Name.Trim(),
            Address = Input.Address.Trim(),
            Phone = string.IsNullOrWhiteSpace(Input.Phone) ? null : Input.Phone.Trim(),
            OpeningTime = TimeOnly.Parse(Input.OpeningTime),
            ClosingTime = TimeOnly.Parse(Input.ClosingTime),
            IsActive = true,
        };

        _db.Branches.Add(branch);
        await _db.SaveChangesAsync(ct);

        TempData["Success"] = $"Филиал «{branch.Name}» создан.";
        return RedirectToPage();
    }

    private async Task LoadBranches(CancellationToken ct)
    {
        Branches = await _db.Branches
            .OrderBy(b => b.BranchId)
            .AsNoTracking()
            .ToListAsync(ct);

        SuccessMessage = TempData["Success"] as string;
    }

    public class BranchInput
    {
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string OpeningTime { get; set; } = "10:00";
        public string ClosingTime { get; set; } = "22:00";
    }
}
