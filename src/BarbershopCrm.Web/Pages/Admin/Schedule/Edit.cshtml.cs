using System.ComponentModel.DataAnnotations;
using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Web.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarbershopCrm.Web.Pages.Admin.Schedule;

[AuthorizePage(RoleCode.Admin, RoleCode.Owner)]
public class EditModel : AppPageModel
{
    private readonly AppDbContext _db;

    public EditModel(AppDbContext db, ICurrentUserAccessor currentUser) : base(currentUser)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public int MasterId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Date { get; set; } = string.Empty;

    public Master? Master { get; private set; }
    public DateOnly TargetDate { get; private set; }
    public IList<WorkSchedule> Existing { get; private set; } = Array.Empty<WorkSchedule>();

    [BindProperty]
    public AddIntervalInput Add { get; set; } = new();

    [BindProperty]
    public BulkShiftInput Bulk { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (!await LoadAsync(ct)) return NotFound();
        Add.StartTime = "10:00";
        Add.EndTime = "20:00";
        Bulk.StartTime = "10:00";
        Bulk.EndTime = "20:00";
        Bulk.LunchStart = "14:00";
        Bulk.LunchEnd = "15:00";
        Bulk.From = TargetDate.ToString("yyyy-MM-dd");
        Bulk.To = TargetDate.AddDays(6).ToString("yyyy-MM-dd");
        return Page();
    }

    public async Task<IActionResult> OnPostAddAsync(CancellationToken ct)
    {
        if (!await LoadAsync(ct)) return NotFound();

        // Each handler validates only its own bound subtree.
        ClearOtherStateExcept("Add");
        if (!ModelState.IsValid) return Page();

        if (!TimeOnly.TryParse(Add.StartTime, out var start) || !TimeOnly.TryParse(Add.EndTime, out var end))
        {
            ModelState.AddModelError(string.Empty, "Время должно быть в формате ЧЧ:ММ.");
            return Page();
        }

        if (end <= start)
        {
            ModelState.AddModelError(string.Empty, "Время окончания должно быть позже начала.");
            return Page();
        }

        if (!ScheduleType.All.Contains(Add.ScheduleType))
        {
            ModelState.AddModelError(string.Empty, "Невалидный тип интервала.");
            return Page();
        }

        // Conflict prevention: an interval with the same start time already exists.
        var dup = Existing.Any(e => e.StartTime == start);
        if (dup)
        {
            ModelState.AddModelError(string.Empty, $"Интервал, начинающийся в {start:HH\\:mm}, уже есть.");
            return Page();
        }

        _db.WorkSchedules.Add(new WorkSchedule
        {
            MasterId = MasterId,
            BranchId = Master!.BranchId,
            WorkDate = TargetDate,
            StartTime = start,
            EndTime = end,
            ScheduleType = Add.ScheduleType,
        });
        await _db.SaveChangesAsync(ct);

        TempData["Success"] = $"{IndexModel.Label(Add.ScheduleType)} {start:HH\\:mm}–{end:HH\\:mm} добавлен.";
        return RedirectToPage(new { masterId = MasterId, date = Date });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken ct)
    {
        if (!await LoadAsync(ct)) return NotFound();

        var item = await _db.WorkSchedules
            .FirstOrDefaultAsync(w => w.WorkScheduleId == id && w.MasterId == MasterId && w.WorkDate == TargetDate, ct);
        if (item is null) return NotFound();

        _db.WorkSchedules.Remove(item);
        await _db.SaveChangesAsync(ct);

        TempData["Success"] = "Интервал удалён.";
        return RedirectToPage(new { masterId = MasterId, date = Date });
    }

    public async Task<IActionResult> OnPostBulkAsync(CancellationToken ct)
    {
        if (!await LoadAsync(ct)) return NotFound();

        ClearOtherStateExcept("Bulk");
        if (!ModelState.IsValid) return Page();

        if (!DateOnly.TryParse(Bulk.From, out var from)
            || !DateOnly.TryParse(Bulk.To, out var to)
            || !TimeOnly.TryParse(Bulk.StartTime, out var start)
            || !TimeOnly.TryParse(Bulk.EndTime, out var end))
        {
            ModelState.AddModelError(string.Empty, "Проверьте формат даты и времени.");
            return Page();
        }
        if (to < from || end <= start)
        {
            ModelState.AddModelError(string.Empty, "Диапазоны указаны неверно.");
            return Page();
        }
        if ((to.DayNumber - from.DayNumber) > 92)
        {
            ModelState.AddModelError(string.Empty, "Можно генерировать не более 92 дней за раз.");
            return Page();
        }

        TimeOnly? lunchStart = null, lunchEnd = null;
        if (!string.IsNullOrWhiteSpace(Bulk.LunchStart) && !string.IsNullOrWhiteSpace(Bulk.LunchEnd))
        {
            if (!TimeOnly.TryParse(Bulk.LunchStart, out var ls) || !TimeOnly.TryParse(Bulk.LunchEnd, out var le)
                || le <= ls || ls < start || le > end)
            {
                ModelState.AddModelError(string.Empty, "Обед должен лежать внутри смены.");
                return Page();
            }
            lunchStart = ls; lunchEnd = le;
        }

        // Apply only on selected weekdays (1=Mon … 7=Sun). Default = all.
        var allowed = (Bulk.Weekdays is { Length: > 0 } ? Bulk.Weekdays : new[] { 1, 2, 3, 4, 5, 6, 7 }).ToHashSet();

        int created = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var dow = (int)d.DayOfWeek;
            var isoDow = dow == 0 ? 7 : dow;
            if (!allowed.Contains(isoDow)) continue;

            var dayHasAny = await _db.WorkSchedules
                .AnyAsync(w => w.MasterId == MasterId && w.WorkDate == d, ct);

            if (dayHasAny && !Bulk.Overwrite) continue;

            if (dayHasAny && Bulk.Overwrite)
            {
                var existing = _db.WorkSchedules.Where(w => w.MasterId == MasterId && w.WorkDate == d);
                _db.WorkSchedules.RemoveRange(existing);
            }

            _db.WorkSchedules.Add(new WorkSchedule
            {
                MasterId = MasterId,
                BranchId = Master!.BranchId,
                WorkDate = d,
                StartTime = start,
                EndTime = end,
                ScheduleType = ScheduleType.Work,
            });
            created++;

            if (lunchStart.HasValue && lunchEnd.HasValue)
            {
                _db.WorkSchedules.Add(new WorkSchedule
                {
                    MasterId = MasterId,
                    BranchId = Master.BranchId,
                    WorkDate = d,
                    StartTime = lunchStart.Value,
                    EndTime = lunchEnd.Value,
                    ScheduleType = ScheduleType.Lunch,
                });
            }
        }

        await _db.SaveChangesAsync(ct);
        TempData["Success"] = $"Расписание применено на {created} день(дней).";
        return RedirectToPage("Index", new { branchId = Master!.BranchId, weekStart = Bulk.From });
    }

    private void ClearOtherStateExcept(string keepPrefix)
    {
        foreach (var key in ModelState.Keys.ToList())
        {
            if (!key.StartsWith(keepPrefix + ".", StringComparison.Ordinal) && key != string.Empty)
                ModelState.Remove(key);
        }
    }

    private async Task<bool> LoadAsync(CancellationToken ct)
    {
        if (!DateOnly.TryParse(Date, out var d)) return false;
        TargetDate = d;

        Master = await _db.Masters
            .Include(m => m.Persona)
            .Include(m => m.Branch)
            .FirstOrDefaultAsync(m => m.MasterId == MasterId, ct);
        if (Master is null) return false;

        // Admin scope check.
        if (Current?.RoleCode == RoleCode.Admin && Current.BranchId != Master.BranchId) return false;

        Existing = await _db.WorkSchedules
            .Where(w => w.MasterId == MasterId && w.WorkDate == TargetDate)
            .OrderBy(w => w.StartTime)
            .AsNoTracking()
            .ToListAsync(ct);
        return true;
    }

    public class AddIntervalInput
    {
        [Required] public string ScheduleType { get; set; } = Domain.Enums.ScheduleType.Work;
        [Required, RegularExpression(@"^\d{2}:\d{2}$", ErrorMessage = "ЧЧ:ММ")]
        public string StartTime { get; set; } = "10:00";
        [Required, RegularExpression(@"^\d{2}:\d{2}$", ErrorMessage = "ЧЧ:ММ")]
        public string EndTime { get; set; } = "20:00";
    }

    public class BulkShiftInput
    {
        [Required] public string From { get; set; } = string.Empty;
        [Required] public string To { get; set; } = string.Empty;
        [Required, RegularExpression(@"^\d{2}:\d{2}$")] public string StartTime { get; set; } = "10:00";
        [Required, RegularExpression(@"^\d{2}:\d{2}$")] public string EndTime { get; set; } = "20:00";
        public string? LunchStart { get; set; }
        public string? LunchEnd { get; set; }
        public int[] Weekdays { get; set; } = Array.Empty<int>();
        public bool Overwrite { get; set; }
    }
}
