using System.ComponentModel.DataAnnotations;
using BarbershopCrm.Domain.Entities;
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
public class IndexModel : AppPageModel
{
    private readonly AppDbContext _db;
    private readonly IImageUploadService _images;

    public IndexModel(AppDbContext db, ICurrentUserAccessor currentUser, IImageUploadService images) : base(currentUser)
    {
        _db = db;
        _images = images;
    }

    public IList<BarbershopCrm.Domain.Entities.Service> Services { get; private set; } = Array.Empty<BarbershopCrm.Domain.Entities.Service>();

    [BindProperty]
    public ServiceInput Input { get; set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadServices(ct);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await LoadServices(ct);
            return Page();
        }

        string? imageUrl;
        try
        {
            imageUrl = await _images.SaveAsync(Input.ImageFile, "services", ct);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError("Input.ImageFile", ex.Message);
            await LoadServices(ct);
            return Page();
        }

        var service = new BarbershopCrm.Domain.Entities.Service
        {
            Name = Input.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim(),
            DurationMinutes = Input.DurationMinutes,
            Price = Input.Price,
            ImageUrl = imageUrl,
            IsActive = true,
        };

        _db.Services.Add(service);
        await _db.SaveChangesAsync(ct);

        TempData["Success"] = $"Услуга «{service.Name}» создана.";
        return RedirectToPage();
    }

    private async Task LoadServices(CancellationToken ct)
    {
        Services = await _db.Services
            .OrderBy(s => s.ServiceId)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public class ServiceInput
    {
        [Required(ErrorMessage = "Введите название услуги.")]
        [StringLength(120, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        [Range(15, 480, ErrorMessage = "Длительность от 15 до 480 минут.")]
        public int DurationMinutes { get; set; } = 30;

        [Range(typeof(decimal), "200", "1000000", ErrorMessage = "Цена не может быть меньше 200 ₽.")]
        public decimal Price { get; set; } = 200m;

        [Display(Name = "Фото услуги")]
        public IFormFile? ImageFile { get; set; }
    }
}
