using System.ComponentModel.DataAnnotations;
using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Web.Auth;
using BarbershopCrm.Web.Pages;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarbershopCrm.Web.Pages.Owner.Services;

[AuthorizePage(RoleCode.Owner)]
public class IndexModel : AppPageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db, ICurrentUserAccessor currentUser) : base(currentUser)
    {
        _db = db;
    }

    public IList<Service> Services { get; private set; } = Array.Empty<Service>();

    [BindProperty]
    public ServiceInput Input { get; set; } = new();

    public string? SuccessMessage { get; set; }

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

        var service = new Service
        {
            Name = Input.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim(),
            DurationMinutes = Input.DurationMinutes,
            Price = Input.Price,
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

        SuccessMessage = TempData["Success"] as string;
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
    }
}
