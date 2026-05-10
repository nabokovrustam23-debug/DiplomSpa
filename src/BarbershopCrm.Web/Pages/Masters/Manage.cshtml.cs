using System.ComponentModel.DataAnnotations;
using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Web.Auth;
using BarbershopCrm.Web.Pages;
using BarbershopCrm.Web.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarbershopCrm.Web.Pages.Masters;

[AuthorizePage(RoleCode.Admin, RoleCode.Owner)]
public class ManageModel : AppPageModel
{
    private const string DefaultPosition = "Барбер";

    private readonly AppDbContext _db;

    public ManageModel(AppDbContext db, ICurrentUserAccessor currentUser) : base(currentUser)
    {
        _db = db;
    }

    public IList<Master> Masters { get; private set; } = Array.Empty<Master>();
    public IList<Branch> Branches { get; private set; } = Array.Empty<Branch>();
    public IList<Service> AllServices { get; private set; } = Array.Empty<Service>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadData(ct);
    }

    private async Task LoadData(CancellationToken ct)
    {
        var query = _db.Masters
            .Include(m => m.Persona)
            .Include(m => m.Branch)
            .Include(m => m.MasterServices).ThenInclude(ms => ms.Service)
            .AsNoTracking();

        if (Current?.RoleCode == RoleCode.Admin && Current.BranchId.HasValue)
        {
            query = query.Where(m => m.BranchId == Current.BranchId.Value);
        }

        Masters = await query.OrderBy(m => m.Branch.Name).ThenBy(m => m.Persona.LastName).ToListAsync(ct);

        Branches = await _db.Branches
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .AsNoTracking()
            .ToListAsync(ct);

        AllServices = await _db.Services
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .AsNoTracking()
            .ToListAsync(ct);
    }
}
