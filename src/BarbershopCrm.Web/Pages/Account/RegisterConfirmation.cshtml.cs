using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BarbershopCrm.Web.Pages.Account;

public sealed class RegisterConfirmationModel : PageModel
{
    public string? Email { get; set; }

    public void OnGet(string? email)
    {
        Email = email;
    }
}
