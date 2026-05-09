using System.Globalization;

namespace BarbershopCrm.Web.Services;

public interface IImageUploadService
{
    /// <summary>
    /// Validates and saves the uploaded file under wwwroot/uploads/{folder}/.
    /// Returns the public URL (e.g. "/uploads/services/abcd.png") or null if file is null/empty.
    /// Throws InvalidOperationException with user-friendly message on validation failure.
    /// </summary>
    Task<string?> SaveAsync(IFormFile? file, string folder, CancellationToken ct);

    void Delete(string? relativeUrl);
}

public sealed class LocalImageUploadService : IImageUploadService
{
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private static readonly string[] AllowedContentTypes =
        ["image/jpeg", "image/png", "image/webp"];
    private const long MaxBytes = 4 * 1024 * 1024;

    private readonly IWebHostEnvironment _env;
    private readonly ILogger<LocalImageUploadService> _logger;

    public LocalImageUploadService(IWebHostEnvironment env, ILogger<LocalImageUploadService> logger)
    {
        _env = env;
        _logger = logger;
    }

    public async Task<string?> SaveAsync(IFormFile? file, string folder, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return null;

        if (file.Length > MaxBytes)
            throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"Файл слишком большой (>{MaxBytes / (1024 * 1024)} МБ)."));

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            throw new InvalidOperationException(
                $"Неподдерживаемый формат. Разрешены: {string.Join(", ", AllowedExtensions)}.");

        if (!AllowedContentTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Файл не является изображением (некорректный MIME-тип).");

        var safeFolder = SanitizeFolder(folder);
        var fileName = $"{Guid.NewGuid():N}{ext}";
        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var dir = Path.Combine(webRoot, "uploads", safeFolder);
        Directory.CreateDirectory(dir);

        var fullPath = Path.Combine(dir, fileName);
        await using (var stream = File.Create(fullPath))
        {
            await file.CopyToAsync(stream, ct);
        }

        return $"/uploads/{safeFolder}/{fileName}";
    }

    public void Delete(string? relativeUrl)
    {
        if (string.IsNullOrWhiteSpace(relativeUrl)) return;
        if (!relativeUrl.StartsWith("/uploads/", StringComparison.Ordinal)) return;

        try
        {
            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var fullPath = Path.Combine(webRoot, relativeUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete uploaded image '{Url}'", relativeUrl);
        }
    }

    private static string SanitizeFolder(string folder)
    {
        var clean = new string(folder.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
        return string.IsNullOrEmpty(clean) ? "misc" : clean.ToLowerInvariant();
    }
}
