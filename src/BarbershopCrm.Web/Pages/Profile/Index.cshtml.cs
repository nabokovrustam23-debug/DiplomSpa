using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Web.Auth;

namespace BarbershopCrm.Web.Pages.Profile;

[AuthorizePage]
public sealed class IndexModel : AppPageModel
{
    public IndexModel(ICurrentUserAccessor currentUser) : base(currentUser) { }

    public string RoleLabel => Current?.RoleCode switch
    {
        RoleCode.Owner  => "Владелец сети",
        RoleCode.Admin  => "Администратор филиала",
        RoleCode.Master => "Мастер",
        RoleCode.Client => "Клиент",
        _ => Current?.RoleCode ?? string.Empty,
    };

    public void OnGet() { }
}
