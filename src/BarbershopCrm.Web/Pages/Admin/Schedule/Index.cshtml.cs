using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Web.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarbershopCrm.Web.Pages.Admin.Schedule;

[AuthorizePage(RoleCode.Admin, RoleCode.Owner)]
public class IndexModel : AppPageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db, ICurrentUserAccessor currentUser) : base(currentUser)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)] public int? BranchId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Date { get; set; }

    public IList<Branch> Branches { get; private set; } = Array.Empty<Branch>();
    public Branch? CurrentBranch { get; private set; }
    public DateOnly DateValue { get; private set; }

    public List<Master> Masters { get; private set; } = new();
    public TimeOnly TimelineStart { get; private set; }
    public TimeOnly TimelineEnd { get; private set; }
    public List<TimelineHour> TimelineHours { get; private set; } = new();
    public Dictionary<int, List<TimelineEntry>> EntriesByMaster { get; private set; } = new();

    public sealed record TimelineHour(TimeOnly Time, int RowIndex);

    public sealed record TimelineEntry(
        TimelineEntryKind Kind,
        TimeOnly StartTime,
        TimeOnly EndTime,
        int RowStart,
        int RowSpan,
        Booking? Booking,
        string? Label);

    public enum TimelineEntryKind { Booking, Lunch, DayOff, Vacation, SickLeave }

    private const int MinutesPerRow = 15;
    public int TotalRows => Math.Max(1, MinutesBetween(TimelineStart, TimelineEnd) / MinutesPerRow);

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        DateValue = !string.IsNullOrWhiteSpace(Date) && DateOnly.TryParse(Date, out var d)
            ? d : DateOnly.FromDateTime(DateTime.Today);

        Branches = await _db.Branches.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .ToListAsync(ct);

        if (Current?.RoleCode == RoleCode.Admin && Current.BranchId.HasValue)
        {
            BranchId = Current.BranchId.Value;
        }
        else if (BranchId is null && Branches.Count > 0)
        {
            BranchId = Branches[0].BranchId;
        }

        if (BranchId is null) return Page();

        CurrentBranch = Branches.FirstOrDefault(b => b.BranchId == BranchId.Value);

        Masters = await _db.Masters.AsNoTracking()
            .Include(m => m.Persona)
            .Where(m => m.IsActive && m.BranchId == BranchId.Value)
            .OrderBy(m => m.Persona.LastName)
            .ToListAsync(ct);

        if (Masters.Count == 0) return Page();

        var masterIds = Masters.Select(m => m.MasterId).ToList();
        var dayStart = new DateTime(DateValue.Year, DateValue.Month, DateValue.Day);
        var dayEnd = dayStart.AddDays(1);

        var bookings = await _db.Bookings.AsNoTracking()
            .Where(b => masterIds.Contains(b.MasterId)
                        && b.StartDateTime >= dayStart && b.StartDateTime < dayEnd
                        && b.Status != BookingStatus.Cancelled)
            .Include(b => b.Service)
            .Include(b => b.Client).ThenInclude(c => c.Persona)
            .OrderBy(b => b.StartDateTime)
            .ToListAsync(ct);

        var schedule = await _db.WorkSchedules.AsNoTracking()
            .Where(w => masterIds.Contains(w.MasterId) && w.WorkDate == DateValue)
            .OrderBy(w => w.StartTime)
            .ToListAsync(ct);

        BuildTimeline(bookings, schedule);
        return Page();
    }

    private void BuildTimeline(List<Booking> bookings, List<WorkSchedule> schedule)
    {
        var workRanges = schedule.Where(w => string.Equals(w.ScheduleType, ScheduleType.Work, StringComparison.Ordinal)).ToList();
        if (workRanges.Count > 0)
        {
            TimelineStart = workRanges.Min(w => w.StartTime);
            TimelineEnd = workRanges.Max(w => w.EndTime);
        }
        else
        {
            TimelineStart = new TimeOnly(10, 0);
            TimelineEnd = new TimeOnly(20, 0);
        }

        foreach (var b in bookings)
        {
            var s = TimeOnly.FromDateTime(b.StartDateTime);
            var e = TimeOnly.FromDateTime(b.StartDateTime.AddMinutes(b.DurationMinutes));
            if (s < TimelineStart) TimelineStart = s;
            if (e > TimelineEnd) TimelineEnd = e;
        }

        TimelineStart = new TimeOnly(TimelineStart.Hour, 0);
        if (TimelineEnd.Minute > 0) TimelineEnd = new TimeOnly(Math.Min(23, TimelineEnd.Hour + 1), 0);

        TimelineHours = new List<TimelineHour>();
        for (var h = TimelineStart; h < TimelineEnd; h = h.AddHours(1))
        {
            var rowIndex = MinutesBetween(TimelineStart, h) / MinutesPerRow;
            TimelineHours.Add(new TimelineHour(h, rowIndex + 1));
        }

        EntriesByMaster = Masters.ToDictionary(m => m.MasterId, _ => new List<TimelineEntry>());

        foreach (var b in bookings)
        {
            if (!EntriesByMaster.ContainsKey(b.MasterId)) continue;
            var s = TimeOnly.FromDateTime(b.StartDateTime);
            var e = TimeOnly.FromDateTime(b.StartDateTime.AddMinutes(b.DurationMinutes));
            EntriesByMaster[b.MasterId].Add(new TimelineEntry(
                TimelineEntryKind.Booking,
                s, e,
                RowFor(s), Math.Max(1, MinutesBetween(s, e) / MinutesPerRow),
                b, null));
        }

        foreach (var w in schedule)
        {
            if (!EntriesByMaster.ContainsKey(w.MasterId)) continue;
            TimelineEntryKind? kind = w.ScheduleType switch
            {
                "Lunch"     => TimelineEntryKind.Lunch,
                "DayOff"    => TimelineEntryKind.DayOff,
                "Vacation"  => TimelineEntryKind.Vacation,
                "SickLeave" => TimelineEntryKind.SickLeave,
                _ => null,
            };
            if (kind is null) continue;

            var label = kind switch
            {
                TimelineEntryKind.Lunch     => "Перерыв",
                TimelineEntryKind.DayOff    => "Выходной",
                TimelineEntryKind.Vacation  => "Отпуск",
                TimelineEntryKind.SickLeave => "Больничный",
                _ => string.Empty,
            };
            EntriesByMaster[w.MasterId].Add(new TimelineEntry(
                kind.Value,
                w.StartTime, w.EndTime,
                RowFor(w.StartTime),
                Math.Max(1, MinutesBetween(w.StartTime, w.EndTime) / MinutesPerRow),
                null, label));
        }

        foreach (var k in EntriesByMaster.Keys.ToList())
            EntriesByMaster[k] = EntriesByMaster[k].OrderBy(e => e.StartTime).ToList();
    }

    private int RowFor(TimeOnly t) => 1 + Math.Max(0, MinutesBetween(TimelineStart, t) / MinutesPerRow);

    private static int MinutesBetween(TimeOnly a, TimeOnly b) => (int)(b - a).TotalMinutes;

    public static string FormatRussianDate(DateOnly d)
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
        var today = DateOnly.FromDateTime(DateTime.Today);
        var prefix = d == today ? "Сегодня" :
                     d == today.AddDays(1) ? "Завтра" :
                     d == today.AddDays(-1) ? "Вчера" :
                     culture.DateTimeFormat.GetDayName(d.DayOfWeek);
        prefix = char.ToUpperInvariant(prefix[0]) + prefix[1..];
        return $"{prefix}, {d.ToString("d MMMM yyyy", culture)}";
    }

    public static string FormatDuration(int totalMinutes)
    {
        var h = totalMinutes / 60;
        var m = totalMinutes % 60;
        if (h == 0) return $"{m} мин";
        if (m == 0) return $"{h} ч";
        return $"{h} ч {m} мин";
    }

    public static string Label(string type) => type switch
    {
        ScheduleType.Work => "Смена",
        ScheduleType.Lunch => "Обед",
        ScheduleType.DayOff => "Выходной",
        ScheduleType.Vacation => "Отпуск",
        ScheduleType.SickLeave => "Больничный",
        _ => type,
    };
}
