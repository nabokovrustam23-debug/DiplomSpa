using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Bookings;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Infrastructure.Scheduling;
using BarbershopCrm.Web.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace BarbershopCrm.Web.Pages.BookingFlow;

[AuthorizePage(RoleCode.Client)]
public class NewModel : AppPageModel
{
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

    [BindProperty(SupportsGet = true)] public int? BranchId { get; set; }
    [BindProperty(SupportsGet = true)] public int? ServiceId { get; set; }
    [BindProperty(SupportsGet = true)] public int? MasterId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Date { get; set; }

    [BindProperty] public ConfirmInput Confirm { get; set; } = new();

    public List<Branch> Branches { get; private set; } = new();
    public List<Service> Services { get; private set; } = new();
    public List<MasterRow> Masters { get; private set; } = new();
    public List<SlotDto> Slots { get; private set; } = new();
    public DateOnly DateValue { get; private set; }

    public sealed record MasterRow(int MasterId, string FullName);

    public sealed class ConfirmInput
    {
        [Required(ErrorMessage = "Выберите филиал.")] public int BranchId { get; set; }
        [Required(ErrorMessage = "Выберите услугу.")] public int ServiceId { get; set; }
        [Required(ErrorMessage = "Выберите мастера.")] public int MasterId { get; set; }
        [Required(ErrorMessage = "Выберите время.")] public string Slot { get; set; } = string.Empty; // ISO local datetime
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Branches = await _db.Branches.AsNoTracking()
            .Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync(ct);

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
        }

        if (BranchId.HasValue && ServiceId.HasValue)
        {
            DateValue = !string.IsNullOrWhiteSpace(Date) && DateOnly.TryParse(Date, out var d)
                ? d : DateOnly.FromDateTime(DateTime.Today);
            Slots = (await _slotService.GetFreeSlotsAsync(BranchId.Value, ServiceId.Value, DateValue, MasterId, ct))
                .Where(s => s.StartDateTime > DateTime.Now)
                .ToList();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostConfirmAsync(CancellationToken ct)
    {
        if (Current is null) return Forbid();

        if (!ModelState.IsValid) return await ReturnPage(ct);
        if (!DateTime.TryParse(Confirm.Slot, out var startDt))
        {
            ModelState.AddModelError(string.Empty, "Не удалось распознать выбранный слот.");
            return await ReturnPage(ct);
        }

        var clientId = await _db.Clients.AsNoTracking()
            .Where(c => c.PersonaId == Current.PersonaId)
            .Select(c => (int?)c.ClientId)
            .FirstOrDefaultAsync(ct);
        if (clientId is null)
        {
            ModelState.AddModelError(string.Empty,
                "Профиль клиента не найден. Обратитесь в администрацию.");
            return await ReturnPage(ct);
        }

        var result = await _bookings.CreateAsync(new CreateBookingCommand(
            clientId.Value,
            Confirm.BranchId,
            Confirm.ServiceId,
            Confirm.MasterId,
            startDt,
            BookingSource.Online), ct);

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.Message ?? "Не удалось создать запись.");
            return await ReturnPage(ct);
        }

        TempData["Success"] = "Запись создана. Ждём вас!";
        return RedirectToPage("/Account/Bookings/Index");
    }

    private async Task<IActionResult> ReturnPage(CancellationToken ct)
    {
        BranchId = Confirm.BranchId == 0 ? BranchId : Confirm.BranchId;
        ServiceId = Confirm.ServiceId == 0 ? ServiceId : Confirm.ServiceId;
        MasterId = Confirm.MasterId == 0 ? MasterId : Confirm.MasterId;
        if (DateTime.TryParse(Confirm.Slot, out var dt))
            Date = DateOnly.FromDateTime(dt).ToString("yyyy-MM-dd");
        await OnGetAsync(ct);
        return Page();
    }
}
