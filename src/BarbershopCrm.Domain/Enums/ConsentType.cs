namespace BarbershopCrm.Domain.Enums;

public static class ConsentType
{
    public const string PersonalData = "PersonalData";
    public const string Marketing = "Marketing";

    public static readonly string[] All = { PersonalData, Marketing };
}
