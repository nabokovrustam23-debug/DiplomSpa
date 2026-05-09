namespace BarbershopCrm.Domain.Enums;

public static class LeadStatus
{
    public const string New = "New";
    public const string InProgress = "InProgress";
    public const string Done = "Done";
    public const string Rejected = "Rejected";

    public static readonly string[] All = { New, InProgress, Done, Rejected };
}
