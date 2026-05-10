using System.Globalization;
using System.Text;

namespace BarbershopCrm.Infrastructure.Analytics;

/// <summary>
/// Pure CSV serialization helpers for analytics export. RFC 4180-style escaping,
/// UTF-8 with BOM (so Excel opens Cyrillic correctly).
/// </summary>
public static class CsvExporter
{
    private static readonly string[] Header =
    {
        "BookingId", "Branch", "Master", "Service", "Client",
        "StartDateTime", "DurationMinutes", "PriceSnapshot",
        "Status", "Source", "VisitTotalAmount", "CancelReason",
    };

    public static byte[] BuildBookingsCsv(IEnumerable<BookingExportRow> rows)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(',', Header)).Append("\r\n");
        foreach (var r in rows)
        {
            var fields = new[]
            {
                r.BookingId.ToString(CultureInfo.InvariantCulture),
                Escape(r.BranchName),
                Escape(r.MasterName),
                Escape(r.ServiceName),
                Escape(r.ClientName),
                r.StartDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                r.DurationMinutes.ToString(CultureInfo.InvariantCulture),
                r.PriceSnapshot.ToString("0.00", CultureInfo.InvariantCulture),
                Escape(r.Status),
                Escape(r.Source),
                r.VisitTotalAmount?.ToString("0.00", CultureInfo.InvariantCulture) ?? "",
                Escape(r.CancelReason ?? ""),
            };
            sb.Append(string.Join(',', fields)).Append("\r\n");
        }

        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        var result = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, result, preamble.Length, body.Length);
        return result;
    }

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var needsQuote = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
        var v = value.Replace("\"", "\"\"");
        return needsQuote ? $"\"{v}\"" : v;
    }
}
