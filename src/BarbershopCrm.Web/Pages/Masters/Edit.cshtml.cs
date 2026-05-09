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
public class EditModel : AppPageModel
{
    private readonly AppDbContext _db;

    public EditModel(AppDbContext db, ICurrentUserAccessor currentUser) : base(currentUser)
    {
        _db = db;
    }

    [BindProperty]
    public MasterEditInput Input { get; set; } = new();

    public int MasterId { get; set; }
    public string MasterName { get; set; } = string.Empty;
    public IList<Service> AllServices { get; private set; } = Array.Empty<Service>();
    public IList<Branch> Branches { get; private set; } = Array.Empty<Branch>();

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
    {
        var master = await GetMasterQuery()
            .FirstOrDefaultAsync(m => m.MasterId == id, ct);

        if (master is null || !CanAccess(master))
            return NotFound();

        await LoadLookups(ct);
        PopulateFromMaster(master);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken ct)
    {
        var master = await _db.Masters
            .Include(m => m.Persona)
            .Include(m => m.MasterServices)
            .FirstOrDefaultAsync(m => m.MasterId == id, ct);

        if (master is null || !CanAccess(master))
            return NotFound();

        MasterId = id;
        MasterName = master.Persona.FullName;

        if (!ModelState.IsValid)
        {
            await LoadLookups(ct);
            return Page();
        }

        master.Persona.LastName = Input.LastName.Trim();
        master.Persona.FirstName = Input.FirstName.Trim();
        master.Persona.MiddleName = string.IsNullOrWhiteSpace(Input.MiddleName) ? null : Input.MiddleName.Trim();
        master.Persona.Phone = Input.Phone.Trim();
        master.Persona.Email = string.IsNullOrWhiteSpace(Input.Email) ? null : Input.Email.Trim();
        master.Position = Input.Position.Trim();
        master.Bio = string.IsNullOrWhiteSpace(Input.Bio) ? null : Input.Bio.Trim();
        master.IsActive = Input.IsActive;

        if (Current?.RoleCode == RoleCode.Owner && Input.BranchId.HasValue)
        {
            master.BranchId = Input.BranchId.Value;
        }

        // Update service bindings
        var selectedIds = Input.SelectedServiceIds ?? Array.Empty<int>();
        var currentIds = master.MasterServices.Select(ms => ms.ServiceId).ToHashSet();

        var toAdd = selectedIds.Except(currentIds);
        var toRemove = currentIds.Except(selectedIds);

        foreach (var serviceId in toAdd)
        {
            master.MasterServices.Add(new MasterService { MasterId = id, ServiceId = serviceId });
        }

        foreach (var ms in master.MasterServices.Where(ms => toRemove.Contains(ms.ServiceId)).ToList())
        {
            master.MasterServices.Remove(ms);
        }

        await _db.SaveChangesAsync(ct);

        TempData["Success"] = $"Мастер «{master.Persona.FullName}» обновлён.";
        return RedirectToPage("Manage");
    }

    private IQueryable<Master> GetMasterQuery()
    {
        return _db.Masters
            .Include(m => m.Persona)
            .Include(m => m.Branch)
            .Include(m => m.MasterServices);
    }

    private bool CanAccess(Master master)
    {
        if (Current?.RoleCode == RoleCode.Owner) return true;
        if (Current?.RoleCode == RoleCode.Admin && Current.BranchId == master.BranchId) return true;
        return false;
    }

    private void PopulateFromMaster(Master master)
    {
        MasterId = master.MasterId;
        MasterName = master.Persona.FullName;
        Input = new MasterEditInput
        {
            LastName = master.Persona.LastName,
            FirstName = master.Persona.FirstName,
            MiddleName = master.Persona.MiddleName,
            Phone = master.Persona.Phone,
            Email = master.Persona.Email,
            Position = master.Position,
            Bio = master.Bio,
            IsActive = master.IsActive,
            BranchId = master.BranchId,
            SelectedServiceIds = master.MasterServices.Select(ms => ms.ServiceId).ToArray(),
        };
    }

    private async Task LoadLookups(CancellationToken ct)
    {
        AllServices = await _db.Services
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .AsNoTracking()
            .ToListAsync(ct);

        Branches = await _db.Branches
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public class MasterEditInput
    {
        public string LastName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string? MiddleName { get; set; }
        public string Phone { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string Position { get; set; } = string.Empty;
        public string? Bio { get; set; }
        public bool IsActive { get; set; } = true;
        public int? BranchId { get; set; }
        public int[] SelectedServiceIds { get; set; } = Array.Empty<int>();
    }
}
