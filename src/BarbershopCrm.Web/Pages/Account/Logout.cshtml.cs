using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Web.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace BarbershopCrm.Web.Pages.Account;

public sealed class LogoutModel : PageModel
{
    private readonly IUserAuthService _auth;
    private readonly AuthOptions _options;

    public LogoutModel(IUserAuthService auth, IOptions<AuthOptions> options)
    {
        _auth = auth;
        _options = options.Value;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var token = HttpContext.Request.Cookies[_options.SessionCookieName];
        if (!string.IsNullOrEmpty(token))
            await _auth.LogoutAsync(token, ct);

        SessionCookie.Clear(HttpContext, _options);
        return RedirectToPage("/Index");
    }
}
