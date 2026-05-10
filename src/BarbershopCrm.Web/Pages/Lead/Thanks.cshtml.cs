using BarbershopCrm.Infrastructure.Auth;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BarbershopCrm.Web.Pages.Lead;

public class ThanksModel : AppPageModel
{
    public ThanksModel(ICurrentUserAccessor cu) : base(cu) { }
}
