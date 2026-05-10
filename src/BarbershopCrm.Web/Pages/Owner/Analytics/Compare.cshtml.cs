using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Analytics;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Web.Auth;
using Microsoft.AspNetCore.Mvc;

namespace BarbershopCrm.Web.Pages.Owner.Analytics;

[AuthorizePage(RoleCode.Owner)]
public class CompareModel : AppPageModel
{
    private readonly IAnalyticsService _service;

    public CompareModel(ICurrentUserAccessor cu, IAnalyticsService service) : base(cu)
    {
        _service = service;
    }

    [BindProperty(SupportsGet = true)] public string? From { get; set; }
    [BindProperty(SupportsGet = true)] public string? To { get; set; }

    public DateOnly FromValue { get; private set; }
    public DateOnly ToValue { get; private set; }
    public IReadOnlyList<BranchCompareRow> Rows { get; private set; } = Array.Empty<BranchCompareRow>();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Current is null) return Forbid();
        (FromValue, ToValue) = BarbershopCrm.Web.Pages.Admin.Analytics.IndexModel.ResolveRange(From, To);
        Rows = await _service.GetBranchComparisonAsync(FromValue, ToValue, ct);
        return Page();
    }
}
