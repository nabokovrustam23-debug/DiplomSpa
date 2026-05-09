namespace BarbershopCrm.Infrastructure.Auth;

public interface ICurrentUserAccessor
{
    CurrentUser? User { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string roleCode);
}
