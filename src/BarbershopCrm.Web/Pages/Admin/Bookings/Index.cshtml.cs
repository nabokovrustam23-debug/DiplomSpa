using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Bookings;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Web.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace BarbershopCrm.Web.Pages.Admin.Bookings;

[AuthorizePage(RoleCode.Admin, RoleCode.Owner)]
public class IndexModel : AppPageModel
{
    private readonly AppDbContext _db;
    private readonly IBookingService _service;

    public IndexModel(ICurrentUserAccessor cu, AppDbContext db, IBookingService service) : base(cu)
    {
        _db = db;
        _service = service;
    }

    [BindProperty(SupportsGet = true)] public int? BranchId { get; set; }
    [BindProperty(SupportsGet = true)] public string? From { get; set; }
    [BindProperty(SupportsGet = true)] public string? To { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }

    public DateOnly FromValue { get; private set; }
    public DateOnly ToValue { get; private set; }
    public List<Booking> Items { get; private set; } = new();
    public List<SelectListItem> BranchOptions { get; private set; } = new();
    public List<string> StatusOptions { get; } = BookingStatus.All.ToList();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Current is null) return Forbid();
        FromValue = !string.IsNullOrWhiteSpace(From) && DateOnly.TryParse(From, out var f)
            ? f : DateOnly.FromDateTime(DateTime.Today);
        ToValue = !string.IsNullOrWhiteSpace(To) && DateOnly.TryParse(To, out var t)
            ? t : FromValue.AddDays(7);
        if (ToValue < FromValue) ToValue = FromValue;

        var branchesQ = _db.Branches.AsNoTracking().Where(b => b.IsActive);
        if (Current.RoleCode == RoleCode.Admin && Current.BranchId.HasValue)
        {
            branchesQ = branchesQ.Where(b => b.BranchId == Current.BranchId.Value);
            BranchId = Current.BranchId;
        }
        BranchOptions = await branchesQ.OrderBy(b => b.Name)
            .Select(b => new SelectListItem(b.Name, b.BranchId.ToString()))
            .ToListAsync(ct);

        var fromDt = new DateTime(FromValue.Year, FromValue.Month, FromValue.Day);
        var toDt = new DateTime(ToValue.Year, ToValue.Month, ToValue.Day).AddDays(1);
        var q = _db.Bookings.AsNoTracking()
            .Include(b => b.Branch)
            .Include(b => b.Service)
            .Include(b => b.Client).ThenInclude(c => c.Persona)
            .Include(b => b.Master).ThenInclude(m => m.Persona)
            .Where(b => b.StartDateTime >= fromDt && b.StartDateTime < toDt);

        if (BranchId.HasValue) q = q.Where(b => b.BranchId == BranchId.Value);
        if (!string.IsNullOrWhiteSpace(Status) && BookingStatus.All.Contains(Status))
            q = q.Where(b => b.Status == Status);

        Items = await q.OrderBy(b => b.StartDateTime).ToListAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync(int id, string? reason, CancellationToken ct)
    {
        if (Current is null) return Forbid();
        // Owner can cancel any; Admin only own branch.
        if (Current.RoleCode == RoleCode.Admin)
        {
            var ownsBranch = await _db.Bookings.AsNoTracking()
                .AnyAsync(b => b.BookingId == id && b.BranchId == Current.BranchId, ct);
            if (!ownsBranch) return NotFound();
        }
        var r = await _service.CancelAsync(id, Current.UserId, Current.RoleCode, reason, ct);
        TempData[r.Success ? "Success" : "Error"] = r.Success ? "Запись отменена." : (r.Message ?? "Ошибка отмены.");
        return RedirectToPage(new { BranchId, From, To, Status });
    }
}
