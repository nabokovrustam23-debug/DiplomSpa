using BarbershopCrm.Domain.Entities;

namespace BarbershopCrm.Infrastructure.Bookings;

public sealed record CreateBookingCommand(
    int ClientId,
    int BranchId,
    int ServiceId,
    int MasterId,
    DateTime StartDateTime,
    string Source);

public sealed record CompleteBookingCommand(
    int BookingId,
    decimal TotalAmount,
    string? MasterNotes);

public interface IBookingService
{
    Task<BookingResult> CreateAsync(CreateBookingCommand cmd, CancellationToken ct);
    Task<BookingResult> CancelAsync(int bookingId, int actorUserId, string actorRole, string? reason, CancellationToken ct);
    Task<BookingResult> ConfirmAsync(int bookingId, int actorUserId, string actorRole, CancellationToken ct);
    Task<BookingResult> CompleteAsync(CompleteBookingCommand cmd, int actorUserId, string actorRole, CancellationToken ct);
    Task<BookingResult> NoShowAsync(int bookingId, int actorUserId, string actorRole, CancellationToken ct);
}
