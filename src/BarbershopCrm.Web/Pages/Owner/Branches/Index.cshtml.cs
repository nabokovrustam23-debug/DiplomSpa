using System.ComponentModel.DataAnnotations;
using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Web.Auth;
using BarbershopCrm.Web.Pages;
using BarbershopCrm.Web.Services;
using BarbershopCrm.Web.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarbershopCrm.Web.Pages.Owner.Branches;

[AuthorizePage(RoleCode.Owner)]
public class IndexModel : AppPageModel
{
    private readonly AppDbContext _db;
    private readonly IImageUploadService _images;

    public IndexModel(AppDbContext db, ICurrentUserAccessor currentUser, IImageUploadService images) : base(currentUser)
    {
        _db = db;
        _images = images;
    }

    public IList<Branch> Branches { get; private set; } = Array.Empty<Branch>();

    [BindProperty]
    public BranchInput Input { get; set; } = new();

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

        string? imageUrl;
        try
        {
            imageUrl = await _images.SaveAsync(Input.ImageFile, "branches", ct);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError("Input.ImageFile", ex.Message);
            await LoadBranches(ct);
            return Page();
        }

        var branch = new Branch
        {
            Name = Input.Name.Trim(),
            Address = Input.Address.Trim(),
            Latitude = Input.Latitude,
            Longitude = Input.Longitude,
            Phone = string.IsNullOrWhiteSpace(Input.Phone) ? null : Input.Phone.Trim(),
            ImageUrl = imageUrl,
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
    }

    public class BranchInput
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

        [Display(Name = "Фото филиала")]
        public IFormFile? ImageFile { get; set; }

        [Required, RegularExpression(@"^\d{2}:\d{2}$", ErrorMessage = "Время в формате ЧЧ:ММ.")]
        public string OpeningTime { get; set; } = "10:00";

        [Required, RegularExpression(@"^\d{2}:\d{2}$", ErrorMessage = "Время в формате ЧЧ:ММ.")]
        public string ClosingTime { get; set; } = "22:00";
    }
}
