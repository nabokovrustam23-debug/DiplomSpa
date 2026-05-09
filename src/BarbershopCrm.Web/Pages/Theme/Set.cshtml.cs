using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BarbershopCrm.Web.Pages.Theme;

public class SetModel : PageModel
{
    private static readonly TimeSpan ThemeCookieLifetime = TimeSpan.FromDays(365);

    public IActionResult OnGet([FromQuery] string? value, [FromQuery] string? returnUrl)
    {
        var theme = value == "light" ? "light" : "dark";

        Response.Cookies.Append("theme", theme, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.Add(ThemeCookieLifetime),
            HttpOnly = false,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
            IsEssential = true
        });

        var safe = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : Url.Page("/Index") ?? "/";

        return Redirect(safe);
    }
}
