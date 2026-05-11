using System.Globalization;
using System.Text;
using BarbershopCrm.Domain.Enums;

namespace BarbershopCrm.Infrastructure.Analytics;

/// <summary>
/// CSV-экспорт журнала записей для Excel/LibreOffice (ru-RU локаль).
/// Использует разделитель «;» (как ждёт Excel в русской локали),
/// директиву <c>sep=;</c> в первой строке (Excel её распознаёт автоматически),
/// UTF-8 с BOM (чтобы кириллица не превратилась в кракозябры),
/// даты в формате <c>dd.MM.yyyy HH:mm</c>, числа с запятой как десятичным разделителем.
/// </summary>
public static class CsvExporter
{
    private const char Separator = ';';

    private static readonly CultureInfo RuRu = CultureInfo.GetCultureInfo("ru-RU");

    private static readonly string[] Header =
    {
        "ID записи",
        "Филиал",
        "Мастер",
        "Услуга",
        "Клиент",
        "Дата и время",
        "Длительность, мин",
        "Стоимость, ₽",
        "Статус",
        "Источник",
        "Итог визита, ₽",
        "Причина отмены",
    };

    public static byte[] BuildBookingsCsv(IEnumerable<BookingExportRow> rows)
    {
        var sb = new StringBuilder();
        // Подсказка Excel: используй «;» как разделитель колонок.
        sb.Append("sep=").Append(Separator).Append("\r\n");
        sb.Append(string.Join(Separator, Header)).Append("\r\n");

        foreach (var r in rows)
        {
            var fields = new[]
            {
                r.BookingId.ToString(RuRu),
                Escape(r.BranchName),
                Escape(r.MasterName),
                Escape(r.ServiceName),
                Escape(r.ClientName),
                r.StartDateTime.ToString("dd.MM.yyyy HH:mm", RuRu),
                r.DurationMinutes.ToString(RuRu),
                r.PriceSnapshot.ToString("0.00", RuRu),
                Escape(StatusLabels.BookingStatus(r.Status)),
                Escape(StatusLabels.BookingSource(r.Source)),
                r.VisitTotalAmount?.ToString("0.00", RuRu) ?? "",
                Escape(r.CancelReason ?? ""),
            };
            sb.Append(string.Join(Separator, fields)).Append("\r\n");
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
        var needsQuote = value.IndexOfAny(new[] { Separator, '"', '\r', '\n' }) >= 0;
        var v = value.Replace("\"", "\"\"");
        return needsQuote ? $"\"{v}\"" : v;
    }
}
