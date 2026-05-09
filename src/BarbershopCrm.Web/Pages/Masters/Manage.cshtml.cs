using System.ComponentModel.DataAnnotations;
using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Web.Auth;
using BarbershopCrm.Web.Pages;
using BarbershopCrm.Web.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarbershopCrm.Web.Pages.Masters;

[AuthorizePage(RoleCode.Admin, RoleCode.Owner)]
public class ManageModel : AppPageModel
{
    private const string DefaultPosition = "Барбер";

    private readonly AppDbContext _db;

    public ManageModel(AppDbContext db, ICurrentUserAccessor currentUser) : base(currentUser)
    {
        _db = db;
    }

    public IList<Master> Masters { get; private set; } = Array.Empty<Master>();
    public IList<Branch> Branches { get; private set; } = Array.Empty<Branch>();
    public IList<Service> AllServices { get; private set; } = Array.Empty<Service>();

    [BindProperty]
    public MasterInput Input { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadData(ct);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (Input.SelectedServiceIds is null || Input.SelectedServiceIds.Length == 0)
        {
            ModelState.AddModelError("Input.SelectedServiceIds",
                "Выберите минимум одну услугу, которую выполняет мастер.");
        }

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

        var validServiceIds = await _db.Services
            .Where(s => s.IsActive && Input.SelectedServiceIds!.Contains(s.ServiceId))
            .Select(s => s.ServiceId)
            .ToListAsync(ct);

        if (validServiceIds.Count == 0)
        {
            ModelState.AddModelError("Input.SelectedServiceIds",
                "Выберите минимум одну услугу, которую выполняет мастер.");
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
            Position = DefaultPosition,
            HireDate = DateOnly.FromDateTime(DateTime.Today),
            IsActive = true,
        };

        foreach (var sid in validServiceIds)
        {
            master.MasterServices.Add(new MasterService { ServiceId = sid });
        }

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

        AllServices = await _db.Services
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    private int? ResolveBranchId(int? inputBranchId)
    {
        if (Current?.RoleCode == RoleCode.Admin)
            return Current.BranchId;

        return inputBranchId;
    }

    public class MasterInput
    {
        [Required(ErrorMessage = "Введите фамилию.")]
        [StringLength(60, MinimumLength = 1)]
        public string LastName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Введите имя.")]
        [StringLength(60, MinimumLength = 1)]
        public string FirstName { get; set; } = string.Empty;

        [StringLength(60)]
        public string? MiddleName { get; set; }

        [Required(ErrorMessage = "Введите телефон.")]
        [RegularExpression(PhoneValidation.RussianPhonePattern, ErrorMessage = PhoneValidation.ErrorMessage)]
        public string Phone { get; set; } = string.Empty;

        [EmailAddress(ErrorMessage = "Некорректный email.")]
        public string? Email { get; set; }

        public int? BranchId { get; set; }

        public int[] SelectedServiceIds { get; set; } = Array.Empty<int>();
    }
}
