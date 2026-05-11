using System.ComponentModel.DataAnnotations;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Web.Auth;
using BarbershopCrm.Web.Pages;
using BarbershopCrm.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarbershopCrm.Web.Pages.Owner.Services;

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
    public ServiceEditInput Input { get; set; } = new();

    public int ServiceId { get; set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
    {
        var service = await _db.Services.FirstOrDefaultAsync(s => s.ServiceId == id, ct);
        if (service is null) return NotFound();

        ServiceId = id;
        Input = new ServiceEditInput
        {
            Name = service.Name,
            Description = service.Description,
            DurationMinutes = service.DurationMinutes,
            Price = service.Price,
            ExistingImageUrl = service.ImageUrl,
            IsActive = service.IsActive,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken ct)
    {
        var service = await _db.Services.FirstOrDefaultAsync(s => s.ServiceId == id, ct);
        if (service is null) return NotFound();

        ServiceId = id;
        Input.ExistingImageUrl = service.ImageUrl;

        if (!ModelState.IsValid)
            return Page();

        if (Input.RemoveImage)
        {
            _images.Delete(service.ImageUrl);
            service.ImageUrl = null;
        }

        if (Input.ImageFile is { Length: > 0 })
        {
            string? newUrl;
            try
            {
                newUrl = await _images.SaveAsync(Input.ImageFile, "services", ct);
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError("Input.ImageFile", ex.Message);
                return Page();
            }
            _images.Delete(service.ImageUrl);
            service.ImageUrl = newUrl;
        }

        service.Name = Input.Name.Trim();
        service.Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim();
        service.DurationMinutes = Input.DurationMinutes;
        service.Price = Input.Price;
        service.IsActive = Input.IsActive;

        await _db.SaveChangesAsync(ct);

        TempData["Success"] = $"Услуга «{service.Name}» обновлена.";
        return RedirectToPage("Index");
    }

    public class ServiceEditInput
    {
        [Required(ErrorMessage = "Введите название услуги.")]
        [StringLength(120, MinimumLength = 2, ErrorMessage = "Название должно быть от 2 до 120 символов.")]
        public string Name { get; set; } = string.Empty;

        [StringLength(500, ErrorMessage = "Описание слишком длинное (макс. 500 символов).")]
        public string? Description { get; set; }

        [Range(15, 480, ErrorMessage = "Длительность от 15 до 480 минут.")]
        public int DurationMinutes { get; set; } = 30;

        [Range(typeof(decimal), "200", "1000000", ErrorMessage = "Цена не может быть меньше 200 ₽.")]
        public decimal Price { get; set; } = 200m;

        [Display(Name = "Фото услуги")]
        public IFormFile? ImageFile { get; set; }

        public string? ExistingImageUrl { get; set; }

        public bool RemoveImage { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
