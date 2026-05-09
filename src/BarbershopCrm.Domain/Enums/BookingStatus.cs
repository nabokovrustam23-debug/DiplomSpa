namespace BarbershopCrm.Domain.Enums;

public static class BookingStatus
{
    public const string Created = "Created";
    public const string Confirmed = "Confirmed";
    public const string Cancelled = "Cancelled";
    public const string Completed = "Completed";
    public const string NoShow = "NoShow";

    public static readonly string[] All =
    {
        Created, Confirmed, Cancelled, Completed, NoShow
    };

    public static readonly string[] Active = { Created, Confirmed };
}
