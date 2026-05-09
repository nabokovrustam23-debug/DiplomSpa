using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BarbershopCrm.Web.Pages.Branches;

public class DetailsModel : PageModel
{
    private readonly AppDbContext _db;

    public DetailsModel(AppDbContext db) => _db = db;

    public Branch Branch { get; private set; } = null!;
    public IList<Master> Masters { get; private set; } = Array.Empty<Master>();

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
    {
        var branch = await _db.Branches
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.BranchId == id && b.IsActive, ct);

        if (branch is null)
            return NotFound();

        Branch = branch;

        Masters = await _db.Masters
            .Include(m => m.Persona)
            .Include(m => m.MasterServices).ThenInclude(ms => ms.Service)
            .Where(m => m.BranchId == id && m.IsActive)
            .OrderBy(m => m.Persona.LastName)
            .AsNoTracking()
            .ToListAsync(ct);

        return Page();
    }
}
