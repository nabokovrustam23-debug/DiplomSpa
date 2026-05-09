namespace BarbershopCrm.Domain.Enums;

public static class BookingSource
{
    public const string Online = "Online";
    public const string Admin = "Admin";
    public const string Lead = "Lead";

    public static readonly string[] All = { Online, Admin, Lead };
}
