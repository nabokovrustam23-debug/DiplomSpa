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
public class EditModel : AppPageModel
{
    private readonly AppDbContext _db;
    private readonly IImageUploadService _images;

    public EditModel(AppDbContext db, ICurrentUserAccessor currentUser, IImageUploadService images) : base(currentUser)
    {
        _db = db;
        _images = images;
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
            ExistingImageUrl = branch.ImageUrl,
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
        Input.ExistingImageUrl = branch.ImageUrl;

        if (!ModelState.IsValid)
            return Page();

        if (Input.RemoveImage)
        {
            _images.Delete(branch.ImageUrl);
            branch.ImageUrl = null;
        }

        if (Input.ImageFile is { Length: > 0 })
        {
            string? newUrl;
            try
            {
                newUrl = await _images.SaveAsync(Input.ImageFile, "branches", ct);
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError("Input.ImageFile", ex.Message);
                return Page();
            }
            _images.Delete(branch.ImageUrl);
            branch.ImageUrl = newUrl;
        }

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
        [StringLength(120, MinimumLength = 2, ErrorMessage = "Название должно быть от 2 до 120 символов.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Введите адрес.")]
        [StringLength(300, MinimumLength = 5, ErrorMessage = "Адрес должен быть от 5 до 300 символов.")]
        public string Address { get; set; } = string.Empty;

        [RegularExpression(PhoneValidation.RussianPhonePattern, ErrorMessage = PhoneValidation.ErrorMessage)]
        public string? Phone { get; set; }

        [Range(-90, 90, ErrorMessage = "Широта должна быть от -90 до 90.")] public double? Latitude { get; set; }
        [Range(-180, 180, ErrorMessage = "Долгота должна быть от -180 до 180.")] public double? Longitude { get; set; }

        [Display(Name = "Фото филиала")]
        public IFormFile? ImageFile { get; set; }

        public string? ExistingImageUrl { get; set; }

        public bool RemoveImage { get; set; }

        [Required(ErrorMessage = "Укажите время открытия."), RegularExpression(@"^\d{2}:\d{2}$", ErrorMessage = "Время в формате ЧЧ:ММ.")]
        public string OpeningTime { get; set; } = "10:00";

        [Required(ErrorMessage = "Укажите время закрытия."), RegularExpression(@"^\d{2}:\d{2}$", ErrorMessage = "Время в формате ЧЧ:ММ.")]
        public string ClosingTime { get; set; } = "22:00";

        public bool IsActive { get; set; } = true;
    }
}
