namespace BarbershopCrm.Domain.Entities;

public class Persona
{
    public int PersonaId { get; set; }
    public string LastName { get; set; } = null!;
    public string FirstName { get; set; } = null!;
    public string? MiddleName { get; set; }
    public string Phone { get; set; } = null!;
    public string? Email { get; set; }
    public DateOnly? BirthDate { get; set; }
    public string? Gender { get; set; }

    public User? User { get; set; }
    public Master? Master { get; set; }
    public Client? Client { get; set; }

    public string FullName => string.IsNullOrWhiteSpace(MiddleName)
        ? $"{LastName} {FirstName}"
        : $"{LastName} {FirstName} {MiddleName}";

    /// <summary>Фамилия + инициалы: «Иванов И.С.» / «Иванов И.» при отсутствии отчества.</summary>
    public string ShortName
    {
        get
        {
            var last = (LastName ?? string.Empty).Trim();
            var fi = FirstInitial(FirstName);
            var mi = FirstInitial(MiddleName);

            if (string.IsNullOrEmpty(last) && string.IsNullOrEmpty(fi))
                return string.Empty;

            var initials = string.IsNullOrEmpty(mi) ? fi : $"{fi}{mi}";
            return string.IsNullOrEmpty(initials) ? last : $"{last} {initials}".Trim();
        }
    }

    private static string FirstInitial(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var ch = value.TrimStart()[0];
        return $"{char.ToUpperInvariant(ch)}.";
    }
}
