using System.Text.Json;
using MurdocsPoly.Core;
using Xunit;

namespace MurdocsPoly.Tests;

public class PolymarketTests
{
    const string MarketJson = """
    {"id":"2589810","question":"Will it rain?","conditionId":"0xabc","outcomes":"[\"Yes\", \"No\"]","outcomePrices":"[\"0.0015\", \"0.9985\"]",
     "clobTokenIds":"[\"111\", \"222\"]","orderPriceMinTickSize":0.001,"orderMinSize":5,"enableOrderBook":true,"negRisk":true,"active":true,"closed":false,"volume":"1234.5"}
    """;

    [Fact]
    public void MarketReadsListsThatArriveAsJsonText()
    {
        var m = Parse.Market(JsonDocument.Parse(MarketJson).RootElement);
        Assert.Equal(new[] { "Yes", "No" }, m.Outcomes.Select(o => o.Name));
        Assert.Equal(new[] { "111", "222" }, m.Outcomes.Select(o => o.TokenId));
        Assert.Equal(0.9985m, m.Outcomes[1].Price);
        Assert.Equal(0.001m, m.TickSize);
        Assert.True(m.NegRisk && m.AcceptingOrders);
        Assert.Equal(1234.5m, m.Volume);
    }

    [Fact]
    public void BookIsSortedBestFirst()
    {
        var b = Parse.Book(JsonDocument.Parse("""{"asset_id":"111","bids":[{"price":"0.01","size":"5"},{"price":"0.03","size":"7"}],"asks":[{"price":"0.09","size":"4"},{"price":"0.05","size":"2"}]}""").RootElement);
        Assert.Equal(0.03m, b.BestBid); Assert.Equal(0.05m, b.BestAsk); Assert.Equal(0.04m, b.Mid);
    }

    [Fact]
    public void StreamAppliesBookThenPriceChanges()
    {
        var s = new MarketStream();
        var changed = new List<string>(); s.BookChanged += changed.Add;
        s.Apply("""[{"event_type":"book","asset_id":"111","bids":[{"price":"0.40","size":"10"}],"asks":[{"price":"0.45","size":"10"}]}]""");
        s.Apply("""{"event_type":"price_change","price_changes":[{"asset_id":"111","price":"0.41","size":"6","side":"BUY"},{"asset_id":"111","price":"0.45","size":"0","side":"SELL"},{"asset_id":"999","price":"0.5","size":"1","side":"BUY"}]}""");
        var b = s.Book("111")!;
        Assert.Equal(0.41m, b.BestBid); Assert.Null(b.BestAsk);
        Assert.Equal(2, b.Bids.Count);
        Assert.Null(s.Book("999"));
        Assert.Equal(new[] { "111", "111" }, changed);
        s.Apply("PONG"); s.Apply("not json");
        s.Apply("""{"event_type":"tick_size_change","asset_id":"111","new_tick_size":"0.001"}""");
        Assert.Equal(0.001m, b.TickSize);
    }

    [Theory]
    [InlineData(0.534, 0.01, 0.53)]
    [InlineData(0.0004, 0.001, 0.001)]
    [InlineData(0.9999, 0.01, 0.99)]
    public void SnapKeepsPricesOnTickAndInsideTheRange(double price, double tick, double expected) =>
        Assert.Equal((decimal)expected, PriceGrid.Snap((decimal)price, (decimal)tick));

    [Fact]
    public void RowsRunHighToLowAndStayInside()
    {
        var rows = PriceGrid.Rows(0.02m, 0.01m, 3, 3);
        Assert.Equal(new[] { 0.05m, 0.04m, 0.03m, 0.02m, 0.01m }, rows);
        Assert.Equal("53.5", PriceGrid.Cents(0.535m, 0.001m));
        Assert.Equal("53", PriceGrid.Cents(0.53m, 0.01m));
        Assert.Equal(2.5m, PriceGrid.Odds(0.4m));
        Assert.Equal((40m, 60m), PriceGrid.BuyOutcome(0.4m, 100m));
    }
}

/// <summary>Talks to the real public API; only runs when MT_LIVE_POLYMARKET=1 (no account needed).</summary>
public class PolymarketLiveTests
{
    static bool Live => Environment.GetEnvironmentVariable("MT_LIVE_POLYMARKET") == "1";

    [Fact]
    public async Task ReadsEventsBooksAndLiveUpdates()
    {
        if (!Live) return;
        using var c = new PolymarketClient();
        var events = await c.ListEventsAsync(limit: 5);
        Assert.NotEmpty(events);
        var m = events.SelectMany(e => e.Markets).First(m => m.Outcomes.Count == 2 && m.Outcomes[0].TokenId != "" && m.AcceptingOrders);
        var book = await c.GetBookAsync(m.Outcomes[0].TokenId);
        Assert.True(book.Bids.Count + book.Asks.Count > 0);
        var tick = await c.GetTickSizeAsync(m.Outcomes[0].TokenId);
        Assert.True(tick is 0.1m or 0.01m or 0.001m or 0.0001m);
        await using var s = new MarketStream();
        var got = new TaskCompletionSource(); s.BookChanged += _ => got.TrySetResult();
        s.Start(new[] { m.Outcomes[0].TokenId });
        Assert.Same(got.Task, await Task.WhenAny(got.Task, Task.Delay(15000)));
        var g = await c.GeoblockAsync();
        Assert.NotNull(g.Ip);
    }
}
