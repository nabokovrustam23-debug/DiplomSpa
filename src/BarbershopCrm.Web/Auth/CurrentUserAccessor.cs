using BarbershopCrm.Infrastructure.Auth;
using Microsoft.AspNetCore.Http;

namespace BarbershopCrm.Web.Auth;

public sealed class CurrentUserAccessor : ICurrentUserAccessor
{
    public const string HttpContextItemKey = "BarbershopCrm.CurrentUser";

    private readonly IHttpContextAccessor _accessor;

    public CurrentUserAccessor(IHttpContextAccessor accessor) => _accessor = accessor;

    public CurrentUser? User =>
        _accessor.HttpContext?.Items[HttpContextItemKey] as CurrentUser;

    public bool IsAuthenticated => User is not null;

    public bool IsInRole(string roleCode) =>
        User?.RoleCode == roleCode;
}
