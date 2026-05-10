using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Bookings;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Web.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace BarbershopCrm.Web.Pages.Admin.Bookings;

[AuthorizePage(RoleCode.Admin, RoleCode.Owner)]
public class NewModel : AppPageModel
{
    private static readonly Regex PhoneRegex = new(@"^\+?[78][\s\-\(\)]*(\d[\s\-\(\)]*){10}$", RegexOptions.Compiled);

    private readonly AppDbContext _db;
    private readonly IBookingService _bookings;

    public NewModel(ICurrentUserAccessor cu, AppDbContext db, IBookingService bookings) : base(cu)
    {
        _db = db;
        _bookings = bookings;
    }

    [BindProperty] public InputModel Input { get; set; } = new();

    public List<SelectListItem> Branches { get; private set; } = new();
    public List<SelectListItem> Services { get; private set; } = new();
    public List<SelectListItem> Masters { get; private set; } = new();

    public sealed class InputModel
    {
        [Required] public int BranchId { get; set; }
        [Required] public int ServiceId { get; set; }
        [Required] public int MasterId { get; set; }
        [Required] public DateTime StartDateTime { get; set; } = DateTime.Now.AddHours(1);
        [Required, StringLength(80)] public string LastName { get; set; } = string.Empty;
        [Required, StringLength(80)] public string FirstName { get; set; } = string.Empty;
        [Required, StringLength(20)] public string Phone { get; set; } = string.Empty;
        [EmailAddress, StringLength(120)] public string? Email { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        if (Current is null) return Forbid();
        await LoadOptionsAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (Current is null) return Forbid();

        if (!PhoneRegex.IsMatch(Input.Phone ?? string.Empty))
            ModelState.AddModelError(nameof(Input.Phone), "Телефон должен содержать 10–11 цифр в формате +7 или 8.");

        if (Current.RoleCode == RoleCode.Admin && Current.BranchId.HasValue && Input.BranchId != Current.BranchId)
            ModelState.AddModelError(nameof(Input.BranchId), "Можно создавать запись только в своём филиале.");

        if (!ModelState.IsValid)
        {
            await LoadOptionsAsync(ct);
            return Page();
        }

        // Find or create persona by phone, then client.
        var phoneNorm = NormalizePhone(Input.Phone ?? string.Empty);
        var persona = await _db.Persona.FirstOrDefaultAsync(p => p.Phone == phoneNorm, ct);
        if (persona is null)
        {
            persona = new Persona
            {
                LastName = Input.LastName.Trim(),
                FirstName = Input.FirstName.Trim(),
                Phone = phoneNorm,
                Email = string.IsNullOrWhiteSpace(Input.Email) ? null : Input.Email.Trim(),
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

        var startUtc = DateTime.SpecifyKind(Input.StartDateTime, DateTimeKind.Local).ToUniversalTime();
        // Project stores DateTime as local (no timezone tracking). Use the value as-is.
        var startDt = Input.StartDateTime;

        var result = await _bookings.CreateAsync(new CreateBookingCommand(
            client.ClientId, Input.BranchId, Input.ServiceId, Input.MasterId, startDt, BookingSource.Admin), ct);

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Message ?? "Не удалось создать запись.");
            await LoadOptionsAsync(ct);
            return Page();
        }

        TempData["Success"] = "Запись создана.";
        return RedirectToPage("/Admin/Bookings/Index", new { BranchId = Input.BranchId });
    }

    private async Task LoadOptionsAsync(CancellationToken ct)
    {
        var branchesQ = _db.Branches.AsNoTracking().Where(b => b.IsActive);
        if (Current!.RoleCode == RoleCode.Admin && Current.BranchId.HasValue)
        {
            branchesQ = branchesQ.Where(b => b.BranchId == Current.BranchId.Value);
            if (Input.BranchId == 0) Input.BranchId = Current.BranchId.Value;
        }

        Branches = await branchesQ.OrderBy(b => b.Name)
            .Select(b => new SelectListItem(b.Name, b.BranchId.ToString()))
            .ToListAsync(ct);

        Services = await _db.Services.AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .Select(s => new SelectListItem($"{s.Name} ({s.DurationMinutes} мин · {s.Price:0} ₽)", s.ServiceId.ToString()))
            .ToListAsync(ct);

        if (Input.BranchId > 0)
        {
            Masters = await _db.Masters.AsNoTracking()
                .Where(m => m.IsActive && m.BranchId == Input.BranchId
                    && (Input.ServiceId == 0 || m.MasterServices.Any(ms => ms.ServiceId == Input.ServiceId)))
                .Include(m => m.Persona)
                .OrderBy(m => m.Persona.LastName)
                .Select(m => new SelectListItem(m.Persona.LastName + " " + m.Persona.FirstName, m.MasterId.ToString()))
                .ToListAsync(ct);
        }
    }

    private static string NormalizePhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 11 && digits.StartsWith("8")) digits = "7" + digits.Substring(1);
        if (digits.Length == 10) digits = "7" + digits;
        return "+" + digits;
    }
}
