using System.ComponentModel.DataAnnotations;
using BarbershopCrm.Infrastructure.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BarbershopCrm.Web.Pages.Account;

public sealed class ResetPasswordModel : PageModel
{
    private readonly IUserAuthService _auth;

    public ResetPasswordModel(IUserAuthService auth) => _auth = auth;

    [BindProperty(SupportsGet = true)]
    public string Token { get; set; } = string.Empty;

    [BindProperty]
    public ResetInput Input { get; set; } = new();

    public bool IsLinkValid { get; private set; } = true;
    public string? ErrorMessage { get; private set; }

    public void OnGet()
    {
        if (string.IsNullOrWhiteSpace(Token))
        {
            IsLinkValid = false;
            ErrorMessage = "Ссылка не содержит токена.";
        }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Token))
        {
            IsLinkValid = false;
            ErrorMessage = "Ссылка не содержит токена.";
            return Page();
        }

        if (!ModelState.IsValid)
            return Page();

        if (Input.Password != Input.PasswordRepeat)
        {
            ModelState.AddModelError($"Input.{nameof(Input.PasswordRepeat)}", "Пароли не совпадают.");
            return Page();
        }

        var result = await _auth.ResetPasswordAsync(Token, Input.Password, ct);
        return result switch
        {
            ConsumeTokenResult.Success
                => RedirectToPage("/Account/ResetPasswordConfirmation"),

            ConsumeTokenResult.Failure { Reason: ConsumeTokenFailureReason.Expired }
                => InvalidLink("Срок действия ссылки истёк. Запросите новую."),

            ConsumeTokenResult.Failure { Reason: ConsumeTokenFailureReason.AlreadyUsed }
                => InvalidLink("Эта ссылка уже была использована."),

            _ => InvalidLink("Ссылка некорректна. Запросите новую."),
        };
    }

    private IActionResult InvalidLink(string message)
    {
        IsLinkValid = false;
        ErrorMessage = message;
        return Page();
    }

    public sealed class ResetInput
    {
        [Required(ErrorMessage = "Введите пароль.")]
        [StringLength(200, MinimumLength = 8, ErrorMessage = "Пароль должен быть не короче 8 символов.")]
        [DataType(DataType.Password)]
        [Display(Name = "Новый пароль")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Повторите пароль.")]
        [DataType(DataType.Password)]
        [Display(Name = "Повторите пароль")]
        public string PasswordRepeat { get; set; } = string.Empty;
    }
}
