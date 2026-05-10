using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BarbershopCrm.Web.Pages;

public class IndexModel : AppPageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db, ICurrentUserAccessor cu) : base(cu)
    {
        _db = db;
    }

    public IList<Branch> Branches { get; private set; } = Array.Empty<Branch>();
    public IList<Service> Services { get; private set; } = Array.Empty<Service>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Branches = await _db.Branches
            .Where(b => b.IsActive)
            .OrderBy(b => b.BranchId)
            .AsNoTracking()
            .ToListAsync(ct);

        Services = await _db.Services
            .Where(s => s.IsActive)
            .OrderBy(s => s.ServiceId)
            .AsNoTracking()
            .ToListAsync(ct);
    }
}
