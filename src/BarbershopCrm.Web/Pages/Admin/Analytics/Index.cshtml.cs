using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Analytics;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Web.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BarbershopCrm.Web.Pages.Admin.Analytics;

[AuthorizePage(RoleCode.Admin, RoleCode.Owner)]
public class IndexModel : AppPageModel
{
    private readonly IAnalyticsService _service;

    public IndexModel(ICurrentUserAccessor cu, IAnalyticsService service) : base(cu)
    {
        _service = service;
    }

    [BindProperty(SupportsGet = true)] public string? From { get; set; }
    [BindProperty(SupportsGet = true)] public string? To { get; set; }

    public DateOnly FromValue { get; private set; }
    public DateOnly ToValue { get; private set; }
    public DashboardSnapshot? Snapshot { get; private set; }
    public string? AccessError { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Current is null) return Forbid();

        // Admin must have a BranchId. Without it the dashboard is meaningless — show a message.
        int? branchId;
        if (Current.RoleCode == RoleCode.Admin)
        {
            if (!Current.BranchId.HasValue)
            {
                AccessError = "У вашей учётной записи администратора не задан филиал. Обратитесь к владельцу.";
                return Page();
            }
            branchId = Current.BranchId.Value;
        }
        else
        {
            // Owner accessing /Admin/Analytics — redirect to richer Owner page.
            return RedirectToPage("/Owner/Analytics/Index");
        }

        (FromValue, ToValue) = ResolveRange(From, To);
        Snapshot = await _service.GetDashboardAsync(branchId, FromValue, ToValue, ct);
        return Page();
    }

    public async Task<IActionResult> OnGetExportAsync(CancellationToken ct)
    {
        if (Current is null) return Forbid();
        if (Current.RoleCode != RoleCode.Admin || !Current.BranchId.HasValue)
            return Forbid();

        (FromValue, ToValue) = ResolveRange(From, To);
        var rows = await _service.GetExportRowsAsync(Current.BranchId.Value, FromValue, ToValue, ct);
        var bytes = CsvExporter.BuildBookingsCsv(rows);
        var fileName = $"bookings_branch{Current.BranchId.Value}_{FromValue:yyyyMMdd}_{ToValue:yyyyMMdd}.csv";
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    internal static (DateOnly from, DateOnly to) ResolveRange(string? fromInput, string? toInput)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        DateOnly from = !string.IsNullOrWhiteSpace(fromInput) && DateOnly.TryParse(fromInput, out var f)
            ? f
            : today.AddDays(-29);
        DateOnly to = !string.IsNullOrWhiteSpace(toInput) && DateOnly.TryParse(toInput, out var t)
            ? t
            : today;
        if (to < from) (from, to) = (to, from);
        return (from, to);
    }
}
