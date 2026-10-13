using System.Globalization;
using System.Text.Json;

namespace MurdocsPoly.Core;

/// <summary>One price level of an order book. Prices are probabilities between 0 and 1 (0.53 = 53 cents = 53%); sizes are shares.</summary>
public readonly record struct Level(decimal Price, decimal Size);

/// <summary>
/// A binary outcome's book. Bids are people wanting to buy the share (the price they pay), asks are people selling it.
/// One share pays 1 USDC if the outcome happens and 0 if not, so a share's price is the market's probability.
/// </summary>
public sealed class OrderBook
{
    public string TokenId { get; init; } = "";
    public decimal TickSize { get; set; } = 0.01m;
    /// <summary>Smallest order in shares, the exchange kind ("v2", or empty for the older CTF exchange) and whether it is a negative-risk market (all from the book).</summary>
    public decimal MinOrderSize { get; set; } = 5;
    public string? Version { get; set; }
    public bool NegRisk { get; set; }
    public SortedDictionary<decimal, decimal> Bids { get; } = new(Comparer<decimal>.Create((a, b) => b.CompareTo(a)));   // best (highest) first
    public SortedDictionary<decimal, decimal> Asks { get; } = new();                                                         // best (lowest) first

    public decimal? BestBid => Bids.Count == 0 ? null : Bids.First().Key;
    public decimal? BestAsk => Asks.Count == 0 ? null : Asks.First().Key;
    public decimal? Mid => BestBid is { } b && BestAsk is { } a ? (a + b) / 2 : null;

    public void Replace(IEnumerable<Level> bids, IEnumerable<Level> asks)
    {
        Bids.Clear(); Asks.Clear();
        foreach (var l in bids) if (l.Size > 0) Bids[l.Price] = l.Size;
        foreach (var l in asks) if (l.Size > 0) Asks[l.Price] = l.Size;
    }

    /// <summary>A live update says "this level now holds Size" (0 removes it). Side is BUY (a bid) or SELL (an ask).</summary>
    public void SetLevel(string side, decimal price, decimal size)
    {
        var book = side.Equals("BUY", StringComparison.OrdinalIgnoreCase) ? Bids : Asks;
        if (size <= 0) book.Remove(price); else book[price] = size;
    }
}

/// <summary>One side of a market, e.g. "Yes" with the id of its tradable token.</summary>
public sealed record Outcome(string Name, string TokenId, decimal? Price);

/// <summary>A question with outcomes (almost always Yes / No), each its own order book.</summary>
public sealed class PmMarket
{
    public string Id { get; init; } = "";
    public string ConditionId { get; init; } = "";
    public string Question { get; init; } = "";
    public string? GroupItemTitle { get; init; }
    public bool Active { get; init; }
    public bool Closed { get; init; }
    public bool AcceptingOrders { get; init; }
    public bool NegRisk { get; init; }
    public decimal TickSize { get; init; } = 0.01m;
    public decimal MinOrderSize { get; init; } = 5;
    public decimal Volume { get; init; }
    public decimal Liquidity { get; init; }
    public List<Outcome> Outcomes { get; init; } = new();
}

/// <summary>A group of related markets (an election, a fed decision, a match). Sports events carry a start time.</summary>
public sealed class PmEvent
{
    public string Id { get; init; } = "";
    public string Slug { get; init; } = "";
    public string Title { get; init; } = "";
    public DateTime? EndDate { get; init; }
    public DateTime? StartTime { get; init; }
    public bool Live { get; init; }
    public bool NegRisk { get; init; }
    public decimal Volume { get; init; }
    public decimal Liquidity { get; init; }
    public List<string> Tags { get; init; } = new();
    public List<PmMarket> Markets { get; init; } = new();
}

/// <summary>Result of Polymarket's own location check. Trading is refused from some countries; reading prices is not.</summary>
public sealed record Geoblock(bool Blocked, string? Country, string? Ip);

/// <summary>Gamma sends several lists as JSON text inside a JSON string, e.g. "outcomes": "[\"Yes\", \"No\"]"; this reads both shapes.</summary>
public static class Json
{
    public static List<string> StringList(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.String)
        {
            try { return JsonSerializer.Deserialize<List<string>>(e.GetString() ?? "[]") ?? new(); } catch (JsonException) { return new(); }
        }
        return e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().Select(x => x.ToString()).ToList() : new();
    }

    public static decimal Dec(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => 0,
        };
    }

    public static string Str(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    public static bool Bool(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    public static DateTime? Date(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String &&
        DateTime.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : null;
}
