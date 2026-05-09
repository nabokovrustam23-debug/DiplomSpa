using BarbershopCrm.Infrastructure.Auth;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BarbershopCrm.Web.Pages;

/// <summary>
/// Convenience base for pages that want easy access to the current user.
/// Pages that require authentication should additionally be decorated with
/// <c>[AuthorizePage]</c> (or any role-restricted variant).
/// </summary>
public abstract class AppPageModel : PageModel
{
    protected ICurrentUserAccessor CurrentUserAccessor { get; }

    protected AppPageModel(ICurrentUserAccessor currentUser)
    {
        CurrentUserAccessor = currentUser;
    }

    public CurrentUser? Current => CurrentUserAccessor.User;
    public bool IsAuthenticated => CurrentUserAccessor.IsAuthenticated;
}
