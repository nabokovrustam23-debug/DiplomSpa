namespace BarbershopCrm.Infrastructure.Bookings;

public enum BookingErrorCode
{
    None = 0,
    NotFound = 1,
    SlotTaken = 2,
    SlotInPast = 3,
    SlotInvalid = 4,
    BranchInactive = 5,
    ServiceInactive = 6,
    MasterInactive = 7,
    MasterCannotDoService = 8,
    MasterNotInBranch = 9,
    OutsideWorkingHours = 10,
    OutsideWorkSchedule = 11,
    CancelTooLate = 12,
    InvalidStatusTransition = 13,
    Unauthorized = 14,
    ValidationFailed = 15,
}

public sealed record BookingResult(
    bool Success,
    BookingErrorCode ErrorCode = BookingErrorCode.None,
    string? Message = null,
    int? BookingId = null)
{
    public static BookingResult Ok(int bookingId) => new(true, BookingErrorCode.None, null, bookingId);
    public static BookingResult Fail(BookingErrorCode code, string message) => new(false, code, message);
}
