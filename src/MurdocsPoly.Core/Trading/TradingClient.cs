using System.Text;
using System.Text.Json;

namespace MurdocsPoly.Core.Trading;

public sealed record ApiCredentials(string ApiKey, string Secret, string Passphrase);
public sealed record OrderResult(bool Success, string OrderId, string Status, string Error);
public sealed record OpenOrder(string Id, string AssetId, string Side, decimal Price, decimal Size, decimal Matched, string Status);

/// <summary>
/// Places, lists and cancels orders on the CLOB. Every call first goes through the <see cref="TradeGuard"/>, which refuses when Polymarket
/// does not allow trading from this location. The private key is only used to sign; it is never sent anywhere.
/// </summary>
public sealed class TradingClient : IDisposable
{
    readonly HttpClient _http;
    readonly bool _own;
    readonly PolymarketClient _public;
    readonly TradeGuard _guard;
    readonly PolySigner _signer;
    readonly Func<DateTimeOffset> _now;
    ApiCredentials? _creds;
    public Account Account { get; }

    public TradingClient(Account account, PolymarketClient pub, TradeGuard guard, HttpClient? http = null, Func<DateTimeOffset>? now = null)
    {
        Account = account; _public = pub; _guard = guard; _signer = new PolySigner(account.PrivateKey);
        _own = http == null; _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public string SignerAddress => _signer.Address;

    /// <summary>Gets (or creates the first time) the API credentials for this key, by signing a message that says you control the address.</summary>
    public async Task<ApiCredentials> CredentialsAsync()
    {
        if (_creds != null) return _creds;
        await _guard.EnsureAllowedAsync();
        foreach (var (method, path) in new[] { (HttpMethod.Get, "/auth/derive-api-key"), (HttpMethod.Post, "/auth/api-key") })
        {
            using var req = new HttpRequestMessage(method, PolymarketClient.Clob + path);
            foreach (var h in ClobAuth.L1Headers(_signer, _now().ToUnixTimeSeconds())) req.Headers.TryAddWithoutValidation(h.Key, h.Value);
            using var res = await _http.SendAsync(req);
            var text = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) continue;
            using var doc = JsonDocument.Parse(text);
            var r = doc.RootElement;
            var key = Json.Str(r, "apiKey"); var secret = Json.Str(r, "secret"); var pass = Json.Str(r, "passphrase");
            if (key != "" && secret != "") return _creds = new ApiCredentials(key, secret, pass);
        }
        throw new HttpRequestException("Polymarket did not give API credentials for this key.");
    }

    async Task<(int Status, string Text)> Send(HttpMethod method, string path, string? query = null, string body = "")
    {
        await _guard.EnsureAllowedAsync();
        var c = await CredentialsAsync();
        var ts = _now().ToUnixTimeSeconds().ToString();
        using var req = new HttpRequestMessage(method, PolymarketClient.Clob + path + query);
        req.Headers.TryAddWithoutValidation("POLY_ADDRESS", _signer.Address);
        req.Headers.TryAddWithoutValidation("POLY_SIGNATURE", ClobAuth.L2Signature(c.Secret, ts, method.Method, path, body));
        req.Headers.TryAddWithoutValidation("POLY_TIMESTAMP", ts);
        req.Headers.TryAddWithoutValidation("POLY_API_KEY", c.ApiKey);
        req.Headers.TryAddWithoutValidation("POLY_PASSPHRASE", c.Passphrase);
        if (body != "") req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var res = await _http.SendAsync(req);
        return ((int)res.StatusCode, await res.Content.ReadAsStringAsync());
    }

    /// <summary>Signs and places a limit order, using the market's current book for its tick size, minimum and exchange.</summary>
    public async Task<OrderResult> PlaceLimitAsync(string tokenId, Side side, decimal price, decimal shares, long? expirationUnixSeconds = null)
    {
        await _guard.EnsureAllowedAsync();
        var book = await _public.GetBookAsync(tokenId);
        var c = await CredentialsAsync();
        var order = OrderBuilder.BuildLimit(_signer, Account, book, side, price, shares, c.ApiKey, Random.Shared.NextInt64(1, 1L << 53), _now().ToUnixTimeMilliseconds(), expirationUnixSeconds);
        var (status, text) = await Send(HttpMethod.Post, "/order", body: order.Body);
        return ParseResult(status, text);
    }

    /// <summary>Buys for a dollar amount, fill-or-kill, at the given worst price (the best ask when the order is sent).</summary>
    public async Task<OrderResult> PlaceMarketBuyAsync(string tokenId, decimal worstPrice, decimal usdAmount)
    {
        await _guard.EnsureAllowedAsync();
        var book = await _public.GetBookAsync(tokenId);
        var c = await CredentialsAsync();
        var order = OrderBuilder.BuildMarketBuy(_signer, Account, book, worstPrice, usdAmount, c.ApiKey, Random.Shared.NextInt64(1, 1L << 53), _now().ToUnixTimeMilliseconds());
        var (status, text) = await Send(HttpMethod.Post, "/order", body: order.Body);
        return ParseResult(status, text);
    }

    public static OrderResult ParseResult(int status, string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var r = doc.RootElement;
            bool ok = Json.Bool(r, "success");
            var err = Json.Str(r, "errorMsg"); if (err == "") err = Json.Str(r, "error");
            return new OrderResult(ok, Json.Str(r, "orderID"), Json.Str(r, "status"), ok ? "" : (err != "" ? err : $"HTTP {status}"));
        }
        catch (JsonException) { return new OrderResult(false, "", "", $"HTTP {status}: {text[..Math.Min(160, text.Length)]}"); }
    }

    public async Task<bool> CancelAsync(string orderId)
    {
        var (status, _) = await Send(HttpMethod.Delete, "/order", body: JsonSerializer.Serialize(new { orderID = orderId }));
        return status is >= 200 and < 300;
    }

    /// <summary>Cancels every open order on one outcome.</summary>
    public async Task<bool> CancelOutcomeAsync(string assetId)
    {
        var (status, _) = await Send(HttpMethod.Delete, "/cancel-market-orders", body: JsonSerializer.Serialize(new { asset_id = assetId }));
        return status is >= 200 and < 300;
    }

    public async Task<List<OpenOrder>> OpenOrdersAsync(string? assetId = null)
    {
        var (status, text) = await Send(HttpMethod.Get, "/data/orders", assetId == null ? null : "?asset_id=" + Uri.EscapeDataString(assetId));
        if (status is < 200 or >= 300) throw new HttpRequestException($"HTTP {status} listing orders");
        return ParseOrders(text);
    }

    public static List<OpenOrder> ParseOrders(string text)
    {
        using var doc = JsonDocument.Parse(text);
        var arr = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement : doc.RootElement.TryGetProperty("data", out var d) ? d : default;
        if (arr.ValueKind != JsonValueKind.Array) return new();
        return arr.EnumerateArray().Select(o => new OpenOrder(Json.Str(o, "id"), Json.Str(o, "asset_id"), Json.Str(o, "side"), Json.Dec(o, "price"),
            Json.Dec(o, "original_size"), Json.Dec(o, "size_matched"), Json.Str(o, "status"))).Where(o => o.Status.Equals("LIVE", StringComparison.OrdinalIgnoreCase) || o.Status == "").ToList();
    }

    public void Dispose() { if (_own) _http.Dispose(); }
}
