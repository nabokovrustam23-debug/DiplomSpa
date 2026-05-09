using BarbershopCrm.Infrastructure.Auth;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BarbershopCrm.Web.Pages.Account;

public sealed class ConfirmEmailModel : PageModel
{
    private readonly IUserAuthService _auth;

    public ConfirmEmailModel(IUserAuthService auth) => _auth = auth;

    public bool IsSuccess { get; private set; }
    public string ErrorMessage { get; private set; } = "";

    public async Task OnGetAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            ErrorMessage = "Токен не указан.";
            return;
        }

        var result = await _auth.ConfirmEmailAsync(token, ct);
        switch (result)
        {
            case ConsumeTokenResult.Success:
                IsSuccess = true;
                break;
            case ConsumeTokenResult.Failure { Reason: ConsumeTokenFailureReason.AlreadyUsed }:
                IsSuccess = true; // Idempotent — already confirmed.
                break;
            case ConsumeTokenResult.Failure { Reason: ConsumeTokenFailureReason.Expired }:
                ErrorMessage = "Срок действия ссылки истёк. Запросите новое письмо.";
                break;
            default:
                ErrorMessage = "Ссылка некорректна или уже была использована.";
                break;
        }
    }
}
