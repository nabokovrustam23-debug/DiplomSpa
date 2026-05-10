namespace BarbershopCrm.Infrastructure.Analytics;

/// <summary>
/// Aggregated KPIs for a single branch (or the whole network if BranchId is null)
/// over the closed-open interval [From, To+1d).
/// </summary>
public sealed record DashboardSnapshot(
    DateOnly From,
    DateOnly To,
    int? BranchId,
    string? BranchName,
    BookingsByStatus ByStatus,
    int TotalBookings,
    decimal Revenue,
    decimal AverageTicket,
    int CompletedVisits,
    int RepeatClients,
    int ClientsWithAtLeastOneCompletedVisit,
    decimal RepeatClientsRate,
    IReadOnlyList<MasterUtilizationRow> Utilization,
    IReadOnlyList<TopServiceRow> TopServices);

public sealed record BookingsByStatus(
    int Created,
    int Confirmed,
    int Completed,
    int Cancelled,
    int NoShow)
{
    public int Total => Created + Confirmed + Completed + Cancelled + NoShow;
}

public sealed record MasterUtilizationRow(
    int MasterId,
    string MasterName,
    int BookedMinutes,
    int WorkMinutes,
    decimal UtilizationPercent);

public sealed record TopServiceRow(
    int ServiceId,
    string ServiceName,
    int Count,
    decimal Revenue);

public sealed record BranchCompareRow(
    int BranchId,
    string BranchName,
    int TotalBookings,
    int CompletedBookings,
    decimal Revenue,
    decimal AverageTicket,
    decimal AverageUtilizationPercent,
    decimal CancelRate,
    decimal NoShowRate);

/// <summary>
/// Flat row used for CSV export. One row per booking.
/// </summary>
public sealed record BookingExportRow(
    int BookingId,
    string BranchName,
    string MasterName,
    string ServiceName,
    string ClientName,
    DateTime StartDateTime,
    int DurationMinutes,
    decimal PriceSnapshot,
    string Status,
    string Source,
    decimal? VisitTotalAmount,
    string? CancelReason);
