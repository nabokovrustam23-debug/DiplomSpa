using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Bookings;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Web.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace BarbershopCrm.Web.Pages.MasterArea;

[AuthorizePage(RoleCode.Master)]
public class BookingsModel : AppPageModel
{
    private readonly AppDbContext _db;
    private readonly IBookingService _service;

    public BookingsModel(ICurrentUserAccessor cu, AppDbContext db, IBookingService service) : base(cu)
    {
        _db = db;
        _service = service;
    }

    [BindProperty(SupportsGet = true)] public string? Date { get; set; }

    public DateOnly DateValue { get; private set; }
    public Master? Self { get; private set; }
    public List<Booking> Bookings { get; private set; } = new();
    [BindProperty] public CompleteInput Complete { get; set; } = new();

    public sealed class CompleteInput
    {
        [Required] public int BookingId { get; set; }
        [Required, Range(0, 1_000_000)] public decimal TotalAmount { get; set; }
        [StringLength(500)] public string? MasterNotes { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Current is null) return Forbid();
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostConfirmAsync(int id, CancellationToken ct)
    {
        if (Current is null) return Forbid();
        var r = await _service.ConfirmAsync(id, Current.UserId, Current.RoleCode, ct);
        TempData[r.Success ? "Success" : "Error"] = r.Success ? "Запись подтверждена." : (r.Message ?? "Ошибка");
        return RedirectToPage(new { Date });
    }

    public async Task<IActionResult> OnPostNoShowAsync(int id, CancellationToken ct)
    {
        if (Current is null) return Forbid();
        var r = await _service.NoShowAsync(id, Current.UserId, Current.RoleCode, ct);
        TempData[r.Success ? "Success" : "Error"] = r.Success ? "Отмечено «не пришёл»." : (r.Message ?? "Ошибка");
        return RedirectToPage(new { Date });
    }

    public async Task<IActionResult> OnPostCompleteAsync(CancellationToken ct)
    {
        if (Current is null) return Forbid();
        if (!ModelState.IsValid)
        {
            await LoadAsync(ct);
            return Page();
        }
        var r = await _service.CompleteAsync(
            new CompleteBookingCommand(Complete.BookingId, Complete.TotalAmount, Complete.MasterNotes),
            Current.UserId, Current.RoleCode, ct);
        TempData[r.Success ? "Success" : "Error"] = r.Success ? "Визит зафиксирован." : (r.Message ?? "Ошибка");
        return RedirectToPage(new { Date });
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        DateValue = !string.IsNullOrWhiteSpace(Date) && DateOnly.TryParse(Date, out var d)
            ? d : DateOnly.FromDateTime(DateTime.Today);

        Self = await _db.Masters.AsNoTracking()
            .Include(m => m.Persona).ThenInclude(p => p.User)
            .FirstOrDefaultAsync(m => m.Persona.User != null && m.Persona.User.UserId == Current!.UserId, ct);

        if (Self is null) return;

        var dayStart = new DateTime(DateValue.Year, DateValue.Month, DateValue.Day);
        var dayEnd = dayStart.AddDays(1);
        Bookings = await _db.Bookings.AsNoTracking()
            .Where(b => b.MasterId == Self.MasterId && b.StartDateTime >= dayStart && b.StartDateTime < dayEnd)
            .Include(b => b.Service)
            .Include(b => b.Client).ThenInclude(c => c.Persona)
            .Include(b => b.Branch)
            .OrderBy(b => b.StartDateTime)
            .ToListAsync(ct);
    }
}
