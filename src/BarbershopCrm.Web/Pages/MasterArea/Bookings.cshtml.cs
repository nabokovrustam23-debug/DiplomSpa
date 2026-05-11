using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Bookings;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Web.Auth;
using Microsoft.AspNetCore.Mvc;
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
    public Branch? Branch { get; private set; }

    public List<Booking> Bookings { get; private set; } = new();
    public List<WorkSchedule> DaySchedule { get; private set; } = new();

    // Параметры таймлайна
    public TimeOnly TimelineStart { get; private set; }
    public TimeOnly TimelineEnd { get; private set; }
    public List<TimelineHour> TimelineHours { get; private set; } = new();
    public List<TimelineEntry> TimelineEntries { get; private set; } = new();

    [BindProperty] public CompleteInput Complete { get; set; } = new();

    public sealed class CompleteInput
    {
        [Required(ErrorMessage = "Не указана запись.")]
        public int BookingId { get; set; }

        [Required(ErrorMessage = "Укажите сумму.")]
        [Range(0, 1_000_000, ErrorMessage = "Сумма должна быть от 0 до 1 000 000.")]
        public decimal TotalAmount { get; set; }

        [StringLength(500, ErrorMessage = "Комментарий слишком длинный (макс. 500 символов).")]
        public string? MasterNotes { get; set; }
    }

    public sealed record TimelineHour(TimeOnly Time, int RowIndex);

    public sealed record TimelineEntry(
        TimelineEntryKind Kind,
        TimeOnly StartTime,
        TimeOnly EndTime,
        int RowStart,
        int RowSpan,
        Booking? Booking,
        string? Label);

    public enum TimelineEntryKind
    {
        Booking,
        Lunch,
        DayOff,
        Vacation,
        SickLeave,
    }

    private const int MinutesPerRow = 15;

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
            .Include(m => m.Branch)
            .FirstOrDefaultAsync(m => m.Persona.User != null && m.Persona.User.UserId == Current!.UserId, ct);

        if (Self is null) return;
        Branch = Self.Branch;

        var dayStart = new DateTime(DateValue.Year, DateValue.Month, DateValue.Day);
        var dayEnd = dayStart.AddDays(1);

        Bookings = await _db.Bookings.AsNoTracking()
            .Where(b => b.MasterId == Self.MasterId
                        && b.StartDateTime >= dayStart && b.StartDateTime < dayEnd
                        && b.Status != BookingStatus.Cancelled)
            .Include(b => b.Service)
            .Include(b => b.Client).ThenInclude(c => c.Persona)
            .Include(b => b.Branch)
            .OrderBy(b => b.StartDateTime)
            .ToListAsync(ct);

        DaySchedule = await _db.WorkSchedules.AsNoTracking()
            .Where(w => w.MasterId == Self.MasterId && w.WorkDate == DateValue)
            .OrderBy(w => w.StartTime)
            .ToListAsync(ct);

        BuildTimeline();
    }

    private void BuildTimeline()
    {
        // Часовые диапазоны: берём от самого раннего Work-интервала
        // до самого позднего, иначе фолбэк на 10:00–20:00.
        var workRanges = DaySchedule.Where(w => string.Equals(w.ScheduleType, ScheduleType.Work, StringComparison.Ordinal)).ToList();
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

        // Учтём бронирования за пределами рабочих часов (на всякий случай).
        foreach (var b in Bookings)
        {
            var s = TimeOnly.FromDateTime(b.StartDateTime);
            var e = TimeOnly.FromDateTime(b.StartDateTime.AddMinutes(b.DurationMinutes));
            if (s < TimelineStart) TimelineStart = s;
            if (e > TimelineEnd) TimelineEnd = e;
        }

        // Округляем к часам
        TimelineStart = new TimeOnly(TimelineStart.Hour, 0);
        if (TimelineEnd.Minute > 0) TimelineEnd = new TimeOnly(Math.Min(23, TimelineEnd.Hour + 1), 0);

        TimelineHours = new List<TimelineHour>();
        for (var h = TimelineStart; h < TimelineEnd; h = h.AddHours(1))
        {
            var rowIndex = MinutesBetween(TimelineStart, h) / MinutesPerRow;
            TimelineHours.Add(new TimelineHour(h, rowIndex + 1));
        }

        TimelineEntries = new List<TimelineEntry>();

        foreach (var b in Bookings)
        {
            var s = TimeOnly.FromDateTime(b.StartDateTime);
            var e = TimeOnly.FromDateTime(b.StartDateTime.AddMinutes(b.DurationMinutes));
            TimelineEntries.Add(new TimelineEntry(
                TimelineEntryKind.Booking,
                s, e,
                RowFor(s), Math.Max(1, MinutesBetween(s, e) / MinutesPerRow),
                b, null));
        }

        foreach (var w in DaySchedule)
        {
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
            TimelineEntries.Add(new TimelineEntry(
                kind.Value,
                w.StartTime, w.EndTime,
                RowFor(w.StartTime),
                Math.Max(1, MinutesBetween(w.StartTime, w.EndTime) / MinutesPerRow),
                null, label));
        }

        TimelineEntries = TimelineEntries.OrderBy(e => e.StartTime).ToList();
    }

    private int RowFor(TimeOnly t) => 1 + Math.Max(0, MinutesBetween(TimelineStart, t) / MinutesPerRow);

    private static int MinutesBetween(TimeOnly a, TimeOnly b) =>
        (int)(b - a).TotalMinutes;

    public int TotalRows => Math.Max(1, MinutesBetween(TimelineStart, TimelineEnd) / MinutesPerRow);

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
}
