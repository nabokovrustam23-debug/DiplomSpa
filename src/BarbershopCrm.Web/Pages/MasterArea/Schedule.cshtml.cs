using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Web.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarbershopCrm.Web.Pages.MasterArea;

[AuthorizePage(RoleCode.Master)]
public class ScheduleModel : AppPageModel
{
    private readonly AppDbContext _db;

    public ScheduleModel(AppDbContext db, ICurrentUserAccessor currentUser) : base(currentUser)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public string? WeekStart { get; set; }

    public DateOnly StartDate { get; private set; }
    public DateOnly[] WeekDays { get; private set; } = Array.Empty<DateOnly>();

    public BarbershopCrm.Domain.Entities.Master? Self { get; private set; }
    public Dictionary<DateOnly, List<WorkSchedule>> ByDay { get; private set; } = new();
    public Dictionary<DateOnly, List<Booking>> BookingsByDay { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        StartDate = ParseStart(WeekStart);
        WeekDays = Enumerable.Range(0, 7).Select(i => StartDate.AddDays(i)).ToArray();

        var userId = Current?.UserId;
        if (userId is null) return RedirectToPage("/Account/Login");

        Self = await _db.Masters
            .Include(m => m.Persona)
            .Include(m => m.Branch)
            .FirstOrDefaultAsync(m => m.Persona.User != null && m.Persona.User.UserId == userId, ct);
        if (Self is null)
        {
            return Page();
        }

        var endDate = StartDate.AddDays(7);
        var schedules = await _db.WorkSchedules
            .Where(w => w.MasterId == Self.MasterId && w.WorkDate >= StartDate && w.WorkDate < endDate)
            .OrderBy(w => w.StartTime)
            .AsNoTracking()
            .ToListAsync(ct);
        ByDay = schedules.GroupBy(w => w.WorkDate).ToDictionary(g => g.Key, g => g.ToList());

        var dayStart = new DateTime(StartDate.Year, StartDate.Month, StartDate.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var dayEnd = dayStart.AddDays(7);
        var bookings = await _db.Bookings
            .Include(b => b.Client).ThenInclude(c => c.Persona)
            .Include(b => b.Service)
            .Where(b => b.MasterId == Self.MasterId
                && b.StartDateTime >= dayStart && b.StartDateTime < dayEnd
                && (b.Status == BookingStatus.Created || b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Completed))
            .OrderBy(b => b.StartDateTime)
            .AsNoTracking()
            .ToListAsync(ct);
        BookingsByDay = bookings.GroupBy(b => DateOnly.FromDateTime(b.StartDateTime)).ToDictionary(g => g.Key, g => g.ToList());

        return Page();
    }

    public static string Label(string type) => BarbershopCrm.Web.Pages.Admin.Schedule.IndexModel.Label(type);

    private static DateOnly ParseStart(string? raw)
    {
        if (DateOnly.TryParse(raw, out var d))
            return StartOfWeek(d);
        return StartOfWeek(DateOnly.FromDateTime(DateTime.Today));
    }

    private static DateOnly StartOfWeek(DateOnly d)
    {
        var dow = (int)d.DayOfWeek;
        var diff = (dow == 0 ? 7 : dow) - 1;
        return d.AddDays(-diff);
    }
}
