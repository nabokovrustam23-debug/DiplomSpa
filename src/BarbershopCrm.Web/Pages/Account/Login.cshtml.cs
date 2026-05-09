using System.ComponentModel.DataAnnotations;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Web.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace BarbershopCrm.Web.Pages.Account;

public sealed class LoginModel : PageModel
{
    private readonly IUserAuthService _auth;
    private readonly AuthOptions _options;

    public LoginModel(IUserAuthService auth, IOptions<AuthOptions> options)
    {
        _auth = auth;
        _options = options.Value;
    }

    [BindProperty]
    public LoginInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; set; }

    public IActionResult OnGet()
    {
        // Already logged in — bounce home (or to ReturnUrl).
        if (HttpContext.Items[CurrentUserAccessor.HttpContextItemKey] is CurrentUser)
        {
            return SafeRedirect(ReturnUrl);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return Page();

        var result = await _auth.LoginAsync(
            Input.Email,
            Input.Password,
            HttpContext.Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            ct);

        switch (result)
        {
            case LoginResult.Success success:
                SessionCookie.Set(HttpContext, _options, success.SessionToken, success.ExpiresAt);
                return SafeRedirect(ReturnUrl);

            case LoginResult.Failure { Reason: LoginFailureReason.AccountInactive }:
                ErrorMessage = "Аккаунт деактивирован. Обратитесь к администратору.";
                return Page();

            case LoginResult.Failure:
            default:
                ErrorMessage = "Неверный email или пароль.";
                return Page();
        }
    }

    private IActionResult SafeRedirect(string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);
        return RedirectToPage("/Index");
    }

    public sealed class LoginInput
    {
        [Required(ErrorMessage = "Введите email.")]
        [EmailAddress(ErrorMessage = "Некорректный email.")]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Введите пароль.")]
        [DataType(DataType.Password)]
        [Display(Name = "Пароль")]
        public string Password { get; set; } = string.Empty;
    }
}
