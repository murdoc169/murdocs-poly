using System.Globalization;
using System.Text.Json;

namespace MurdocsPoly.Core;

/// <summary>Pure parsers for the public API's JSON, so they can be tested without network.</summary>
public static class Parse
{
    static JsonElement Prop(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;

    public static PmMarket Market(JsonElement m)
    {
        var names = Json.StringList(Prop(m, "outcomes"));
        var tokens = Json.StringList(Prop(m, "clobTokenIds"));
        var prices = Json.StringList(Prop(m, "outcomePrices"));
        var outcomes = names.Select((n, i) => new Outcome(n, i < tokens.Count ? tokens[i] : "",
            i < prices.Count && decimal.TryParse(prices[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null)).ToList();
        var tick = Json.Dec(m, "orderPriceMinTickSize");
        var min = Json.Dec(m, "orderMinSize");
        return new PmMarket
        {
            Id = Json.Str(m, "id"), ConditionId = Json.Str(m, "conditionId"), Question = Json.Str(m, "question"),
            GroupItemTitle = Json.Str(m, "groupItemTitle") is { Length: > 0 } g ? g : null,
            Active = Json.Bool(m, "active"), Closed = Json.Bool(m, "closed"), AcceptingOrders = Json.Bool(m, "acceptingOrders") || Json.Bool(m, "enableOrderBook"),
            NegRisk = Json.Bool(m, "negRisk"), TickSize = tick > 0 ? tick : 0.01m, MinOrderSize = min > 0 ? min : 5,
            Volume = Json.Dec(m, "volume"), Liquidity = Json.Dec(m, "liquidity"), Outcomes = outcomes,
            SportsMarketType = Json.Str(m, "sportsMarketType") is { Length: > 0 } smt ? smt : null, Line = Json.DecN(m, "line"),
            BestBid = Json.DecN(m, "bestBid"), BestAsk = Json.DecN(m, "bestAsk"),
        };
    }

    public static PmEvent Event(JsonElement e)
    {
        var tags = Prop(e, "tags"); var markets = Prop(e, "markets"); var teams = Prop(e, "teams"); var sport = Prop(e, "sport");
        return new PmEvent
        {
            Id = Json.Str(e, "id"), Slug = Json.Str(e, "slug"), Title = Json.Str(e, "title"),
            EndDate = Json.Date(e, "endDate"), StartTime = Json.Date(e, "startTime") ?? Json.Date(e, "eventDate"),
            Live = Json.Bool(e, "live"), NegRisk = Json.Bool(e, "negRisk"), Volume = Json.Dec(e, "volume"), Liquidity = Json.Dec(e, "liquidity"),
            Tags = tags.ValueKind == JsonValueKind.Array ? tags.EnumerateArray().Select(x => Json.Str(x, "label")).Where(x => x != "").ToList() : new(),
            Markets = markets.ValueKind == JsonValueKind.Array ? markets.EnumerateArray().Select(Market).ToList() : new(),
            Score = Json.Str(e, "score") is { Length: > 0 } sc ? sc : null, Period = Json.Str(e, "period") is { Length: > 0 } pe ? pe : null,
            Elapsed = Json.Str(e, "elapsed") is { Length: > 0 } el ? el : null, GameId = Json.DecN(e, "gameId") is { } gid ? (long)gid : null,
            League = Json.Str(sport, "name") is { Length: > 0 } ln ? ln : null, Image = Json.Str(e, "image") is { Length: > 0 } im ? im : null, Ended = Json.Bool(e, "ended"),
            Teams = teams.ValueKind == JsonValueKind.Array ? teams.EnumerateArray().Select(t => new PmTeam(Json.Str(t, "name"), Json.Str(t, "abbreviation"),
                Json.Str(t, "color") is { Length: > 0 } c ? c : null, Json.Str(t, "logo") is { Length: > 0 } lg ? lg : null, Json.Str(t, "record") is { Length: > 0 } rc ? rc : null,
                Json.Str(t, "ordering") is { Length: > 0 } od ? od : null)).ToList() : new(),
        };
    }

    public static List<Level> Levels(JsonElement arr) => arr.ValueKind != JsonValueKind.Array ? new() :
        arr.EnumerateArray().Select(l => new Level(Json.Dec(l, "price"), Json.Dec(l, "size"))).ToList();

    /// <summary>The full book from <c>GET /book</c>, or from a "book" message on the stream.</summary>
    public static OrderBook Book(JsonElement b, decimal tick = 0.01m)
    {
        var min = Json.Dec(b, "min_order_size");
        var book = new OrderBook { TokenId = Json.Str(b, "asset_id"), TickSize = tick, MinOrderSize = min > 0 ? min : 5, NegRisk = Json.Bool(b, "neg_risk"), Version = Json.Str(b, "version") is { Length: > 0 } v ? v : null };
        book.Replace(Levels(Prop(b, "bids")), Levels(Prop(b, "asks")));
        return book;
    }
}
