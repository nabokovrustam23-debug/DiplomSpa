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
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int DurationMinutes { get; set; } = 30;
        public decimal Price { get; set; }
    }
}
