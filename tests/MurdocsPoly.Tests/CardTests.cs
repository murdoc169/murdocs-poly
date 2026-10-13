using System.Text.Json;
using MurdocsPoly.Core;
using Xunit;

namespace MurdocsPoly.Tests;

public class CardTests
{
    static PmEvent Ev(string json) => Parse.Event(JsonDocument.Parse(json).RootElement);

    const string Main = """
    {"id":"1","slug":"ucl-rcl-spo-2026-10-13","title":"Racing Club de Lens vs. Sporting CP","gameId":90122452,"live":true,"ended":false,"period":"2H","elapsed":"66","score":"0-1","volume":500270,
     "sport":{"name":"UCL"},"startTime":"2026-10-13T16:45:00Z",
     "teams":[{"name":"Racing Club de Lens","abbreviation":"rcl","color":"#c41c35","record":"1-0-0","ordering":"home"},{"name":"Sporting CP","abbreviation":"spo","color":"#0a7d4f","record":"1-0-0","ordering":"away"}],
     "markets":[
      {"id":"m1","groupItemTitle":"Racing Club de Lens","sportsMarketType":"moneyline","outcomes":"[\"Yes\",\"No\"]","outcomePrices":"[\"0.115\",\"0.885\"]","clobTokenIds":"[\"t1y\",\"t1n\"]","bestBid":0.11,"bestAsk":0.12,"orderPriceMinTickSize":0.01,"orderMinSize":5,"active":true},
      {"id":"m2","groupItemTitle":"Draw (Racing Club de Lens vs. Sporting CP)","sportsMarketType":"moneyline","outcomes":"[\"Yes\",\"No\"]","outcomePrices":"[\"0.28\",\"0.72\"]","clobTokenIds":"[\"t2y\",\"t2n\"]","bestBid":0.27,"bestAsk":0.29,"active":true},
      {"id":"m3","groupItemTitle":"Sporting CP","sportsMarketType":"moneyline","outcomes":"[\"Yes\",\"No\"]","outcomePrices":"[\"0.615\",\"0.385\"]","clobTokenIds":"[\"t3y\",\"t3n\"]","bestBid":0.61,"bestAsk":0.62,"active":true}]}
    """;

    const string More = """
    {"id":"2","slug":"ucl-rcl-spo-2026-10-13-more-markets","title":"Racing Club de Lens vs. Sporting CP - More Markets","gameId":90122452,"live":true,"volume":10000,
     "markets":[
      {"id":"s1","groupItemTitle":"Racing Club de Lens (-1.5)","sportsMarketType":"spreads","line":-1.5,"outcomes":"[\"Racing Club de Lens\",\"Sporting CP\"]","outcomePrices":"[\"0.02\",\"0.98\"]","clobTokenIds":"[\"s1y\",\"s1n\"]","bestBid":0.014,"bestAsk":0.077,"active":true},
      {"id":"s2","groupItemTitle":"Sporting CP (-1.5)","sportsMarketType":"spreads","line":-1.5,"outcomes":"[\"Sporting CP\",\"Racing Club de Lens\"]","outcomePrices":"[\"0.21\",\"0.79\"]","clobTokenIds":"[\"s2y\",\"s2n\"]","bestBid":0.19,"bestAsk":0.23,"active":true},
      {"id":"t1","groupItemTitle":"O/U 1.5","sportsMarketType":"totals","line":1.5,"outcomes":"[\"Over\",\"Under\"]","outcomePrices":"[\"0.72\",\"0.28\"]","clobTokenIds":"[\"t15o\",\"t15u\"]","bestBid":0.71,"bestAsk":0.73,"active":true},
      {"id":"t2","groupItemTitle":"O/U 2.5","sportsMarketType":"totals","line":2.5,"outcomes":"[\"Over\",\"Under\"]","outcomePrices":"[\"0.335\",\"0.665\"]","clobTokenIds":"[\"t25o\",\"t25u\"]","bestBid":0.33,"bestAsk":0.34,"active":true}]}
    """;

    [Fact]
    public void GameCardHasTeamsScoreAndTheThreeButtonGroups()
    {
        var c = CardBuilder.LiveGames(new[] { Ev(Main), Ev(More) }).Single();
        Assert.Equal("game", c.Kind); Assert.Equal("UCL", c.League); Assert.True(c.Live); Assert.Equal("2H", c.Status);
        Assert.Equal(new[] { "0", "1" }, c.Teams.Select(t => t.Score));
        Assert.Equal(new[] { "RCL", "DRAW", "SPO" }, c.Moneyline.Select(b => b.Label));
        Assert.Equal(new int?[] { 12, 29, 62 }, c.Moneyline.Select(b => b.Cents));         // the price to buy: the best ask
        Assert.Equal("$510.27K Vol", c.VolumeText);
        // the spread closest to even: Sporting -1.5 (so Lens +1.5), home first
        Assert.Equal(new[] { "RCL +1.5", "SPO -1.5" }, c.Spread.Select(b => b.Label));
        Assert.Equal(new int?[] { 81, 23 }, new int?[] { c.Spread[0].Cents, c.Spread[1].Cents });   // Lens +1.5 buys the "No" side: 1 - best bid 0.19; Sporting -1.5 is its best ask
        Assert.Equal(new[] { "O 2.5", "U 2.5" }, c.Total.Select(b => b.Label));
        Assert.Equal(34, c.Total[0].Cents); Assert.Equal(67, c.Total[1].Cents);
    }

    [Fact]
    public void ButtonsPointAtMarketsThatCarryTheTokensToTrade()
    {
        var c = CardBuilder.LiveGames(new[] { Ev(Main), Ev(More) }).Single();
        var b = c.Moneyline[0];
        var m = c.Markets.Single(x => x.Id == b.MarketId);
        Assert.Equal("t1y", m.Outcomes[b.Index].Token);
        Assert.Equal(0.01m, m.Tick); Assert.Equal(5m, m.Min);
    }

    [Fact]
    public void SuspendedAndEndedGamesAreLeftOut()
    {
        var sus = Ev(Main.Replace("\"period\":\"2H\"", "\"period\":\"SUS\""));
        Assert.Empty(CardBuilder.LiveGames(new[] { sus }));
    }

    [Fact]
    public void PlainEventsShowTheirBiggestMarketsWithYesAndNo()
    {
        var e = Ev("""
            {"id":"9","title":"Fed decision","volume":34798283,"tags":[{"label":"Fed"}],"markets":[
            {"id":"a","question":"Cut 25?","active":true,"closed":false,"volume":"100","outcomes":"[\"Yes\",\"No\"]","outcomePrices":"[\"0.6\",\"0.4\"]","clobTokenIds":"[\"y\",\"n\"]","bestBid":0.59,"bestAsk":0.61},
            {"id":"b","question":"Cut 50?","active":true,"closed":false,"volume":"900","outcomes":"[\"Yes\",\"No\"]","outcomePrices":"[\"0.1\",\"0.9\"]","clobTokenIds":"[\"y2\",\"n2\"]","bestBid":0.09,"bestAsk":0.11}]}
            """);
        var c = CardBuilder.Plain(e);
        Assert.Equal("market", c.Kind); Assert.Equal("$34.8M Vol", c.VolumeText);
        Assert.Equal("Cut 50?", c.Rows[0].Text);                    // biggest first
        Assert.Equal(new int?[] { 11, 91 }, c.Rows[0].Buttons.Select(b => b.Cents));
    }
}
