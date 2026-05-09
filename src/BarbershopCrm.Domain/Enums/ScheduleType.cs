namespace BarbershopCrm.Domain.Enums;

public static class ScheduleType
{
    public const string Work = "Work";
    public const string Lunch = "Lunch";
    public const string DayOff = "DayOff";
    public const string Vacation = "Vacation";
    public const string SickLeave = "SickLeave";

    public static readonly string[] All =
    {
        Work, Lunch, DayOff, Vacation, SickLeave
    };
}
