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
            Latitude = branch.Latitude,
            Longitude = branch.Longitude,
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
        branch.Latitude = Input.Latitude;
        branch.Longitude = Input.Longitude;
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
        [Required(ErrorMessage = "Введите название филиала.")]
        [StringLength(120, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Введите адрес.")]
        [StringLength(300, MinimumLength = 5)]
        public string Address { get; set; } = string.Empty;

        [RegularExpression(PhoneValidation.RussianPhonePattern, ErrorMessage = PhoneValidation.ErrorMessage)]
        public string? Phone { get; set; }

        [Range(-90, 90)] public double? Latitude { get; set; }
        [Range(-180, 180)] public double? Longitude { get; set; }

        [Required, RegularExpression(@"^\d{2}:\d{2}$", ErrorMessage = "Время в формате ЧЧ:ММ.")]
        public string OpeningTime { get; set; } = "10:00";

        [Required, RegularExpression(@"^\d{2}:\d{2}$", ErrorMessage = "Время в формате ЧЧ:ММ.")]
        public string ClosingTime { get; set; } = "22:00";

        public bool IsActive { get; set; } = true;
    }
}
