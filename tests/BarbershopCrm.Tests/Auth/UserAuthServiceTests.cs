using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BarbershopCrm.Tests.Auth;

public class UserAuthServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _dbOptions;
    private readonly Pbkdf2PasswordHasher _hasher = new(iterations: 10_000); // minimum allowed
    private readonly DeterministicTokenGenerator _tokens = new();
    private readonly TestClock _clock = new(DateTimeOffset.Parse("2026-01-15T10:00:00Z"));
    private readonly AuthOptions _options = new();

    public UserAuthServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:;Cache=Shared");
        _connection.Open();
        _dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
    }

    public async Task InitializeAsync()
    {
        using var db = new AppDbContext(_dbOptions);
        await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync()
    {
        _connection.Dispose();
        return Task.CompletedTask;
    }

    private AppDbContext NewContext() => new(_dbOptions);

    private UserAuthService NewService(AppDbContext db) =>
        new(db, _hasher, _tokens, _clock, Options.Create(_options),
            NullLogger<UserAuthService>.Instance);

    private static RegisterClientCommand SampleClientCmd(string email = "alice@example.com",
        string phone = "+79180001111") =>
        new(email, "Secret12345", "Иванова", "Алиса", null, phone);

    // --------------------------- Register ---------------------------

    [Fact]
    public async Task Register_Success_CreatesUserWithClientRoleAndIssuesEmailToken()
    {
        await using var db = NewContext();
        var svc = NewService(db);
        _tokens.Enqueue("emailtok-1");

        var result = await svc.RegisterClientAsync(SampleClientCmd());

        result.Should().BeOfType<RegistrationResult.Success>();
        var success = (RegistrationResult.Success)result;
        success.EmailVerificationToken.Should().Be("emailtok-1");

        var user = await db.Users.Include(u => u.Persona).Include(u => u.Role).SingleAsync();
        user.Login.Should().Be("alice@example.com");
        user.IsEmailConfirmed.Should().BeFalse();
        user.IsActive.Should().BeTrue();
        user.Role.Code.Should().Be(RoleCode.Client);
        user.Persona.LastName.Should().Be("Иванова");
        user.Persona.Phone.Should().Be("+79180001111");

        var clientRow = await db.Clients.SingleAsync();
        clientRow.PersonaId.Should().Be(user.PersonaId);

        var token = await db.UserTokens.SingleAsync();
        token.Purpose.Should().Be(UserTokenPurpose.EmailVerification);
        token.Token.Should().Be("emailtok-1");
        token.ConsumedAt.Should().BeNull();
        token.ExpiresAt.Should().Be(_clock.UtcNow.UtcDateTime.AddHours(_options.EmailVerificationTokenLifetimeHours));
    }

    [Fact]
    public async Task Register_NormalisesEmail_ToLowerInvariantTrimmed()
    {
        await using var db = NewContext();
        var svc = NewService(db);
        _tokens.Enqueue("t1");

        await svc.RegisterClientAsync(SampleClientCmd("  Alice@Example.COM "));

        var user = await db.Users.SingleAsync();
        user.Login.Should().Be("alice@example.com");
    }

    [Fact]
    public async Task Register_Returns_EmailAlreadyUsed_WhenLoginExists()
    {
        await using var db = NewContext();
        var svc = NewService(db);
        _tokens.Enqueue("t1");
        _tokens.Enqueue("t2");

        await svc.RegisterClientAsync(SampleClientCmd("dup@example.com", "+79180001112"));
        var second = await svc.RegisterClientAsync(SampleClientCmd("DUP@example.com", "+79180001113"));

        second.Should().BeOfType<RegistrationResult.Failure>()
            .Which.Reason.Should().Be(RegistrationFailureReason.EmailAlreadyUsed);
        (await db.Users.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Register_Returns_PhoneAlreadyUsed_WhenPhoneExists()
    {
        await using var db = NewContext();
        var svc = NewService(db);
        _tokens.Enqueue("t1");
        _tokens.Enqueue("t2");

        await svc.RegisterClientAsync(SampleClientCmd("first@example.com", "+79180005555"));
        var second = await svc.RegisterClientAsync(SampleClientCmd("second@example.com", "+7 (918) 000-55-55"));

        second.Should().BeOfType<RegistrationResult.Failure>()
            .Which.Reason.Should().Be(RegistrationFailureReason.PhoneAlreadyUsed);
        (await db.Users.CountAsync()).Should().Be(1);
    }

    // --------------------------- Login ---------------------------

    [Fact]
    public async Task Login_Success_CreatesActiveSession_AndUpdatesLastLogin()
    {
        await using var db = NewContext();
        var svc = NewService(db);
        _tokens.Enqueue("emailtok-1");
        _tokens.Enqueue("session-1");

        await svc.RegisterClientAsync(SampleClientCmd("login@example.com"));

        _clock.Advance(TimeSpan.FromMinutes(1));
        var result = await svc.LoginAsync("login@example.com", "Secret12345", "ua", "127.0.0.1");

        result.Should().BeOfType<LoginResult.Success>();
        var s = (LoginResult.Success)result;
        s.SessionToken.Should().Be("session-1");
        s.ExpiresAt.Should().Be(_clock.UtcNow.UtcDateTime.AddDays(_options.SessionLifetimeDays));

        var session = await db.UserSessions.SingleAsync();
        session.Token.Should().Be("session-1");
        session.RevokedAt.Should().BeNull();
        session.UserAgent.Should().Be("ua");
        session.IpAddress.Should().Be("127.0.0.1");

        var user = await db.Users.SingleAsync();
        user.LastLoginAt.Should().Be(_clock.UtcNow.UtcDateTime);
    }

    [Fact]
    public async Task Login_InvalidCredentials_WhenUserUnknown()
    {
        await using var db = NewContext();
        var svc = NewService(db);

        var result = await svc.LoginAsync("nobody@nowhere.zz", "anything", null, null);

        result.Should().BeOfType<LoginResult.Failure>()
            .Which.Reason.Should().Be(LoginFailureReason.InvalidCredentials);
    }

    [Fact]
    public async Task Login_InvalidCredentials_WhenWrongPassword()
    {
        await using var db = NewContext();
        var svc = NewService(db);
        _tokens.Enqueue("emailtok-1");
        await svc.RegisterClientAsync(SampleClientCmd("login@example.com"));

        var result = await svc.LoginAsync("login@example.com", "wrong-password", null, null);

        result.Should().BeOfType<LoginResult.Failure>()
            .Which.Reason.Should().Be(LoginFailureReason.InvalidCredentials);
    }

    [Fact]
    public async Task Login_AccountInactive_WhenUserDisabled()
    {
        await using (var db = NewContext())
        {
            var svc = NewService(db);
            _tokens.Enqueue("emailtok-1");
            await svc.RegisterClientAsync(SampleClientCmd("login@example.com"));

            var u = await db.Users.SingleAsync();
            u.IsActive = false;
            await db.SaveChangesAsync();
        }

        await using var db2 = NewContext();
        var svc2 = NewService(db2);
        var result = await svc2.LoginAsync("login@example.com", "Secret12345", null, null);

        result.Should().BeOfType<LoginResult.Failure>()
            .Which.Reason.Should().Be(LoginFailureReason.AccountInactive);
    }

    // --------------------------- Resolve session ---------------------------

    [Fact]
    public async Task ResolveSession_ReturnsCurrentUser_WhenActive()
    {
        await using var db = NewContext();
        var svc = NewService(db);
        _tokens.Enqueue("emailtok-1");
        _tokens.Enqueue("session-1");
        await svc.RegisterClientAsync(SampleClientCmd("client@example.com", "+79180009999"));
        await svc.LoginAsync("client@example.com", "Secret12345", null, null);

        var current = await svc.ResolveSessionAsync("session-1");

        current.Should().NotBeNull();
        current!.Login.Should().Be("client@example.com");
        current.Email.Should().Be("client@example.com");
        current.RoleCode.Should().Be(RoleCode.Client);
        current.IsEmailConfirmed.Should().BeFalse();
        current.FullName.Should().Be("Иванова Алиса");
    }

    [Fact]
    public async Task ResolveSession_ReturnsNull_AfterLogout()
    {
        await using var db = NewContext();
        var svc = NewService(db);
        _tokens.Enqueue("emailtok-1");
        _tokens.Enqueue("session-1");
        await svc.RegisterClientAsync(SampleClientCmd("client@example.com", "+79180009999"));
        await svc.LoginAsync("client@example.com", "Secret12345", null, null);

        await svc.LogoutAsync("session-1");

        (await svc.ResolveSessionAsync("session-1")).Should().BeNull();
    }

    [Fact]
    public async Task ResolveSession_ReturnsNull_WhenExpired()
    {
        await using var db = NewContext();
        var svc = NewService(db);
        _tokens.Enqueue("emailtok-1");
        _tokens.Enqueue("session-1");
        await svc.RegisterClientAsync(SampleClientCmd("client@example.com", "+79180009999"));
        await svc.LoginAsync("client@example.com", "Secret12345", null, null);

        // Move clock past session lifetime
        _clock.Advance(TimeSpan.FromDays(_options.SessionLifetimeDays + 1));

        (await svc.ResolveSessionAsync("session-1")).Should().BeNull();
    }

    [Fact]
    public async Task ResolveSession_ReturnsNull_ForUnknownToken()
    {
        await using var db = NewContext();
        var svc = NewService(db);

        (await svc.ResolveSessionAsync("does-not-exist")).Should().BeNull();
        (await svc.ResolveSessionAsync(string.Empty)).Should().BeNull();
    }

    // --------------------------- Confirm email ---------------------------

    [Fact]
    public async Task ConfirmEmail_Success_MarksUserConfirmedAndConsumesToken()
    {
        await using var db = NewContext();
        var svc = NewService(db);
        _tokens.Enqueue("emailtok-1");
        await svc.RegisterClientAsync(SampleClientCmd("c@example.com"));

        var result = await svc.ConfirmEmailAsync("emailtok-1");

        result.Should().BeOfType<ConsumeTokenResult.Success>();
        var user = await db.Users.SingleAsync();
        user.IsEmailConfirmed.Should().BeTrue();
        var token = await db.UserTokens.SingleAsync();
        token.ConsumedAt.Should().Be(_clock.UtcNow.UtcDateTime);
    }

    [Fact]
    public async Task ConfirmEmail_AlreadyUsed_OnReplay()
    {
        await using var db = NewContext();
        var svc = NewService(db);
        _tokens.Enqueue("emailtok-1");
        await svc.RegisterClientAsync(SampleClientCmd("c@example.com"));

        await svc.ConfirmEmailAsync("emailtok-1");
        var replay = await svc.ConfirmEmailAsync("emailtok-1");

        replay.Should().BeOfType<ConsumeTokenResult.Failure>()
            .Which.Reason.Should().Be(ConsumeTokenFailureReason.AlreadyUsed);
    }

    [Fact]
    public async Task ConfirmEmail_NotFound_WhenTokenUnknown()
    {
        await using var db = NewContext();
        var svc = NewService(db);

        var result = await svc.ConfirmEmailAsync("nope");

        result.Should().BeOfType<ConsumeTokenResult.Failure>()
            .Which.Reason.Should().Be(ConsumeTokenFailureReason.NotFound);
    }

    [Fact]
    public async Task ConfirmEmail_Expired_WhenPastExpiry()
    {
        await using var db = NewContext();
        var svc = NewService(db);
        _tokens.Enqueue("emailtok-1");
        await svc.RegisterClientAsync(SampleClientCmd("c@example.com"));

        _clock.Advance(TimeSpan.FromHours(_options.EmailVerificationTokenLifetimeHours + 1));

        var result = await svc.ConfirmEmailAsync("emailtok-1");

        result.Should().BeOfType<ConsumeTokenResult.Failure>()
            .Which.Reason.Should().Be(ConsumeTokenFailureReason.Expired);
    }

    [Fact]
    public async Task IssueEmailVerificationToken_InvalidatesPriorTokens()
    {
        await using var db = NewContext();
        var svc = NewService(db);
        _tokens.Enqueue("first-token");
        _tokens.Enqueue("second-token");
        await svc.RegisterClientAsync(SampleClientCmd("c@example.com"));

        var newToken = await svc.IssueEmailVerificationTokenAsync("c@example.com");

        newToken.Should().Be("second-token");
        var tokens = await db.UserTokens.OrderBy(t => t.TokenId).ToListAsync();
        tokens.Should().HaveCount(2);
        tokens[0].Token.Should().Be("first-token");
        tokens[0].ConsumedAt.Should().Be(_clock.UtcNow.UtcDateTime);
        tokens[1].Token.Should().Be("second-token");
        tokens[1].ConsumedAt.Should().BeNull();
    }

    [Fact]
    public async Task IssueEmailVerificationToken_ReturnsNull_WhenAlreadyConfirmed()
    {
        await using var db = NewContext();
        var svc = NewService(db);
        _tokens.Enqueue("first-token");
        await svc.RegisterClientAsync(SampleClientCmd("c@example.com"));
        await svc.ConfirmEmailAsync("first-token");

        var result = await svc.IssueEmailVerificationTokenAsync("c@example.com");

        result.Should().BeNull();
    }

    [Fact]
    public async Task IssueEmailVerificationToken_ReturnsNull_ForUnknownEmail()
    {
        await using var db = NewContext();
        var svc = NewService(db);

        var result = await svc.IssueEmailVerificationTokenAsync("ghost@nowhere.zz");

        result.Should().BeNull();
    }

    // --------------------------- Reset password ---------------------------

    [Fact]
    public async Task IssuePasswordResetToken_Success_AndOldTokensInvalidated()
    {
        await using var db = NewContext();
        var svc = NewService(db);
        _tokens.Enqueue("emailtok-1");
        _tokens.Enqueue("reset-1");
        _tokens.Enqueue("reset-2");
        await svc.RegisterClientAsync(SampleClientCmd("u@example.com"));

        var t1 = await svc.IssuePasswordResetTokenAsync("u@example.com");
        var t2 = await svc.IssuePasswordResetTokenAsync("u@example.com");

        t1.Should().Be("reset-1");
        t2.Should().Be("reset-2");

        var resets = await db.UserTokens
            .Where(t => t.Purpose == UserTokenPurpose.PasswordReset)
            .OrderBy(t => t.TokenId)
            .ToListAsync();
        resets.Should().HaveCount(2);
        resets[0].ConsumedAt.Should().NotBeNull();
        resets[1].ConsumedAt.Should().BeNull();
    }

    [Fact]
    public async Task IssuePasswordResetToken_ReturnsNull_ForUnknownEmail()
    {
        await using var db = NewContext();
        var svc = NewService(db);

        (await svc.IssuePasswordResetTokenAsync("ghost@nowhere.zz")).Should().BeNull();
    }

    [Fact]
    public async Task ResetPassword_Success_HashesNewPassword_RevokesAllSessions_ConsumesToken()
    {
        await using var db = NewContext();
        var svc = NewService(db);
        _tokens.Enqueue("emailtok-1");
        _tokens.Enqueue("session-1");
        _tokens.Enqueue("session-2");
        _tokens.Enqueue("reset-1");
        await svc.RegisterClientAsync(SampleClientCmd("u@example.com"));
        await svc.LoginAsync("u@example.com", "Secret12345", null, null);
        await svc.LoginAsync("u@example.com", "Secret12345", null, null);
        var resetToken = await svc.IssuePasswordResetTokenAsync("u@example.com");
        resetToken.Should().Be("reset-1");

        // Two active sessions exist before reset
        (await db.UserSessions.CountAsync(s => s.RevokedAt == null)).Should().Be(2);

        var result = await svc.ResetPasswordAsync("reset-1", "BrandNew1234!");

        result.Should().BeOfType<ConsumeTokenResult.Success>();

        // All sessions revoked
        (await db.UserSessions.CountAsync(s => s.RevokedAt == null)).Should().Be(0);

        // Old password no longer works, new password does
        var oldLogin = await svc.LoginAsync("u@example.com", "Secret12345", null, null);
        oldLogin.Should().BeOfType<LoginResult.Failure>();

        _tokens.Enqueue("session-3");
        var newLogin = await svc.LoginAsync("u@example.com", "BrandNew1234!", null, null);
        newLogin.Should().BeOfType<LoginResult.Success>();

        // Token consumed
        var tk = await db.UserTokens.SingleAsync(t => t.Token == "reset-1");
        tk.ConsumedAt.Should().Be(_clock.UtcNow.UtcDateTime);
    }

    [Fact]
    public async Task ResetPassword_NotFound_OnUnknownToken()
    {
        await using var db = NewContext();
        var svc = NewService(db);

        var result = await svc.ResetPasswordAsync("nope", "NewPass1234");
        result.Should().BeOfType<ConsumeTokenResult.Failure>()
            .Which.Reason.Should().Be(ConsumeTokenFailureReason.NotFound);
    }

    [Fact]
    public async Task ResetPassword_Expired_AfterTokenLifetime()
    {
        await using var db = NewContext();
        var svc = NewService(db);
        _tokens.Enqueue("emailtok-1");
        _tokens.Enqueue("reset-1");
        await svc.RegisterClientAsync(SampleClientCmd("u@example.com"));
        await svc.IssuePasswordResetTokenAsync("u@example.com");

        _clock.Advance(TimeSpan.FromHours(_options.PasswordResetTokenLifetimeHours + 1));

        var result = await svc.ResetPasswordAsync("reset-1", "NewPass1234");
        result.Should().BeOfType<ConsumeTokenResult.Failure>()
            .Which.Reason.Should().Be(ConsumeTokenFailureReason.Expired);
    }

    [Fact]
    public async Task ResetPassword_AlreadyUsed_OnReplay()
    {
        await using var db = NewContext();
        var svc = NewService(db);
        _tokens.Enqueue("emailtok-1");
        _tokens.Enqueue("reset-1");
        await svc.RegisterClientAsync(SampleClientCmd("u@example.com"));
        await svc.IssuePasswordResetTokenAsync("u@example.com");

        (await svc.ResetPasswordAsync("reset-1", "NewPass1234"))
            .Should().BeOfType<ConsumeTokenResult.Success>();
        (await svc.ResetPasswordAsync("reset-1", "EvenNewer1234"))
            .Should().BeOfType<ConsumeTokenResult.Failure>()
            .Which.Reason.Should().Be(ConsumeTokenFailureReason.AlreadyUsed);
    }

    // --------------------------- Helpers ---------------------------

    private sealed class DeterministicTokenGenerator : ITokenGenerator
    {
        private readonly Queue<string> _queue = new();
        public void Enqueue(string token) => _queue.Enqueue(token);
        public string GenerateUrlSafeToken(int byteLength = 32)
        {
            if (_queue.TryDequeue(out var t)) return t;
            return Guid.NewGuid().ToString("N");
        }
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; private set; }
        public TestClock(DateTimeOffset start) => UtcNow = start;
        public override DateTimeOffset GetUtcNow() => UtcNow;
        public void Advance(TimeSpan delta) => UtcNow = UtcNow.Add(delta);
    }
}
