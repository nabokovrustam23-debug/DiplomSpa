namespace BarbershopCrm.Domain.Enums;

public static class NotificationStatus
{
    public const string Pending = "Pending";
    public const string Sent = "Sent";
    public const string Failed = "Failed";

    public static readonly string[] All = { Pending, Sent, Failed };
}
