namespace BarbershopCrm.Domain.Enums;

public static class UserTokenPurpose
{
    public const string EmailVerification = "EmailVerification";
    public const string PasswordReset = "PasswordReset";

    public static readonly string[] All = { EmailVerification, PasswordReset };
}
