namespace BarbershopCrm.Infrastructure.Auth;

/// <summary>
/// Snapshot of the authenticated user, attached to HttpContext on each request.
/// </summary>
public sealed record CurrentUser(
    int UserId,
    int PersonaId,
    string Login,
    string Email,
    string FullName,
    string RoleCode,
    int? BranchId,
    bool IsEmailConfirmed,
    int SessionId);
