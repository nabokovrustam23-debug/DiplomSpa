using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BarbershopCrm.Infrastructure.Auth;

public sealed class UserAuthService : IUserAuthService
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenGenerator _tokens;
    private readonly TimeProvider _clock;
    private readonly ILogger<UserAuthService> _log;
    private readonly AuthOptions _options;

    public UserAuthService(
        AppDbContext db,
        IPasswordHasher hasher,
        ITokenGenerator tokens,
        TimeProvider clock,
        IOptions<AuthOptions> options,
        ILogger<UserAuthService> log)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
        _clock = clock;
        _options = options.Value;
        _log = log;
    }

    public async Task<RegistrationResult> RegisterClientAsync(
        RegisterClientCommand cmd,
        CancellationToken ct = default)
    {
        var email = NormaliseEmail(cmd.Email);
        var phone = NormalisePhone(cmd.Phone);

        if (await _db.Users.AnyAsync(u => u.Login == email, ct))
            return new RegistrationResult.Failure(RegistrationFailureReason.EmailAlreadyUsed);

        if (await _db.Persona.AnyAsync(p => p.Phone == phone, ct))
            return new RegistrationResult.Failure(RegistrationFailureReason.PhoneAlreadyUsed);

        var clientRoleId = await _db.Roles
            .Where(r => r.Code == RoleCode.Client)
            .Select(r => r.RoleId)
            .SingleAsync(ct);

        var hash = _hasher.Hash(cmd.Password);
        var now = _clock.GetUtcNow().UtcDateTime;

        var persona = new Persona
        {
            LastName = cmd.LastName.Trim(),
            FirstName = cmd.FirstName.Trim(),
            MiddleName = string.IsNullOrWhiteSpace(cmd.MiddleName) ? null : cmd.MiddleName.Trim(),
            Phone = phone,
            Email = email,
        };

        var user = new User
        {
            Persona = persona,
            RoleId = clientRoleId,
            Login = email,
            PasswordHash = hash.HashBase64,
            PasswordSalt = hash.SaltBase64,
            PasswordIterations = hash.Iterations,
            IsEmailConfirmed = false,
            IsActive = true,
            CreatedAt = now,
        };

        var client = new Client
        {
            Persona = persona,
            Source = "self",
            CreatedAt = now,
        };

        var tokenString = _tokens.GenerateUrlSafeToken();
        var emailToken = new UserToken
        {
            User = user,
            Purpose = UserTokenPurpose.EmailVerification,
            Token = tokenString,
            ExpiresAt = now.AddHours(_options.EmailVerificationTokenLifetimeHours),
            CreatedAt = now,
        };

        _db.Persona.Add(persona);
        _db.Users.Add(user);
        _db.Clients.Add(client);
        _db.UserTokens.Add(emailToken);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation(
            "Registered client {UserId} ({Email}); email-verification token issued, expires {Expires:o}",
            user.UserId, email, emailToken.ExpiresAt);

        return new RegistrationResult.Success(user.UserId, tokenString);
    }

    public async Task<LoginResult> LoginAsync(
        string login,
        string password,
        string? userAgent,
        string? ipAddress,
        CancellationToken ct = default)
    {
        var email = NormaliseEmail(login);

        var user = await _db.Users
            .AsTracking()
            .FirstOrDefaultAsync(u => u.Login == email, ct);

        if (user is null)
        {
            // Equal-time fake verification to mitigate timing-based enumeration.
            _hasher.Verify("placeholder-password", "AAAAAAAAAAAAAAAAAAAAAA==", "AAAAAAAAAAAAAAAA",
                Pbkdf2PasswordHasher.DefaultIterations);
            _log.LogInformation("Login failed for unknown user {Login}", email);
            return new LoginResult.Failure(LoginFailureReason.InvalidCredentials);
        }

        if (!user.IsActive)
        {
            _log.LogInformation("Login refused: user {UserId} is inactive", user.UserId);
            return new LoginResult.Failure(LoginFailureReason.AccountInactive);
        }

        var ok = _hasher.Verify(password, user.PasswordHash, user.PasswordSalt, user.PasswordIterations);
        if (!ok)
        {
            _log.LogInformation("Login failed: wrong password for user {UserId}", user.UserId);
            return new LoginResult.Failure(LoginFailureReason.InvalidCredentials);
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var sessionToken = _tokens.GenerateUrlSafeToken();
        var expiresAt = now.AddDays(_options.SessionLifetimeDays);

        _db.UserSessions.Add(new UserSession
        {
            UserId = user.UserId,
            Token = sessionToken,
            ExpiresAt = expiresAt,
            CreatedAt = now,
            UserAgent = Truncate(userAgent, 500),
            IpAddress = Truncate(ipAddress, 64),
        });

        user.LastLoginAt = now;
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("User {UserId} logged in (session expires {Expires:o})", user.UserId, expiresAt);
        return new LoginResult.Success(user.UserId, sessionToken, expiresAt);
    }

    public async Task LogoutAsync(string sessionToken, CancellationToken ct = default)
    {
        var session = await _db.UserSessions
            .AsTracking()
            .FirstOrDefaultAsync(s => s.Token == sessionToken, ct);

        if (session is null || session.RevokedAt is not null)
            return;

        session.RevokedAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Session {SessionId} revoked (logout)", session.SessionId);
    }

    public async Task<CurrentUser?> ResolveSessionAsync(string sessionToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
            return null;

        var now = _clock.GetUtcNow().UtcDateTime;

        var data = await _db.UserSessions
            .AsNoTracking()
            .Where(s => s.Token == sessionToken
                        && s.RevokedAt == null
                        && s.ExpiresAt > now
                        && s.User.IsActive)
            .Select(s => new
            {
                s.SessionId,
                s.User.UserId,
                s.User.PersonaId,
                s.User.Login,
                Email = s.User.Persona.Email,
                LastName = s.User.Persona.LastName,
                FirstName = s.User.Persona.FirstName,
                MiddleName = s.User.Persona.MiddleName,
                RoleCode = s.User.Role.Code,
                s.User.BranchId,
                s.User.IsEmailConfirmed,
            })
            .FirstOrDefaultAsync(ct);

        if (data is null)
            return null;

        var fullName = string.Join(' ',
            new[] { data.LastName, data.FirstName, data.MiddleName }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

        return new CurrentUser(
            UserId: data.UserId,
            PersonaId: data.PersonaId,
            Login: data.Login,
            Email: data.Email ?? data.Login,
            FullName: fullName,
            RoleCode: data.RoleCode,
            BranchId: data.BranchId,
            IsEmailConfirmed: data.IsEmailConfirmed,
            SessionId: data.SessionId);
    }

    public async Task<ConsumeTokenResult> ConfirmEmailAsync(string token, CancellationToken ct = default)
    {
        var (result, userToken) = await FindTokenAsync(token, UserTokenPurpose.EmailVerification, ct);
        if (result is not null)
            return result;

        userToken!.ConsumedAt = _clock.GetUtcNow().UtcDateTime;
        userToken.User.IsEmailConfirmed = true;
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Email confirmed for user {UserId}", userToken.UserId);
        return new ConsumeTokenResult.Success(userToken.UserId);
    }

    public async Task<string?> IssueEmailVerificationTokenAsync(string email, CancellationToken ct = default)
    {
        var emailNorm = NormaliseEmail(email);
        var user = await _db.Users
            .AsTracking()
            .FirstOrDefaultAsync(u => u.Login == emailNorm, ct);

        if (user is null || user.IsEmailConfirmed || !user.IsActive)
            return null;

        // Invalidate any prior unconsumed verification tokens.
        var now = _clock.GetUtcNow().UtcDateTime;
        var prior = await _db.UserTokens
            .AsTracking()
            .Where(t => t.UserId == user.UserId
                        && t.Purpose == UserTokenPurpose.EmailVerification
                        && t.ConsumedAt == null
                        && t.ExpiresAt > now)
            .ToListAsync(ct);

        foreach (var t in prior)
            t.ConsumedAt = now;

        var tokenString = _tokens.GenerateUrlSafeToken();
        _db.UserTokens.Add(new UserToken
        {
            UserId = user.UserId,
            Purpose = UserTokenPurpose.EmailVerification,
            Token = tokenString,
            ExpiresAt = now.AddHours(_options.EmailVerificationTokenLifetimeHours),
            CreatedAt = now,
        });

        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Email-verification token re-issued for user {UserId}", user.UserId);
        return tokenString;
    }

    public async Task<string?> IssuePasswordResetTokenAsync(string email, CancellationToken ct = default)
    {
        var emailNorm = NormaliseEmail(email);
        var user = await _db.Users
            .AsTracking()
            .FirstOrDefaultAsync(u => u.Login == emailNorm, ct);

        if (user is null || !user.IsActive)
            return null;

        var now = _clock.GetUtcNow().UtcDateTime;

        var prior = await _db.UserTokens
            .AsTracking()
            .Where(t => t.UserId == user.UserId
                        && t.Purpose == UserTokenPurpose.PasswordReset
                        && t.ConsumedAt == null
                        && t.ExpiresAt > now)
            .ToListAsync(ct);

        foreach (var t in prior)
            t.ConsumedAt = now;

        var tokenString = _tokens.GenerateUrlSafeToken();
        _db.UserTokens.Add(new UserToken
        {
            UserId = user.UserId,
            Purpose = UserTokenPurpose.PasswordReset,
            Token = tokenString,
            ExpiresAt = now.AddHours(_options.PasswordResetTokenLifetimeHours),
            CreatedAt = now,
        });

        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Password-reset token issued for user {UserId}", user.UserId);
        return tokenString;
    }

    public async Task<ConsumeTokenResult> ResetPasswordAsync(string token, string newPassword, CancellationToken ct = default)
    {
        var (result, userToken) = await FindTokenAsync(token, UserTokenPurpose.PasswordReset, ct);
        if (result is not null)
            return result;

        var now = _clock.GetUtcNow().UtcDateTime;
        var hash = _hasher.Hash(newPassword);

        userToken!.ConsumedAt = now;
        var user = userToken.User;
        user.PasswordHash = hash.HashBase64;
        user.PasswordSalt = hash.SaltBase64;
        user.PasswordIterations = hash.Iterations;

        // Revoke all active sessions for this user.
        var sessions = await _db.UserSessions
            .AsTracking()
            .Where(s => s.UserId == user.UserId && s.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var s in sessions)
            s.RevokedAt = now;

        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Password reset for user {UserId}; revoked {Sessions} active sessions",
            user.UserId, sessions.Count);

        return new ConsumeTokenResult.Success(user.UserId);
    }

    private async Task<(ConsumeTokenResult? Failure, UserToken? Token)> FindTokenAsync(
        string token, string expectedPurpose, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            return (new ConsumeTokenResult.Failure(ConsumeTokenFailureReason.NotFound), null);

        var entry = await _db.UserTokens
            .AsTracking()
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == token, ct);

        if (entry is null)
            return (new ConsumeTokenResult.Failure(ConsumeTokenFailureReason.NotFound), null);

        if (entry.Purpose != expectedPurpose)
            return (new ConsumeTokenResult.Failure(ConsumeTokenFailureReason.PurposeMismatch), null);

        if (entry.ConsumedAt is not null)
            return (new ConsumeTokenResult.Failure(ConsumeTokenFailureReason.AlreadyUsed), null);

        if (entry.ExpiresAt <= _clock.GetUtcNow().UtcDateTime)
            return (new ConsumeTokenResult.Failure(ConsumeTokenFailureReason.Expired), null);

        return (null, entry);
    }

    internal static string NormaliseEmail(string email)
        => (email ?? string.Empty).Trim().ToLowerInvariant();

    internal static string NormalisePhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            throw new ArgumentException("Phone is required.", nameof(phone));

        // Keep digits and leading +.
        var trimmed = phone.Trim();
        var hasPlus = trimmed.StartsWith('+');
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());

        return hasPlus ? "+" + digits : digits;
    }

    private static string? Truncate(string? s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max];
}
