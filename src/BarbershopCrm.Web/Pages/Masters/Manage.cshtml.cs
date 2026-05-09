using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Web.Auth;
using BarbershopCrm.Web.Pages;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarbershopCrm.Web.Pages.Masters;

[AuthorizePage(RoleCode.Admin, RoleCode.Owner)]
public class ManageModel : AppPageModel
{
    private readonly AppDbContext _db;

    public ManageModel(AppDbContext db, ICurrentUserAccessor currentUser) : base(currentUser)
    {
        _db = db;
    }

    public IList<Master> Masters { get; private set; } = Array.Empty<Master>();
    public IList<Branch> Branches { get; private set; } = Array.Empty<Branch>();

    [BindProperty]
    public MasterInput Input { get; set; } = new();

    public string? SuccessMessage { get; set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadData(ct);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await LoadData(ct);
            return Page();
        }

        var branchId = ResolveBranchId(Input.BranchId);
        if (branchId is null)
        {
            ModelState.AddModelError("", "Невалидный филиал.");
            await LoadData(ct);
            return Page();
        }

        var persona = new Persona
        {
            LastName = Input.LastName.Trim(),
            FirstName = Input.FirstName.Trim(),
            MiddleName = string.IsNullOrWhiteSpace(Input.MiddleName) ? null : Input.MiddleName.Trim(),
            Phone = Input.Phone.Trim(),
            Email = string.IsNullOrWhiteSpace(Input.Email) ? null : Input.Email.Trim(),
        };

        var master = new Master
        {
            Persona = persona,
            BranchId = branchId.Value,
            Position = Input.Position.Trim(),
            HireDate = DateOnly.FromDateTime(DateTime.Today),
            IsActive = true,
        };

        _db.Persona.Add(persona);
        _db.Masters.Add(master);
        await _db.SaveChangesAsync(ct);

        TempData["Success"] = $"Мастер «{persona.FullName}» добавлен.";
        return RedirectToPage();
    }

    private async Task LoadData(CancellationToken ct)
    {
        var query = _db.Masters
            .Include(m => m.Persona)
            .Include(m => m.Branch)
            .Include(m => m.MasterServices).ThenInclude(ms => ms.Service)
            .AsNoTracking();

        if (Current?.RoleCode == RoleCode.Admin && Current.BranchId.HasValue)
        {
            query = query.Where(m => m.BranchId == Current.BranchId.Value);
        }

        Masters = await query.OrderBy(m => m.Branch.Name).ThenBy(m => m.Persona.LastName).ToListAsync(ct);

        Branches = await _db.Branches
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .AsNoTracking()
            .ToListAsync(ct);

        SuccessMessage = TempData["Success"] as string;
    }

    private int? ResolveBranchId(int? inputBranchId)
    {
        if (Current?.RoleCode == RoleCode.Admin)
            return Current.BranchId;

        return inputBranchId;
    }

    public class MasterInput
    {
        public string LastName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string? MiddleName { get; set; }
        public string Phone { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string Position { get; set; } = "Барбер";
        public int? BranchId { get; set; }
    }
}
