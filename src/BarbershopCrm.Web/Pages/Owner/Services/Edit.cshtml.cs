using System.ComponentModel.DataAnnotations;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Web.Auth;
using BarbershopCrm.Web.Pages;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarbershopCrm.Web.Pages.Owner.Services;

[AuthorizePage(RoleCode.Owner)]
public class EditModel : AppPageModel
{
    private readonly AppDbContext _db;

    public EditModel(AppDbContext db, ICurrentUserAccessor currentUser) : base(currentUser)
    {
        _db = db;
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
            IsActive = service.IsActive,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken ct)
    {
        var service = await _db.Services.FirstOrDefaultAsync(s => s.ServiceId == id, ct);
        if (service is null) return NotFound();

        ServiceId = id;

        if (!ModelState.IsValid)
            return Page();

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
        [StringLength(120, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        [Range(15, 480, ErrorMessage = "Длительность от 15 до 480 минут.")]
        public int DurationMinutes { get; set; } = 30;

        [Range(typeof(decimal), "200", "1000000", ErrorMessage = "Цена не может быть меньше 200 ₽.")]
        public decimal Price { get; set; } = 200m;

        public bool IsActive { get; set; } = true;
    }
}
