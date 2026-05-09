namespace BarbershopCrm.Infrastructure.Auth;

public enum LoginFailureReason
{
    InvalidCredentials,
    AccountInactive,
}

public abstract record LoginResult
{
    public sealed record Success(int UserId, string SessionToken, DateTime ExpiresAt) : LoginResult;
    public sealed record Failure(LoginFailureReason Reason) : LoginResult;
}

public enum RegistrationFailureReason
{
    EmailAlreadyUsed,
    PhoneAlreadyUsed,
}

public abstract record RegistrationResult
{
    public sealed record Success(int UserId, string EmailVerificationToken) : RegistrationResult;
    public sealed record Failure(RegistrationFailureReason Reason) : RegistrationResult;
}

public enum ConsumeTokenFailureReason
{
    NotFound,
    Expired,
    AlreadyUsed,
    PurposeMismatch,
}

public abstract record ConsumeTokenResult
{
    public sealed record Success(int UserId) : ConsumeTokenResult;
    public sealed record Failure(ConsumeTokenFailureReason Reason) : ConsumeTokenResult;
}
