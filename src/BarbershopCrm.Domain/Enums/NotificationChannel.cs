namespace BarbershopCrm.Domain.Enums;

public static class NotificationChannel
{
    public const string Email = "Email";
    public const string Sms = "Sms";
    public const string InApp = "InApp";

    public static readonly string[] All = { Email, Sms, InApp };
}
