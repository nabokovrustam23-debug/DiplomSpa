using System.ComponentModel.DataAnnotations;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Web.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace BarbershopCrm.Web.Pages.Account;

public sealed class LoginModel : PageModel
{
    private readonly IUserAuthService _auth;
    private readonly AuthOptions _options;
    private readonly IWebHostEnvironment _env;

    public LoginModel(IUserAuthService auth, IOptions<AuthOptions> options, IWebHostEnvironment env)
    {
        _auth = auth;
        _options = options.Value;
        _env = env;
    }

    [BindProperty]
    public LoginInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; set; }

    public bool ShowQuickLogin => _env.IsDevelopment();

    public IReadOnlyList<QuickLoginAccount> QuickAccounts { get; } = new[]
    {
        new QuickLoginAccount("owner@thq.ru",   "Владелец",       "доступ ко всем филиалам"),
        new QuickLoginAccount("admin1@thq.ru",  "Админ филиала 1", "Тихий час — Центр"),
        new QuickLoginAccount("admin2@thq.ru",  "Админ филиала 2", "Тихий час — Фестивальный"),
        new QuickLoginAccount("master1@thq.ru", "Мастер",         "филиал Центр, все услуги"),
        new QuickLoginAccount("client1@thq.ru", "Клиент",         "обычный клиент"),
    };

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

        return await DoLoginAsync(Input.Email, Input.Password, ct);
    }

    public async Task<IActionResult> OnPostQuickAsync(string email, CancellationToken ct)
    {
        if (!_env.IsDevelopment())
            return NotFound();

        if (string.IsNullOrWhiteSpace(email) || !QuickAccounts.Any(a => a.Email == email))
        {
            ErrorMessage = "Учётная запись не найдена.";
            return Page();
        }

        ModelState.Clear();
        return await DoLoginAsync(email, SeedDevData.TestPassword, ct);
    }

    private async Task<IActionResult> DoLoginAsync(string email, string password, CancellationToken ct)
    {
        var result = await _auth.LoginAsync(
            email,
            password,
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

    public sealed record QuickLoginAccount(string Email, string Title, string Subtitle);
}
