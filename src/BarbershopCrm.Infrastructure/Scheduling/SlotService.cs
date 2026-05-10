using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Domain.Scheduling;
using BarbershopCrm.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BarbershopCrm.Infrastructure.Scheduling;

public sealed record SlotDto(int MasterId, string MasterName, DateTime StartDateTime);

public interface ISlotService
{
    Task<IReadOnlyList<SlotDto>> GetFreeSlotsAsync(
        int branchId,
        int serviceId,
        DateOnly date,
        int? masterId,
        CancellationToken ct);
}

public sealed class SlotService : ISlotService
{
    private const int SlotStepMinutes = 15;

    private readonly AppDbContext _db;

    public SlotService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<SlotDto>> GetFreeSlotsAsync(
        int branchId, int serviceId, DateOnly date, int? masterId, CancellationToken ct)
    {
        var branch = await _db.Branches.AsNoTracking()
            .Where(b => b.IsActive && b.BranchId == branchId)
            .FirstOrDefaultAsync(ct);
        if (branch is null) return Array.Empty<SlotDto>();

        var service = await _db.Services.AsNoTracking()
            .Where(s => s.IsActive && s.ServiceId == serviceId)
            .FirstOrDefaultAsync(ct);
        if (service is null) return Array.Empty<SlotDto>();

        // Masters of this branch who can do the service.
        var mastersQuery = _db.Masters
            .Include(m => m.Persona)
            .Include(m => m.MasterServices)
            .Where(m => m.IsActive && m.BranchId == branchId
                && m.MasterServices.Any(ms => ms.ServiceId == serviceId));
        if (masterId.HasValue)
            mastersQuery = mastersQuery.Where(m => m.MasterId == masterId.Value);

        var masters = await mastersQuery.AsNoTracking().ToListAsync(ct);
        if (masters.Count == 0) return Array.Empty<SlotDto>();

        var ids = masters.Select(m => m.MasterId).ToList();

        var schedules = await _db.WorkSchedules.AsNoTracking()
            .Where(w => w.WorkDate == date && ids.Contains(w.MasterId))
            .ToListAsync(ct);

        var dayStart = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Unspecified);
        var dayEnd = dayStart.AddDays(1);
        var bookings = await _db.Bookings.AsNoTracking()
            .Where(bk => ids.Contains(bk.MasterId)
                && (bk.Status == BookingStatus.Created || bk.Status == BookingStatus.Confirmed)
                && bk.StartDateTime >= dayStart && bk.StartDateTime < dayEnd)
            .Select(bk => new { bk.MasterId, bk.StartDateTime, bk.DurationMinutes })
            .ToListAsync(ct);

        var result = new List<SlotDto>();
        foreach (var m in masters)
        {
            var mySchedules = schedules.Where(s => s.MasterId == m.MasterId).ToList();
            var hasUnavailable = mySchedules.Any(s =>
                s.ScheduleType == ScheduleType.DayOff
                || s.ScheduleType == ScheduleType.Vacation
                || s.ScheduleType == ScheduleType.SickLeave);
            var workRanges = mySchedules
                .Where(s => s.ScheduleType == ScheduleType.Work)
                .Select(s => new TimeRange(s.StartTime, s.EndTime))
                .ToList();

            var blocked = mySchedules
                .Where(s => s.ScheduleType == ScheduleType.Lunch)
                .Select(s => new TimeRange(s.StartTime, s.EndTime))
                .ToList();
            blocked.AddRange(bookings
                .Where(b => b.MasterId == m.MasterId)
                .Select(b => new TimeRange(
                    TimeOnly.FromDateTime(b.StartDateTime),
                    TimeOnly.FromDateTime(b.StartDateTime.AddMinutes(b.DurationMinutes)))));

            var input = new MasterScheduleInput
            {
                MasterId = m.MasterId,
                MasterName = m.Persona.FullName,
                WorkRanges = workRanges,
                BlockedRanges = blocked,
                IsDayOff = hasUnavailable,
            };

            foreach (var dt in SlotCalculator.ComputeSlots(
                date,
                service.DurationMinutes,
                SlotStepMinutes,
                branch.OpeningTime,
                branch.ClosingTime,
                input))
            {
                result.Add(new SlotDto(m.MasterId, m.Persona.FullName, dt));
            }
        }

        return result.OrderBy(r => r.StartDateTime).ThenBy(r => r.MasterName).ToList();
    }
}
