using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Web.Auth;
using Microsoft.AspNetCore.Mvc;

namespace BarbershopCrm.Web.Pages.MasterArea;

[AuthorizePage(RoleCode.Master)]
public class ScheduleModel : AppPageModel
{
    public ScheduleModel(ICurrentUserAccessor cu) : base(cu) { }

    /// <summary>
    /// Старое /Master/Schedule объединено с /Master/Bookings; страница оставлена
    /// только для совместимости со старыми ссылками и редиректит на новый таймлайн.
    /// </summary>
    public IActionResult OnGet() => RedirectToPage("/MasterArea/Bookings");
}
