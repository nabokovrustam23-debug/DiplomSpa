using System.ComponentModel.DataAnnotations;
using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Domain.Enums;
using BarbershopCrm.Infrastructure.Auth;
using BarbershopCrm.Infrastructure.Data;
using BarbershopCrm.Web.Auth;
using BarbershopCrm.Web.Pages;
using BarbershopCrm.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BarbershopCrm.Web.Pages.Owner.Services;

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

    public IList<BarbershopCrm.Domain.Entities.Service> Services { get; private set; } = Array.Empty<BarbershopCrm.Domain.Entities.Service>();


    public async Task OnGetAsync(CancellationToken ct)
    {
        await LoadServices(ct);
    }

    private async Task LoadServices(CancellationToken ct)
    {
        Services = await _db.Services
            .OrderBy(s => s.ServiceId)
            .AsNoTracking()
            .ToListAsync(ct);
    }
}
