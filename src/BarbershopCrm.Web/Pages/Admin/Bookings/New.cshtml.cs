using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Bookings;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Infrastructure.Scheduling;
using BarbershopCrm.Web.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace BarbershopCrm.Web.Pages.Admin.Bookings;

[AuthorizePage(RoleCode.Admin, RoleCode.Owner)]
public class NewModel : AppPageModel
{
    private static readonly Regex PhoneRegex = new(@"^\+?[78][\s\-\(\)]*(\d[\s\-\(\)]*){10}$", RegexOptions.Compiled);

    private readonly AppDbContext _db;
    private readonly ISlotService _slotService;
    private readonly IBookingService _bookings;

    public NewModel(ICurrentUserAccessor cu, AppDbContext db, ISlotService slotService, IBookingService bookings)
        : base(cu)
    {
        _db = db;
        _slotService = slotService;
        _bookings = bookings;
    }

    // --- Wizard filter state (preserved across GET reloads) -----------------
    [BindProperty(SupportsGet = true)] public int? BranchId { get; set; }
    [BindProperty(SupportsGet = true)] public int? ServiceId { get; set; }
    [BindProperty(SupportsGet = true)] public int? MasterId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Date { get; set; }
    [BindProperty(SupportsGet = true)] public int? LeadId { get; set; }

    [BindProperty] public ConfirmInput Confirm { get; set; } = new();

    public List<Branch> Branches { get; private set; } = new();
    public List<Service> Services { get; private set; } = new();
    public List<MasterRow> Masters { get; private set; } = new();
    public List<SlotDto> Slots { get; private set; } = new();
    public DateOnly DateValue { get; private set; }
    public string? LeadHint { get; private set; }

    public sealed record MasterRow(int MasterId, string FullName);

    public sealed class ConfirmInput
    {
        [Required(ErrorMessage = "Выберите филиал.")] public int BranchId { get; set; }
        [Required(ErrorMessage = "Выберите услугу.")] public int ServiceId { get; set; }
        [Required(ErrorMessage = "Выберите мастера.")] public int MasterId { get; set; }

        [Required(ErrorMessage = "Выберите время.")]
        public string Slot { get; set; } = string.Empty; // ISO local datetime

        [Required(ErrorMessage = "Укажите фамилию.")]
        [StringLength(80, ErrorMessage = "Фамилия слишком длинная (макс. 80 символов).")]
        public string LastName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Укажите имя.")]
        [StringLength(80, ErrorMessage = "Имя слишком длинное (макс. 80 символов).")]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Укажите телефон.")]
        [StringLength(20, ErrorMessage = "Слишком длинный номер.")]
        public string Phone { get; set; } = string.Empty;

        [EmailAddress(ErrorMessage = "Некорректный email.")]
        [StringLength(120, ErrorMessage = "Email слишком длинный (макс. 120 символов).")]
        public string? Email { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Current is null) return Forbid();

        // Admin always works in own branch — preselect & lock.
        if (Current.RoleCode == RoleCode.Admin && Current.BranchId.HasValue)
            BranchId = Current.BranchId.Value;

        if (LeadId.HasValue)
        {
            var lead = await _db.Leads.AsNoTracking()
                .FirstOrDefaultAsync(l => l.LeadId == LeadId.Value, ct);
            if (lead is not null)
            {
                if (Current.RoleCode == RoleCode.Admin && Current.BranchId.HasValue
                    && lead.PreferredBranchId.HasValue
                    && lead.PreferredBranchId != Current.BranchId)
                    return Forbid();

                var parts = (lead.RawName ?? string.Empty).Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    Confirm.LastName = parts[0];
                    Confirm.FirstName = parts[1];
                }
                else if (parts.Length == 1)
                {
                    Confirm.FirstName = parts[0];
                }
                Confirm.Phone = lead.RawPhone ?? string.Empty;
                if (!BranchId.HasValue && lead.PreferredBranchId.HasValue)
                    BranchId = lead.PreferredBranchId.Value;

                LeadHint = $"Заявка #{lead.LeadId} от {lead.CreatedAt:dd.MM.yyyy HH:mm} — {lead.RawName}, {lead.RawPhone}.";
            }
        }

        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (Current is null) return Forbid();

        // Admin always works in own branch.
        if (Current.RoleCode == RoleCode.Admin && Current.BranchId.HasValue)
            Confirm.BranchId = Current.BranchId.Value;

        if (!PhoneRegex.IsMatch(Confirm.Phone ?? string.Empty))
            ModelState.AddModelError(nameof(Confirm.Phone), "Телефон должен содержать 10–11 цифр в формате +7 или 8.");

        if (Current.RoleCode == RoleCode.Admin && Current.BranchId.HasValue
            && Confirm.BranchId != Current.BranchId)
            ModelState.AddModelError(nameof(Confirm.BranchId), "Можно создавать запись только в своём филиале.");

        if (!ModelState.IsValid)
            return await ReturnPage(ct);

        if (!DateTime.TryParse(Confirm.Slot, out var startDt))
        {
            ModelState.AddModelError(nameof(Confirm.Slot), "Не удалось распознать выбранный слот.");
            return await ReturnPage(ct);
        }
        startDt = RoundToMinute(startDt);

        var phoneNorm = NormalizePhone(Confirm.Phone ?? string.Empty);
        var persona = await _db.Persona.FirstOrDefaultAsync(p => p.Phone == phoneNorm, ct);
        if (persona is null)
        {
            persona = new Persona
            {
                LastName = Confirm.LastName.Trim(),
                FirstName = Confirm.FirstName.Trim(),
                Phone = phoneNorm,
                Email = string.IsNullOrWhiteSpace(Confirm.Email) ? null : Confirm.Email.Trim(),
            };
            _db.Persona.Add(persona);
            await _db.SaveChangesAsync(ct);
        }

        var client = await _db.Clients.FirstOrDefaultAsync(c => c.PersonaId == persona.PersonaId, ct);
        if (client is null)
        {
            client = new Client { PersonaId = persona.PersonaId, Source = "Admin" };
            _db.Clients.Add(client);
            await _db.SaveChangesAsync(ct);
        }

        var result = await _bookings.CreateAsync(new CreateBookingCommand(
            client.ClientId, Confirm.BranchId, Confirm.ServiceId, Confirm.MasterId, startDt, BookingSource.Admin), ct);

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Message ?? "Не удалось создать запись.");
            return await ReturnPage(ct);
        }

        if (LeadId.HasValue)
        {
            var lead = await _db.Leads.FirstOrDefaultAsync(l => l.LeadId == LeadId.Value, ct);
            if (lead is not null && lead.Status != LeadStatus.Done && lead.Status != LeadStatus.Rejected)
            {
                lead.Status = LeadStatus.Done;
                lead.ProcessedByUserId = Current.UserId;
                lead.ProcessedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
        }

        TempData["Success"] = LeadId.HasValue
            ? "Запись создана, заявка закрыта."
            : "Запись создана.";
        return RedirectToPage("/Admin/Bookings/Index", new { BranchId = Confirm.BranchId });
    }

    private async Task<IActionResult> ReturnPage(CancellationToken ct)
    {
        // Echo the filters from POST back into the GET state so the wizard re-renders correctly.
        if (Confirm.BranchId != 0) BranchId = Confirm.BranchId;
        if (Confirm.ServiceId != 0) ServiceId = Confirm.ServiceId;
        if (Confirm.MasterId != 0) MasterId = Confirm.MasterId;
        if (DateTime.TryParse(Confirm.Slot, out var dt))
            Date = DateOnly.FromDateTime(dt).ToString("yyyy-MM-dd");

        await LoadAsync(ct);
        return Page();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        var branchesQ = _db.Branches.AsNoTracking().Where(b => b.IsActive);
        if (Current!.RoleCode == RoleCode.Admin && Current.BranchId.HasValue)
            branchesQ = branchesQ.Where(b => b.BranchId == Current.BranchId.Value);

        Branches = await branchesQ.OrderBy(b => b.Name).ToListAsync(ct);

        if (BranchId.HasValue)
        {
            Services = await _db.Services.AsNoTracking()
                .Where(s => s.IsActive
                    && _db.MasterServices.Any(ms => ms.ServiceId == s.ServiceId
                        && _db.Masters.Any(m => m.MasterId == ms.MasterId
                            && m.IsActive && m.BranchId == BranchId.Value)))
                .OrderBy(s => s.Name).ToListAsync(ct);
        }

        if (BranchId.HasValue && ServiceId.HasValue)
        {
            Masters = await _db.Masters.AsNoTracking()
                .Where(m => m.IsActive && m.BranchId == BranchId
                    && m.MasterServices.Any(ms => ms.ServiceId == ServiceId))
                .Include(m => m.Persona)
                .OrderBy(m => m.Persona.LastName)
                .Select(m => new MasterRow(m.MasterId, m.Persona.LastName + " " + m.Persona.FirstName))
                .ToListAsync(ct);

            DateValue = !string.IsNullOrWhiteSpace(Date) && DateOnly.TryParse(Date, out var d)
                ? d : DateOnly.FromDateTime(DateTime.Today);

            Slots = (await _slotService.GetFreeSlotsAsync(BranchId.Value, ServiceId.Value, DateValue, MasterId, ct))
                .Where(s => s.StartDateTime > DateTime.Now.AddMinutes(-30)) // admin может бронировать «прямо сейчас»
                .ToList();
        }
    }

    private static DateTime RoundToMinute(DateTime dt) =>
        new(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0, dt.Kind);

    private static string NormalizePhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 11 && digits.StartsWith("8")) digits = "7" + digits.Substring(1);
        if (digits.Length == 10) digits = "7" + digits;
        return "+" + digits;
    }
}
