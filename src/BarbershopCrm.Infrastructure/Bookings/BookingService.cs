using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BarbershopCrm.Infrastructure.Bookings;

public sealed class BookingService : IBookingService
{
    private readonly AppDbContext _db;
    private readonly BookingOptions _opts;

    public BookingService(AppDbContext db, IOptions<BookingOptions> opts)
    {
        _db = db;
        _opts = opts.Value;
    }

    public async Task<BookingResult> CreateAsync(CreateBookingCommand cmd, CancellationToken ct)
    {
        if (cmd.StartDateTime <= DateTime.UtcNow.AddSeconds(-30))
            return BookingResult.Fail(BookingErrorCode.SlotInPast, "Нельзя записаться на прошедшее время.");

        var client = await _db.Clients.FirstOrDefaultAsync(c => c.ClientId == cmd.ClientId, ct);
        if (client is null)
            return BookingResult.Fail(BookingErrorCode.NotFound, "Клиент не найден.");

        var branch = await _db.Branches.FirstOrDefaultAsync(b => b.BranchId == cmd.BranchId, ct);
        if (branch is null) return BookingResult.Fail(BookingErrorCode.NotFound, "Филиал не найден.");
        if (!branch.IsActive) return BookingResult.Fail(BookingErrorCode.BranchInactive, "Филиал временно не работает.");

        var service = await _db.Services.FirstOrDefaultAsync(s => s.ServiceId == cmd.ServiceId, ct);
        if (service is null) return BookingResult.Fail(BookingErrorCode.NotFound, "Услуга не найдена.");
        if (!service.IsActive) return BookingResult.Fail(BookingErrorCode.ServiceInactive, "Услуга недоступна.");

        var master = await _db.Masters.FirstOrDefaultAsync(m => m.MasterId == cmd.MasterId, ct);
        if (master is null) return BookingResult.Fail(BookingErrorCode.NotFound, "Мастер не найден.");
        if (!master.IsActive) return BookingResult.Fail(BookingErrorCode.MasterInactive, "Мастер сейчас не работает.");
        if (master.BranchId != cmd.BranchId)
            return BookingResult.Fail(BookingErrorCode.MasterNotInBranch, "Мастер работает в другом филиале.");

        var canDo = await _db.MasterServices.AnyAsync(ms => ms.MasterId == cmd.MasterId && ms.ServiceId == cmd.ServiceId, ct);
        if (!canDo) return BookingResult.Fail(BookingErrorCode.MasterCannotDoService, "Мастер не выполняет эту услугу.");

        var step = Math.Max(_opts.SlotIntervalMinutes, 5);
        if (cmd.StartDateTime.Minute % step != 0 || cmd.StartDateTime.Second != 0)
            return BookingResult.Fail(BookingErrorCode.SlotInvalid, $"Время записи должно быть кратно {step} минутам.");

        var startTime = TimeOnly.FromDateTime(cmd.StartDateTime);
        var endTime = startTime.AddMinutes(service.DurationMinutes);
        if (startTime < branch.OpeningTime || endTime > branch.ClosingTime || endTime <= startTime)
            return BookingResult.Fail(BookingErrorCode.OutsideWorkingHours, "Время вне часов работы филиала.");

        var date = DateOnly.FromDateTime(cmd.StartDateTime);
        var schedules = await _db.WorkSchedules.AsNoTracking()
            .Where(w => w.MasterId == cmd.MasterId && w.WorkDate == date)
            .ToListAsync(ct);

        if (schedules.Any(s => s.ScheduleType is ScheduleType.DayOff or ScheduleType.Vacation or ScheduleType.SickLeave))
            return BookingResult.Fail(BookingErrorCode.OutsideWorkSchedule, "Мастер не работает в этот день.");

        var workCovers = schedules.Any(s => s.ScheduleType == ScheduleType.Work
            && s.StartTime <= startTime && s.EndTime >= endTime);
        if (!workCovers)
            return BookingResult.Fail(BookingErrorCode.OutsideWorkSchedule, "Время вне рабочей смены мастера.");

        var lunchOverlap = schedules.Any(s => s.ScheduleType == ScheduleType.Lunch
            && s.StartTime < endTime && s.EndTime > startTime);
        if (lunchOverlap)
            return BookingResult.Fail(BookingErrorCode.OutsideWorkSchedule, "Слот пересекается с обеденным перерывом.");

        // Overlap with active booking on same master.
        var endDateTime = cmd.StartDateTime.AddMinutes(service.DurationMinutes);
        var overlap = await _db.Bookings.AsNoTracking().AnyAsync(b =>
            b.MasterId == cmd.MasterId
            && (b.Status == BookingStatus.Created || b.Status == BookingStatus.Confirmed)
            && b.StartDateTime < endDateTime
            && b.StartDateTime.AddMinutes(b.DurationMinutes) > cmd.StartDateTime, ct);
        if (overlap)
            return BookingResult.Fail(BookingErrorCode.SlotTaken, "Слот уже занят, выберите другое время.");

        var booking = new Booking
        {
            ClientId = cmd.ClientId,
            MasterId = cmd.MasterId,
            ServiceId = cmd.ServiceId,
            BranchId = cmd.BranchId,
            StartDateTime = cmd.StartDateTime,
            DurationMinutes = service.DurationMinutes,
            PriceSnapshot = service.Price,
            Status = BookingStatus.Created,
            Source = NormalizeSource(cmd.Source),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.Bookings.Add(booking);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return BookingResult.Fail(BookingErrorCode.SlotTaken, "Слот занят, выберите другое время.");
        }

        return BookingResult.Ok(booking.BookingId);
    }

    public async Task<BookingResult> CancelAsync(int bookingId, int actorUserId, string actorRole, string? reason, CancellationToken ct)
    {
        var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.BookingId == bookingId, ct);
        if (booking is null) return BookingResult.Fail(BookingErrorCode.NotFound, "Запись не найдена.");

        if (booking.Status is BookingStatus.Cancelled or BookingStatus.Completed or BookingStatus.NoShow)
            return BookingResult.Fail(BookingErrorCode.InvalidStatusTransition, $"Нельзя отменить запись в статусе {booking.Status}.");

        if (actorRole == RoleCode.Client)
        {
            // Client may cancel only their own.
            var ownsBooking = await _db.Clients
                .Where(c => c.ClientId == booking.ClientId)
                .Join(_db.Persona, c => c.PersonaId, p => p.PersonaId, (c, p) => p)
                .Join(_db.Users, p => p.PersonaId, u => u.PersonaId, (p, u) => u.UserId)
                .AnyAsync(uid => uid == actorUserId, ct);
            if (!ownsBooking) return BookingResult.Fail(BookingErrorCode.Unauthorized, "Нельзя отменить чужую запись.");

            var hours = (booking.StartDateTime - DateTime.UtcNow).TotalHours;
            if (hours < _opts.CancelCutoffHours)
                return BookingResult.Fail(BookingErrorCode.CancelTooLate,
                    $"Отмена возможна не позднее, чем за {_opts.CancelCutoffHours} ч до начала.");
        }

        booking.Status = BookingStatus.Cancelled;
        booking.CancelReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        booking.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return BookingResult.Ok(booking.BookingId);
    }

    public async Task<BookingResult> ConfirmAsync(int bookingId, int actorUserId, string actorRole, CancellationToken ct)
    {
        var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.BookingId == bookingId, ct);
        if (booking is null) return BookingResult.Fail(BookingErrorCode.NotFound, "Запись не найдена.");

        if (booking.Status != BookingStatus.Created)
            return BookingResult.Fail(BookingErrorCode.InvalidStatusTransition,
                "Подтвердить можно только запись со статусом «Создана».");

        if (!await ActorOwnsMasterAsync(booking.MasterId, actorUserId, actorRole, ct))
            return BookingResult.Fail(BookingErrorCode.Unauthorized, "Нет прав на подтверждение этой записи.");

        booking.Status = BookingStatus.Confirmed;
        booking.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return BookingResult.Ok(booking.BookingId);
    }

    public async Task<BookingResult> CompleteAsync(CompleteBookingCommand cmd, int actorUserId, string actorRole, CancellationToken ct)
    {
        if (cmd.TotalAmount < 0)
            return BookingResult.Fail(BookingErrorCode.ValidationFailed, "Сумма не может быть отрицательной.");

        var booking = await _db.Bookings.Include(b => b.Visit)
            .FirstOrDefaultAsync(b => b.BookingId == cmd.BookingId, ct);
        if (booking is null) return BookingResult.Fail(BookingErrorCode.NotFound, "Запись не найдена.");

        if (booking.Status is not BookingStatus.Created and not BookingStatus.Confirmed)
            return BookingResult.Fail(BookingErrorCode.InvalidStatusTransition,
                "Завершить можно только активную запись.");

        if (!await ActorOwnsMasterAsync(booking.MasterId, actorUserId, actorRole, ct))
            return BookingResult.Fail(BookingErrorCode.Unauthorized, "Нет прав на завершение этой записи.");

        booking.Status = BookingStatus.Completed;
        booking.UpdatedAt = DateTime.UtcNow;
        if (booking.Visit is null)
        {
            _db.Visits.Add(new Visit
            {
                BookingId = booking.BookingId,
                TotalAmount = cmd.TotalAmount,
                MasterNotes = string.IsNullOrWhiteSpace(cmd.MasterNotes) ? null : cmd.MasterNotes.Trim(),
                CompletedAt = DateTime.UtcNow,
            });
        }
        else
        {
            booking.Visit.TotalAmount = cmd.TotalAmount;
            booking.Visit.MasterNotes = string.IsNullOrWhiteSpace(cmd.MasterNotes) ? null : cmd.MasterNotes.Trim();
            booking.Visit.CompletedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
        return BookingResult.Ok(booking.BookingId);
    }

    public async Task<BookingResult> NoShowAsync(int bookingId, int actorUserId, string actorRole, CancellationToken ct)
    {
        var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.BookingId == bookingId, ct);
        if (booking is null) return BookingResult.Fail(BookingErrorCode.NotFound, "Запись не найдена.");

        if (booking.Status is not BookingStatus.Created and not BookingStatus.Confirmed)
            return BookingResult.Fail(BookingErrorCode.InvalidStatusTransition,
                "Отметить «не пришёл» можно только активную запись.");

        if (!await ActorOwnsMasterAsync(booking.MasterId, actorUserId, actorRole, ct))
            return BookingResult.Fail(BookingErrorCode.Unauthorized, "Нет прав на изменение этой записи.");

        booking.Status = BookingStatus.NoShow;
        booking.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return BookingResult.Ok(booking.BookingId);
    }

    private async Task<bool> ActorOwnsMasterAsync(int masterId, int actorUserId, string actorRole, CancellationToken ct)
    {
        if (actorRole == RoleCode.Owner) return true;

        var master = await _db.Masters.AsNoTracking()
            .Where(m => m.MasterId == masterId)
            .Select(m => new { m.MasterId, m.BranchId, m.PersonaId })
            .FirstOrDefaultAsync(ct);
        if (master is null) return false;

        if (actorRole == RoleCode.Admin)
        {
            var actorBranch = await _db.Users.AsNoTracking()
                .Where(u => u.UserId == actorUserId)
                .Select(u => u.BranchId)
                .FirstOrDefaultAsync(ct);
            return actorBranch == master.BranchId;
        }

        if (actorRole == RoleCode.Master)
        {
            var actorPersonaId = await _db.Users.AsNoTracking()
                .Where(u => u.UserId == actorUserId)
                .Select(u => u.PersonaId)
                .FirstOrDefaultAsync(ct);
            return actorPersonaId == master.PersonaId;
        }

        return false;
    }

    private static string NormalizeSource(string raw) => raw switch
    {
        BookingSource.Online or BookingSource.Admin or BookingSource.Lead => raw,
        _ => BookingSource.Online,
    };

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        if (ex.InnerException is SqliteException sqliteEx)
        {
            // SQLITE_CONSTRAINT_UNIQUE = 2067; primary code is 19.
            return sqliteEx.SqliteErrorCode == 19 && sqliteEx.SqliteExtendedErrorCode == 2067;
        }
        return false;
    }
}
