using System.ComponentModel.DataAnnotations;
using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Web.Auth;
using BarbershopCrm.Web.Pages;
using BarbershopCrm.Web.Services;
using BarbershopCrm.Web.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarbershopCrm.Web.Pages.Owner.Branches;

[AuthorizePage(RoleCode.Owner)]
public class IndexModel : AppPageModel
{
    private readonly AppDbContext _db;
    private readonly IImageUploadService _images;

    public IndexModel(AppDbContext db, ICurrentUserAccessor currentUser, IImageUploadService images) : base(currentUser)
    {
        _db = db;
        _images = images;
    }

    public IList<Branch> Branches { get; private set; } = Array.Empty<Branch>();


    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadBranches(ct);
    }

    private async Task LoadBranches(CancellationToken ct)
    {
        Branches = await _db.Branches
            .OrderBy(b => b.BranchId)
            .AsNoTracking()
            .ToListAsync(ct);
    }
}
