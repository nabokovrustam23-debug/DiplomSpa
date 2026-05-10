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

    public IList<Branch> Branches { get; private set; } = Array.Empty<Branch>();
    public IList<Master> Masters { get; private set; } = Array.Empty<Master>();
    public Dictionary<(int MasterId, DateOnly Date), List<WorkSchedule>> Grid { get; private set; } = new();

    [BindProperty(SupportsGet = true)]
    public int? BranchId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? WeekStart { get; set; } // YYYY-MM-DD

    public DateOnly StartDate { get; private set; }
    public DateOnly[] WeekDays { get; private set; } = Array.Empty<DateOnly>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        StartDate = ParseStart(WeekStart);
        WeekDays = Enumerable.Range(0, 7).Select(i => StartDate.AddDays(i)).ToArray();

        Branches = await _db.Branches
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .AsNoTracking()
            .ToListAsync(ct);

        // Admin sees only own branch.
        if (Current?.RoleCode == RoleCode.Admin && Current.BranchId.HasValue)
        {
            BranchId = Current.BranchId.Value;
        }
        else if (BranchId is null && Branches.Count > 0)
        {
            BranchId = Branches[0].BranchId;
        }

        if (BranchId is null) return;

        Masters = await _db.Masters
            .Include(m => m.Persona)
            .Where(m => m.IsActive && m.BranchId == BranchId.Value)
            .OrderBy(m => m.Persona.LastName)
            .AsNoTracking()
            .ToListAsync(ct);

        var ids = Masters.Select(m => m.MasterId).ToList();
        var endDate = StartDate.AddDays(7);
        var schedules = await _db.WorkSchedules
            .Where(w => ids.Contains(w.MasterId) && w.WorkDate >= StartDate && w.WorkDate < endDate)
            .OrderBy(w => w.StartTime)
            .AsNoTracking()
            .ToListAsync(ct);

        Grid = schedules
            .GroupBy(s => (s.MasterId, s.WorkDate))
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    public string FormatRanges((int MasterId, DateOnly Date) key)
    {
        if (!Grid.TryGetValue(key, out var list) || list.Count == 0) return "—";
        return string.Join(", ", list.Select(s => $"{Label(s.ScheduleType)} {s.StartTime:HH\\:mm}–{s.EndTime:HH\\:mm}"));
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

    private static DateOnly ParseStart(string? raw)
    {
        if (DateOnly.TryParse(raw, out var d))
            return StartOfWeek(d);
        return StartOfWeek(DateOnly.FromDateTime(DateTime.Today));
    }

    private static DateOnly StartOfWeek(DateOnly d)
    {
        // Monday-start week.
        var dow = (int)d.DayOfWeek;
        var diff = (dow == 0 ? 7 : dow) - 1;
        return d.AddDays(-diff);
    }
}
