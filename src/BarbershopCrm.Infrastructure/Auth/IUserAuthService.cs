namespace BarbershopCrm.Infrastructure.Auth;

/// <summary>
/// Coordinates user authentication, registration, email verification and password reset.
/// All token strings are URL-safe; emails are normalised by the caller (trim + lower-case).
/// </summary>
public interface IUserAuthService
{
    Task<RegistrationResult> RegisterClientAsync(
        RegisterClientCommand command,
        CancellationToken cancellationToken = default);

    Task<LoginResult> LoginAsync(
        string login,
        string password,
        string? userAgent,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task LogoutAsync(string sessionToken, CancellationToken cancellationToken = default);

    Task<CurrentUser?> ResolveSessionAsync(
        string sessionToken,
        CancellationToken cancellationToken = default);

    Task<ConsumeTokenResult> ConfirmEmailAsync(
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-issues an email verification token for an existing unconfirmed user.
    /// Returns null if the email is unknown or already confirmed (anti-enumeration: caller
    /// should still report success to the user).
    /// </summary>
    Task<string?> IssueEmailVerificationTokenAsync(
        string email,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a password reset token for the user with the given email.
    /// Returns null if the email is unknown (anti-enumeration: caller should still report
    /// success to the user).
    /// </summary>
    Task<string?> IssuePasswordResetTokenAsync(
        string email,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Consumes a password reset token, sets the new password, and revokes ALL active
    /// sessions of the affected user.
    /// </summary>
    Task<ConsumeTokenResult> ResetPasswordAsync(
        string token,
        string newPassword,
        CancellationToken cancellationToken = default);
}

public sealed record RegisterClientCommand(
    string Email,
    string Password,
    string LastName,
    string FirstName,
    string? MiddleName,
    string Phone);
