using System.Text.Json;

namespace MurdocsPoly.Core;

/// <summary>
/// Read-only access to Polymarket: market discovery (Gamma API), order books and tick sizes (CLOB API) and the location check.
/// None of this needs an account, a wallet or a key. Placing orders is a separate, signed flow (see docs/POLYMARKET.md).
/// </summary>
public sealed class PolymarketClient : IDisposable
{
    public const string Gamma = "https://gamma-api.polymarket.com", Clob = "https://clob.polymarket.com";
    readonly HttpClient _http;
    readonly bool _own;

    public PolymarketClient(HttpClient? http = null)
    {
        _own = http == null;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    async Task<JsonDocument> Get(string url)
    {
        using var res = await _http.GetAsync(url);
        var text = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"{(int)res.StatusCode} from {new Uri(url).AbsolutePath}: {text[..Math.Min(200, text.Length)]}");
        return JsonDocument.Parse(text);
    }

    /// <summary>Open events, biggest 24 hour volume first. A tag (e.g. "sports", "politics") narrows it.</summary>
    public async Task<List<PmEvent>> ListEventsAsync(string? tagSlug = null, int limit = 50, int offset = 0, string order = "volume24hr")
    {
        var url = $"{Gamma}/events?active=true&closed=false&limit={limit}&offset={offset}&order={order}&ascending=false" + (tagSlug == null ? "" : $"&tag_slug={Uri.EscapeDataString(tagSlug)}");
        using var doc = await Get(url);
        return doc.RootElement.EnumerateArray().Select(Parse.Event).ToList();
    }

    public async Task<PmEvent?> GetEventBySlugAsync(string slug)
    {
        using var doc = await Get($"{Gamma}/events?slug={Uri.EscapeDataString(slug)}");
        return doc.RootElement.EnumerateArray().Select(Parse.Event).FirstOrDefault();
    }

    /// <summary>Free-text search over events.</summary>
    public async Task<List<PmEvent>> SearchAsync(string text, int limit = 20)
    {
        using var doc = await Get($"{Gamma}/public-search?q={Uri.EscapeDataString(text)}&limit_per_type={limit}&events_status=active");
        return doc.RootElement.TryGetProperty("events", out var ev) && ev.ValueKind == JsonValueKind.Array ? ev.EnumerateArray().Select(Parse.Event).ToList() : new();
    }

    public async Task<OrderBook> GetBookAsync(string tokenId)
    {
        using var doc = await Get($"{Clob}/book?token_id={tokenId}");
        var tick = Json.Dec(doc.RootElement, "tick_size");
        return Parse.Book(doc.RootElement, tick > 0 ? tick : 0.01m);
    }

    public async Task<decimal> GetTickSizeAsync(string tokenId)
    {
        using var doc = await Get($"{Clob}/tick-size?token_id={tokenId}");
        return Json.Dec(doc.RootElement, "minimum_tick_size");
    }

    /// <summary>Whether Polymarket accepts trading from this machine's address. Reading is allowed either way.</summary>
    public async Task<Geoblock> GeoblockAsync()
    {
        using var doc = await Get("https://polymarket.com/api/geoblock");
        var r = doc.RootElement;
        return new Geoblock(Json.Bool(r, "blocked"), Json.Str(r, "country") is { Length: > 0 } c ? c : null, Json.Str(r, "ip") is { Length: > 0 } ip ? ip : null);
    }

    public void Dispose() { if (_own) _http.Dispose(); }
}
