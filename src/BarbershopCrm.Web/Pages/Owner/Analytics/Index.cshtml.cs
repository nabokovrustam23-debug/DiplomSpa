using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Analytics;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Web.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace BarbershopCrm.Web.Pages.Owner.Analytics;

[AuthorizePage(RoleCode.Owner)]
public class IndexModel : AppPageModel
{
    private readonly IAnalyticsService _service;
    private readonly AppDbContext _db;

    public IndexModel(ICurrentUserAccessor cu, IAnalyticsService service, AppDbContext db) : base(cu)
    {
        _service = service;
        _db = db;
    }

    [BindProperty(SupportsGet = true)] public string? From { get; set; }
    [BindProperty(SupportsGet = true)] public string? To { get; set; }
    [BindProperty(SupportsGet = true)] public int? BranchId { get; set; }

    public DateOnly FromValue { get; private set; }
    public DateOnly ToValue { get; private set; }
    public DashboardSnapshot? Snapshot { get; private set; }
    public List<SelectListItem> BranchOptions { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Current is null) return Forbid();

        BranchOptions = await _db.Branches.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .Select(b => new SelectListItem(b.Name, b.BranchId.ToString()))
            .ToListAsync(ct);

        (FromValue, ToValue) = BarbershopCrm.Web.Pages.Admin.Analytics.IndexModel.ResolveRange(From, To);
        Snapshot = await _service.GetDashboardAsync(BranchId, FromValue, ToValue, ct);
        return Page();
    }

    public async Task<IActionResult> OnGetExportAsync(CancellationToken ct)
    {
        if (Current is null) return Forbid();
        (FromValue, ToValue) = BarbershopCrm.Web.Pages.Admin.Analytics.IndexModel.ResolveRange(From, To);
        var rows = await _service.GetExportRowsAsync(BranchId, FromValue, ToValue, ct);
        var bytes = CsvExporter.BuildBookingsCsv(rows);
        var scope = BranchId.HasValue ? $"branch{BranchId.Value}" : "all";
        var fileName = $"bookings_{scope}_{FromValue:yyyyMMdd}_{ToValue:yyyyMMdd}.csv";
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }
}
