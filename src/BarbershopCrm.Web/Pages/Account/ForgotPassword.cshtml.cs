using System.ComponentModel.DataAnnotations;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Email;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace BarbershopCrm.Web.Pages.Account;

public sealed class ForgotPasswordModel : PageModel
{
    private readonly IUserAuthService _auth;
    private readonly IEmailSender _email;
    private readonly AuthOptions _options;

    public ForgotPasswordModel(IUserAuthService auth, IEmailSender email, IOptions<AuthOptions> options)
    {
        _auth = auth;
        _email = email;
        _options = options.Value;
    }

    [BindProperty]
    [Required(ErrorMessage = "Введите email.")]
    [EmailAddress(ErrorMessage = "Некорректный email.")]
    public string Email { get; set; } = string.Empty;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return Page();

        var token = await _auth.IssuePasswordResetTokenAsync(Email, ct);
        if (token is not null)
        {
            var link = BuildResetLink(token);
            var message = AccountEmails.PasswordReset(
                Email.Trim().ToLowerInvariant(),
                fullName: "клиент «Тихого часа»",
                link,
                _options.PasswordResetTokenLifetimeHours);
            await _email.SendAsync(message, ct);
        }

        // Anti-enumeration: always show same neutral confirmation.
        return RedirectToPage("/Account/ForgotPasswordConfirmation");
    }

    private string BuildResetLink(string token)
    {
        var pageUrl = Url.Page("/Account/ResetPassword", values: new { token });
        if (!string.IsNullOrEmpty(_options.PublicBaseUrl))
        {
            return _options.PublicBaseUrl.TrimEnd('/') + pageUrl;
        }
        return $"{Request.Scheme}://{Request.Host}{pageUrl}";
    }
}
