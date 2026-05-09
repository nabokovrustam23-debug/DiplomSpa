namespace BarbershopCrm.Infrastructure.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string SessionCookieName { get; set; } = "tihiy_session";
    public int SessionLifetimeDays { get; set; } = 30;
    public int EmailVerificationTokenLifetimeHours { get; set; } = 24;
    public int PasswordResetTokenLifetimeHours { get; set; } = 1;

    public string PublicBaseUrl { get; set; } = "http://localhost:5158";
}
