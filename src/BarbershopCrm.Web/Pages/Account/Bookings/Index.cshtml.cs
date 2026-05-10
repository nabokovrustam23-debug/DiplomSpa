using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Bookings;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Web.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BarbershopCrm.Web.Pages.Account.Bookings;

[AuthorizePage(RoleCode.Client)]
public class IndexModel : AppPageModel
{
    private readonly AppDbContext _db;
    private readonly IBookingService _service;
    private readonly BookingOptions _opts;

    public IndexModel(ICurrentUserAccessor cu, AppDbContext db, IBookingService service, Microsoft.Extensions.Options.IOptions<BookingOptions> opts)
        : base(cu)
    {
        _db = db;
        _service = service;
        _opts = opts.Value;
    }

    public List<Booking> Active { get; private set; } = new();
    public List<Booking> Past { get; private set; } = new();
    public int CancelCutoffHours => _opts.CancelCutoffHours;

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Current is null) return Forbid();

        var clientId = await _db.Clients.AsNoTracking()
            .Where(c => c.PersonaId == Current.PersonaId)
            .Select(c => (int?)c.ClientId)
            .FirstOrDefaultAsync(ct);
        if (clientId is null) return Page();

        var all = await _db.Bookings.AsNoTracking()
            .Where(b => b.ClientId == clientId)
            .Include(b => b.Branch)
            .Include(b => b.Service)
            .Include(b => b.Master).ThenInclude(m => m.Persona)
            .OrderByDescending(b => b.StartDateTime)
            .ToListAsync(ct);

        var nowUtc = DateTime.UtcNow;
        Active = all
            .Where(b => (b.Status == BookingStatus.Created || b.Status == BookingStatus.Confirmed) && b.StartDateTime > nowUtc)
            .OrderBy(b => b.StartDateTime)
            .ToList();
        Past = all.Except(Active).OrderByDescending(b => b.StartDateTime).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync(int id, string? reason, CancellationToken ct)
    {
        if (Current is null) return Forbid();
        var result = await _service.CancelAsync(id, Current.UserId, Current.RoleCode, reason, ct);
        TempData[result.Success ? "Success" : "Error"]
            = result.Success ? "Запись отменена." : (result.Message ?? "Ошибка отмены.");
        return RedirectToPage();
    }
}
