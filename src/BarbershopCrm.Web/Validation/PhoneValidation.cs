namespace BarbershopCrm.Web.Validation;

/// <summary>
/// Russian phone format helpers shared across registration / master / branch forms.
/// Accepts +7 or 8 prefix followed by exactly 10 more digits, separators allowed:
/// space, dash, parentheses. Examples: +7 (861) 200-10-10, 89180000020, +79180000020.
/// </summary>
public static class PhoneValidation
{
    public const string RussianPhonePattern =
        @"^\+?[78][\s\-\(\)]*(\d[\s\-\(\)]*){10}$";

    public const string ErrorMessage =
        "Некорректный телефон. Формат: +7 (XXX) XXX-XX-XX или 8XXXXXXXXXX (всего 11 цифр).";
}
