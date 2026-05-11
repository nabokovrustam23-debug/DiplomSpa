namespace BarbershopCrm.Domain.Enums;

/// <summary>
/// Русские названия для технических enum-кодов из <see cref="BookingStatus"/>,
/// <see cref="LeadStatus"/> и источников записей. UI всегда показывает
/// эти метки; код-значения хранятся в БД на латинице.
/// </summary>
public static class StatusLabels
{
    public static string BookingStatus(string code) => code switch
    {
        Enums.BookingStatus.Created   => "Новая",
        Enums.BookingStatus.Confirmed => "Подтверждена",
        Enums.BookingStatus.Cancelled => "Отменена",
        Enums.BookingStatus.Completed => "Завершена",
        Enums.BookingStatus.NoShow    => "Не пришёл",
        _ => code,
    };

    public static string LeadStatus(string code) => code switch
    {
        Enums.LeadStatus.New        => "Новая",
        Enums.LeadStatus.InProgress => "В работе",
        Enums.LeadStatus.Done       => "Готово",
        Enums.LeadStatus.Rejected   => "Отклонена",
        _ => code,
    };

    public static string BookingSource(string code) => code switch
    {
        "Online" => "Онлайн-запись",
        "Admin"  => "Администратор",
        "Lead"   => "Из заявки",
        _ => code,
    };
}
