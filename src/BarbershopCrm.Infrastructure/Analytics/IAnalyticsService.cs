namespace BarbershopCrm.Infrastructure.Analytics;

public interface IAnalyticsService
{
    /// <summary>
    /// Aggregate KPIs for a branch (or the whole network if branchId is null) over [from, to].
    /// The interval is inclusive on both ends; internally translated to [from, to+1day) on StartDateTime.
    /// </summary>
    Task<DashboardSnapshot> GetDashboardAsync(int? branchId, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// Comparison row per active branch over [from, to]. Used by /Owner/Analytics/Compare.
    /// </summary>
    Task<IReadOnlyList<BranchCompareRow>> GetBranchComparisonAsync(DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// Per-booking flat rows for CSV export over [from, to], optionally restricted to a single branch.
    /// </summary>
    Task<IReadOnlyList<BookingExportRow>> GetExportRowsAsync(int? branchId, DateOnly from, DateOnly to, CancellationToken ct = default);
}
