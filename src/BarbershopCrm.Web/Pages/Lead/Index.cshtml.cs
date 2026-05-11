using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Infrastructure.Notifications;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace BarbershopCrm.Web.Pages.Lead;

public class IndexModel : AppPageModel
{
    private static readonly Regex PhoneRegex = new(@"^\+?[78][\s\-\(\)]*(\d[\s\-\(\)]*){10}$", RegexOptions.Compiled);
    private readonly AppDbContext _db;
    private readonly INotificationService _notifications;

    public IndexModel(ICurrentUserAccessor cu, AppDbContext db, INotificationService notifications) : base(cu)
    {
        _db = db;
        _notifications = notifications;
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    public List<SelectListItem> Branches { get; private set; } = new();

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Укажите имя.")]
        [StringLength(120, ErrorMessage = "Имя слишком длинное (макс. 120 символов).")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Укажите телефон.")]
        [StringLength(20, ErrorMessage = "Слишком длинный номер.")]
        public string Phone { get; set; } = string.Empty;

        [Required(ErrorMessage = "Выберите филиал.")]
        public int? PreferredBranchId { get; set; }

        [StringLength(500, ErrorMessage = "Комментарий слишком длинный (макс. 500 символов).")]
        public string? Comment { get; set; }

        public bool ConsentGiven { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);

        if (IsAuthenticated && Current is not null)
        {
            var p = await _db.Persona.AsNoTracking()
                .FirstOrDefaultAsync(x => x.PersonaId == Current.PersonaId, ct);
            if (p is not null)
            {
                Input.Name = p.LastName + " " + p.FirstName;
                Input.Phone = p.Phone;
            }
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!PhoneRegex.IsMatch(Input.Phone ?? string.Empty))
            ModelState.AddModelError(nameof(Input.Phone), "Телефон должен содержать 10–11 цифр в формате +7 или 8.");
        if (!Input.ConsentGiven)
            ModelState.AddModelError(nameof(Input.ConsentGiven), "Необходимо подтвердить согласие на обработку персональных данных.");

        if (!ModelState.IsValid)
        {
            await LoadAsync(ct);
            return Page();
        }

        var lead = new Domain.Entities.Lead
        {
            PersonaId = Current?.PersonaId,
            RawName = Input.Name.Trim(),
            RawPhone = NormalizePhone(Input.Phone ?? string.Empty),
            PreferredBranchId = Input.PreferredBranchId,
            Comment = string.IsNullOrWhiteSpace(Input.Comment) ? null : Input.Comment.Trim(),
            Status = LeadStatus.New,
            CreatedAt = DateTime.UtcNow,
        };
        _db.Leads.Add(lead);

        if (Current is not null)
        {
            _db.ConsentLog.Add(new ConsentLogEntry
            {
                PersonaId = Current.PersonaId,
                ConsentType = "PersonalData",
                AcceptedAt = DateTime.UtcNow,
            });
        }
        await _db.SaveChangesAsync(ct);

        await _notifications.OnLeadCreatedAsync(lead.LeadId, ct);

        TempData["Success"] = "Заявка принята. Мы свяжемся с вами в ближайшее время.";
        return RedirectToPage("/Lead/Thanks");
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Branches = await _db.Branches.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .Select(b => new SelectListItem(b.Name, b.BranchId.ToString()))
            .ToListAsync(ct);
    }

    private static string NormalizePhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 11 && digits.StartsWith("8")) digits = "7" + digits.Substring(1);
        if (digits.Length == 10) digits = "7" + digits;
        return "+" + digits;
    }
}
