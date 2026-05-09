using System.ComponentModel.DataAnnotations;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Email;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace BarbershopCrm.Web.Pages.Account;

public sealed class ResendConfirmationEmailModel : PageModel
{
    private readonly IUserAuthService _auth;
    private readonly IEmailSender _email;
    private readonly AuthOptions _options;

    public ResendConfirmationEmailModel(IUserAuthService auth, IEmailSender email, IOptions<AuthOptions> options)
    {
        _auth = auth;
        _email = email;
        _options = options.Value;
    }

    [BindProperty(SupportsGet = true)]
    [EmailAddress(ErrorMessage = "Некорректный email.")]
    [Required(ErrorMessage = "Введите email.")]
    public string Email { get; set; } = string.Empty;

    public bool IsSent { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return Page();

        var token = await _auth.IssueEmailVerificationTokenAsync(Email, ct);
        if (token is not null)
        {
            var link = BuildConfirmationLink(token);
            var message = AccountEmails.EmailVerification(
                Email.Trim().ToLowerInvariant(),
                fullName: "клиент «Тихого часа»",
                link,
                _options.EmailVerificationTokenLifetimeHours);
            await _email.SendAsync(message, ct);
        }

        // Anti-enumeration: always show the same neutral confirmation.
        IsSent = true;
        return Page();
    }

    private string BuildConfirmationLink(string token)
    {
        var pageUrl = Url.Page("/Account/ConfirmEmail", values: new { token });
        if (!string.IsNullOrEmpty(_options.PublicBaseUrl))
        {
            return _options.PublicBaseUrl.TrimEnd('/') + pageUrl;
        }
        return $"{Request.Scheme}://{Request.Host}{pageUrl}";
    }
}
