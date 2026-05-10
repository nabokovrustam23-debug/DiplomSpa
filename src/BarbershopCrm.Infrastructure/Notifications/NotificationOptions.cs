namespace BarbershopCrm.Infrastructure.Notifications;

public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>How often (seconds) the dispatcher polls Notifications WHERE Status='Pending'.</summary>
    public int PollIntervalSeconds { get; set; } = 10;

    /// <summary>How often (minutes) the reminder job scans for upcoming bookings.</summary>
    public int ReminderIntervalMinutes { get; set; } = 60;

    /// <summary>Hours-before-booking thresholds at which a reminder is generated. Default: 24 h and 2 h.</summary>
    public int[] ReminderHoursBefore { get; set; } = new[] { 24, 2 };

    /// <summary>Maximum number of pending notifications processed per dispatcher tick.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>If false, dispatcher and reminder background services do not run (useful for tests).</summary>
    public bool BackgroundEnabled { get; set; } = true;
}
