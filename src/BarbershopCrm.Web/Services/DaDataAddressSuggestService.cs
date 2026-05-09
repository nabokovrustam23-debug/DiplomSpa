using System.Text.Json;
using System.Text.Json.Serialization;

namespace BarbershopCrm.Web.Services;

public sealed class DaDataOptions
{
    public const string SectionName = "DaData";
    public string? ApiKey { get; set; }
    public string DefaultCity { get; set; } = "Краснодар";
    public int Count { get; set; } = 7;
    public string SuggestionsUrl { get; set; } =
        "https://suggestions.dadata.ru/suggestions/api/4_1/rs/suggest/address";
}

public interface IAddressSuggestService
{
    bool IsConfigured { get; }
    Task<IReadOnlyList<AddressSuggestion>> SuggestAsync(string query, CancellationToken ct);
}

public sealed record AddressSuggestion(string Value, double? Latitude, double? Longitude);

public sealed class DaDataAddressSuggestService : IAddressSuggestService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly DaDataOptions _options;
    private readonly ILogger<DaDataAddressSuggestService> _logger;

    public DaDataAddressSuggestService(
        HttpClient http,
        Microsoft.Extensions.Options.IOptions<DaDataOptions> options,
        ILogger<DaDataAddressSuggestService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<IReadOnlyList<AddressSuggestion>> SuggestAsync(string query, CancellationToken ct)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(query))
            return Array.Empty<AddressSuggestion>();

        var payload = new SuggestRequest
        {
            Query = query,
            Count = _options.Count,
            Locations = string.IsNullOrWhiteSpace(_options.DefaultCity)
                ? null
                : new[] { new SuggestLocation { City = _options.DefaultCity } },
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, _options.SuggestionsUrl);
        req.Headers.TryAddWithoutValidation("Authorization", $"Token {_options.ApiKey}");
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        req.Content = JsonContent.Create(payload, options: JsonOptions);

        try
        {
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("DaData returned {Status} for query '{Query}'", resp.StatusCode, query);
                return Array.Empty<AddressSuggestion>();
            }

            var body = await resp.Content.ReadFromJsonAsync<SuggestResponse>(JsonOptions, ct);
            if (body?.Suggestions is null)
                return Array.Empty<AddressSuggestion>();

            return body.Suggestions
                .Select(s => new AddressSuggestion(
                    s.Value,
                    ParseCoord(s.Data?.GeoLat),
                    ParseCoord(s.Data?.GeoLon)))
                .Where(s => !string.IsNullOrWhiteSpace(s.Value))
                .ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "DaData call failed for query '{Query}'", query);
            return Array.Empty<AddressSuggestion>();
        }
    }

    private static double? ParseCoord(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return double.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v : null;
    }

    private sealed class SuggestRequest
    {
        [JsonPropertyName("query")] public string Query { get; init; } = "";
        [JsonPropertyName("count")] public int Count { get; init; }
        [JsonPropertyName("locations")] public SuggestLocation[]? Locations { get; init; }
    }

    private sealed class SuggestLocation
    {
        [JsonPropertyName("city")] public string? City { get; init; }
    }

    private sealed class SuggestResponse
    {
        [JsonPropertyName("suggestions")] public List<SuggestionItem>? Suggestions { get; init; }
    }

    private sealed class SuggestionItem
    {
        [JsonPropertyName("value")] public string Value { get; init; } = "";
        [JsonPropertyName("data")] public SuggestionData? Data { get; init; }
    }

    private sealed class SuggestionData
    {
        [JsonPropertyName("geo_lat")] public string? GeoLat { get; init; }
        [JsonPropertyName("geo_lon")] public string? GeoLon { get; init; }
    }
}
