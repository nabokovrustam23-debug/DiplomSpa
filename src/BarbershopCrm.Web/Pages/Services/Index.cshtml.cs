using BarbershopCrm.Domain.Entities;
using BarbershopCrm.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BarbershopCrm.Web.Pages.Services;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? Category { get; set; }
    [BindProperty(SupportsGet = true)] public decimal? MinPrice { get; set; }
    [BindProperty(SupportsGet = true)] public decimal? MaxPrice { get; set; }
    [BindProperty(SupportsGet = true)] public int? MaxDuration { get; set; }
    [BindProperty(SupportsGet = true)] public string? Sort { get; set; }

    public IList<Service> Services { get; private set; } = Array.Empty<Service>();
    public int TotalCount { get; private set; }
    public int VisibleCount { get; private set; }

    public IReadOnlyList<CategoryOption> Categories { get; } = new[]
    {
        new CategoryOption("strizhka", "Стрижки",   new[] { "стрижка", "стрижки" }),
        new CategoryOption("borodA",   "Борода",    new[] { "борода", "бороды", "камуфляж" }),
        new CategoryOption("britio",   "Бритьё",    new[] { "бритьё", "бритье" }),
    };

    public sealed record CategoryOption(string Slug, string Title, string[] Keywords);

    public async Task OnGetAsync(CancellationToken ct)
    {
        var all = await _db.Services
            .Where(s => s.IsActive)
            .AsNoTracking()
            .ToListAsync(ct);

        TotalCount = all.Count;

        IEnumerable<Service> filtered = all;

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            filtered = filtered.Where(s =>
                s.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (s.Description != null && s.Description.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(Category))
        {
            var cat = Categories.FirstOrDefault(c => c.Slug == Category);
            if (cat is not null)
            {
                filtered = filtered.Where(s => MatchCategory(s, cat));
            }
        }

        if (MinPrice.HasValue)
            filtered = filtered.Where(s => s.Price >= MinPrice.Value);

        if (MaxPrice.HasValue)
            filtered = filtered.Where(s => s.Price <= MaxPrice.Value);

        if (MaxDuration.HasValue)
            filtered = filtered.Where(s => s.DurationMinutes <= MaxDuration.Value);

        filtered = Sort switch
        {
            "price-asc"    => filtered.OrderBy(s => s.Price).ThenBy(s => s.Name),
            "price-desc"   => filtered.OrderByDescending(s => s.Price).ThenBy(s => s.Name),
            "duration-asc" => filtered.OrderBy(s => s.DurationMinutes).ThenBy(s => s.Name),
            _              => filtered.OrderBy(s => s.Name),
        };

        Services = filtered.ToList();
        VisibleCount = Services.Count;
    }

    public string CategoryOfService(Service s)
    {
        foreach (var c in Categories)
        {
            if (MatchCategory(s, c)) return c.Title;
        }
        return "Другое";
    }

    private static bool MatchCategory(Service s, CategoryOption cat)
    {
        var hay = (s.Name + " " + (s.Description ?? "")).ToLowerInvariant();
        return cat.Keywords.Any(k => hay.Contains(k));
    }
}
