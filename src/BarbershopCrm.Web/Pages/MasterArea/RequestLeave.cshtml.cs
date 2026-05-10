using System.ComponentModel.DataAnnotations;
using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Web.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarbershopCrm.Web.Pages.MasterArea;

[AuthorizePage(RoleCode.Master)]
public class RequestLeaveModel : AppPageModel
{
    private readonly AppDbContext _db;

    public RequestLeaveModel(AppDbContext db, ICurrentUserAccessor currentUser) : base(currentUser)
    {
        _db = db;
    }

    public BarbershopCrm.Domain.Entities.Master? Self { get; private set; }

    [BindProperty]
    public LeaveInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await LoadSelfAsync(ct);
        Input.From = DateOnly.FromDateTime(DateTime.Today).AddDays(1).ToString("yyyy-MM-dd");
        Input.To = DateOnly.FromDateTime(DateTime.Today).AddDays(3).ToString("yyyy-MM-dd");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        await LoadSelfAsync(ct);
        if (Self is null)
        {
            ModelState.AddModelError(string.Empty, "Профиль мастера не найден.");
            return Page();
        }

        if (!ModelState.IsValid) return Page();

        if (!DateOnly.TryParse(Input.From, out var from) || !DateOnly.TryParse(Input.To, out var to))
        {
            ModelState.AddModelError(string.Empty, "Неверный формат даты.");
            return Page();
        }
        if (to < from)
        {
            ModelState.AddModelError(string.Empty, "Дата окончания не может быть раньше начала.");
            return Page();
        }
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (from < today)
        {
            ModelState.AddModelError(string.Empty, "Нельзя оформить отпуск задним числом.");
            return Page();
        }
        if ((to.DayNumber - from.DayNumber) > 60)
        {
            ModelState.AddModelError(string.Empty, "Максимальная длительность заявки — 60 дней.");
            return Page();
        }

        var type = Input.LeaveType == ScheduleType.SickLeave ? ScheduleType.SickLeave : ScheduleType.Vacation;

        int created = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var existing = _db.WorkSchedules.Where(w => w.MasterId == Self.MasterId && w.WorkDate == d);
            _db.WorkSchedules.RemoveRange(existing);

            _db.WorkSchedules.Add(new WorkSchedule
            {
                MasterId = Self.MasterId,
                BranchId = Self.BranchId,
                WorkDate = d,
                StartTime = new TimeOnly(0, 0),
                EndTime = new TimeOnly(23, 59),
                ScheduleType = type,
            });
            created++;
        }

        await _db.SaveChangesAsync(ct);
        TempData["Success"] = $"Заявка зарегистрирована: {(type == ScheduleType.SickLeave ? "больничный" : "отпуск")} на {created} дн.";
        return RedirectToPage("Schedule", new { weekStart = Input.From });
    }

    private async Task LoadSelfAsync(CancellationToken ct)
    {
        var userId = Current?.UserId;
        if (userId is null) return;
        Self = await _db.Masters
            .Include(m => m.Persona)
            .Include(m => m.Branch)
            .FirstOrDefaultAsync(m => m.Persona.User != null && m.Persona.User.UserId == userId, ct);
    }

    public class LeaveInput
    {
        [Required] public string LeaveType { get; set; } = ScheduleType.Vacation;
        [Required] public string From { get; set; } = string.Empty;
        [Required] public string To { get; set; } = string.Empty;
    }
}
