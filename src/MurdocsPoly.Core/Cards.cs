using System.Globalization;
using System.Text.RegularExpressions;

namespace MurdocsPoly.Core;

/// <summary>A market as the cards need it: its outcomes with the price to buy each one (in cents), plus what an order needs.</summary>
public sealed record CardMarket(string Id, string Question, decimal Tick, decimal Min, List<CardOutcome> Outcomes);
public sealed record CardOutcome(string Name, string Token, int? Cents);

/// <summary>A price button on a card: which outcome of which market it buys, and how to draw it.</summary>
public sealed record CardButton(string Label, string MarketId, int Index, int? Cents, string? Color, string Kind);

public sealed record CardTeam(string Name, string Abbr, string? Logo, string? Record, string? Score, string? Color);

/// <summary>
/// One tile on the page: a live game (teams, score, moneyline / spread / total buttons) or a plain event (a few Yes / No markets).
/// Plain data for the web UI; built by <see cref="CardBuilder"/>.
/// </summary>
public sealed class Card
{
    public string Kind { get; init; } = "market";           // "game" or "market"
    public string EventId { get; init; } = "";
    public string Title { get; init; } = "";
    public string League { get; init; } = "";
    public bool Live { get; init; }
    public string Status { get; init; } = "";
    public string VolumeText { get; init; } = "";
    public string? Image { get; init; }
    public List<string> Tags { get; init; } = new();
    public List<CardTeam> Teams { get; init; } = new();
    public List<CardButton> Moneyline { get; init; } = new();
    public List<CardButton> Spread { get; init; } = new();
    public List<CardButton> Total { get; init; } = new();
    public List<CardMarket> Markets { get; init; } = new();
    /// <summary>For plain events: one row per market, each with its Yes / No buttons.</summary>
    public List<CardRow> Rows { get; init; } = new();
}

public sealed record CardRow(string Text, List<CardButton> Buttons);

public static class CardBuilder
{
    static readonly Regex ScoreRx = new(@"^\s*(\d+)\s*-\s*(\d+)\s*$");
    static readonly HashSet<string> GenericTags = new(StringComparer.OrdinalIgnoreCase) { "Sports", "Games", "Soccer", "Tennis", "Esports", "Recurring", "Featured" };

    public static string VolumeText(decimal v)
    {
        var inv = CultureInfo.InvariantCulture;
        return v >= 1_000_000 ? "$" + (v / 1_000_000).ToString("0.##", inv) + "M Vol" : v >= 1_000 ? "$" + (v / 1_000).ToString("0.##", inv) + "K Vol" : "$" + v.ToString("0", inv) + " Vol";
    }

    /// <summary>The price to buy an outcome now, in cents: the best ask, or the last price when the book is empty.</summary>
    public static int? Cents(PmMarket m, int index)
    {
        decimal? p = index == 0 ? m.BestAsk : (m.BestBid is { } b ? 1 - b : null);
        p ??= index < m.Outcomes.Count ? m.Outcomes[index].Price : null;
        return p is { } v ? Math.Clamp((int)Math.Round(v * 100, MidpointRounding.AwayFromZero), 1, 99) : null;
    }

    public static CardMarket ToCardMarket(PmMarket m) =>
        new(m.Id, m.GroupItemTitle ?? m.Question, m.TickSize, m.MinOrderSize, m.Outcomes.Select((o, i) => new CardOutcome(o.Name, o.TokenId, Cents(m, i))).ToList());

    /// <summary>The live games, one card per game, grouped by league in order of volume.</summary>
    public static List<Card> LiveGames(IEnumerable<PmEvent> events)
    {
        var cards = new List<Card>();
        foreach (var g in events.Where(e => e.Period != "SUS").GroupBy(e => e.GameId?.ToString() ?? e.Id))
        {
            var main = g.FirstOrDefault(e => e.Markets.Any(m => m.SportsMarketType == "moneyline") && !e.Slug.EndsWith("more-markets"));
            if (main == null) continue;
            var more = g.FirstOrDefault(e => e.Slug.EndsWith("more-markets"));
            var card = Game(main, more);
            if (card != null) cards.Add(card);
        }
        var leagueVolume = cards.GroupBy(c => c.League).ToDictionary(g => g.Key, g => g.Sum(c => c.Volume()));
        return cards.OrderByDescending(c => leagueVolume[c.League]).ThenBy(c => c.League).ThenByDescending(c => c.Volume()).ToList();
    }

    static decimal Volume(this Card c) => decimal.TryParse(new string(c.VolumeText.Where(ch => char.IsDigit(ch) || ch == '.').ToArray()), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v * (c.VolumeText.Contains('M') ? 1_000_000 : c.VolumeText.Contains('K') ? 1_000 : 1) : 0;

    public static Card? Game(PmEvent main, PmEvent? more)
    {
        var teams = main.Teams.OrderBy(t => t.Ordering == "away" ? 1 : 0).ToList();
        var money = main.Markets.Where(m => m.SportsMarketType == "moneyline" && m.Outcomes.Count == 2).ToList();
        if (teams.Count < 2 || money.Count == 0) return null;
        bool headToHead = money.Count == 1 && teams.All(t => money[0].Outcomes.Any(o => string.Equals(o.Name, t.Name, StringComparison.OrdinalIgnoreCase)));
        var used = new List<PmMarket>(money);

        // "1-0" is a score; "4-6, 6-1, 5-4" is a list of sets (one number per set for each player)
        var parts = (main.Score ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => ScoreRx.Match(x)).Where(x => x.Success).ToList();
        string? hs = parts.Count == 0 ? null : string.Join("  ", parts.Select(x => x.Groups[1].Value));
        string? aws = parts.Count == 0 ? null : string.Join("  ", parts.Select(x => x.Groups[2].Value));
        var rows = new List<CardTeam>
        {
            new(teams[0].Name, Abbr(teams[0]), teams[0].Logo, headToHead ? null : teams[0].Record, hs, teams[0].Color),
            new(teams[1].Name, Abbr(teams[1]), teams[1].Logo, headToHead ? null : teams[1].Record, aws, teams[1].Color),
        };

        // moneyline in screen order: home, draw (if any), away
        var ml = new List<CardButton>();
        if (headToHead)
            foreach (var t in teams)
            {
                int i = money[0].Outcomes.FindIndex(o => string.Equals(o.Name, t.Name, StringComparison.OrdinalIgnoreCase));
                ml.Add(new CardButton(Abbr(t).ToUpperInvariant(), money[0].Id, i, Cents(money[0], i), t.Color, "moneyline"));
            }
        else
        foreach (var t in new[] { teams[0], null, teams[1] })
        {
            var mk = t == null ? money.FirstOrDefault(x => (x.GroupItemTitle ?? "").StartsWith("Draw", StringComparison.OrdinalIgnoreCase))
                               : money.FirstOrDefault(x => string.Equals(x.GroupItemTitle, t.Name, StringComparison.OrdinalIgnoreCase));
            if (mk != null) ml.Add(new CardButton(t == null ? "DRAW" : Abbr(t).ToUpperInvariant(), mk.Id, 0, Cents(mk, 0), t?.Color, "moneyline"));
        }

        var spread = new List<CardButton>(); var total = new List<CardButton>();
        if (more != null)
        {
            var sp = MostEven(more.Markets.Where(x => x.SportsMarketType == "spreads" && x.Line != null && x.Outcomes.Count == 2));
            if (sp != null)
            {
                used.Add(sp);
                var first = teams.FirstOrDefault(t => string.Equals(t.Name, sp.Outcomes[0].Name, StringComparison.OrdinalIgnoreCase)) ?? teams[0];
                var second = first == teams[0] ? teams[1] : teams[0];
                var a = new CardButton($"{Abbr(first).ToUpperInvariant()} {Signed(sp.Line!.Value)}", sp.Id, 0, Cents(sp, 0), null, "spread");
                var b = new CardButton($"{Abbr(second).ToUpperInvariant()} {Signed(-sp.Line!.Value)}", sp.Id, 1, Cents(sp, 1), null, "spread");
                spread = first == teams[0] ? new() { a, b } : new() { b, a };
            }
            var tt = MostEven(more.Markets.Where(x => x.SportsMarketType == "totals" && x.Line != null && x.Outcomes.Count == 2));
            if (tt != null)
            {
                used.Add(tt);
                var line = tt.Line!.Value.ToString("0.#", CultureInfo.InvariantCulture);
                total = new() { new($"O {line}", tt.Id, 0, Cents(tt, 0), null, "total"), new($"U {line}", tt.Id, 1, Cents(tt, 1), null, "total") };
            }
        }

        bool live = main.Live && !string.IsNullOrEmpty(main.Period);
        return new Card
        {
            Kind = "game", EventId = main.Id, Title = main.Title, League = LeagueOf(main), Live = live,
            Status = live ? main.Period! : (main.StartTime?.ToLocalTime().ToString("h:mm tt", CultureInfo.InvariantCulture) ?? ""),
            VolumeText = VolumeText(main.Volume + (more?.Volume ?? 0)), Image = main.Image, Tags = main.Tags, Teams = rows,
            Moneyline = ml, Spread = spread, Total = total, Markets = used.Select(ToCardMarket).ToList(),
        };
    }

    /// <summary>Any other event as a tile: its biggest markets, each with Yes / No (or its own two outcomes).</summary>
    public static Card Plain(PmEvent e)
    {
        var markets = e.Markets.Where(m => m.Active && !m.Closed && m.Outcomes.Count == 2 && m.Outcomes[0].TokenId != "").OrderByDescending(m => m.Volume).Take(3).ToList();
        var rows = markets.Select(m => new CardRow(m.GroupItemTitle ?? m.Question,
            new() { new(m.Outcomes[0].Name, m.Id, 0, Cents(m, 0), null, "yes"), new(m.Outcomes[1].Name, m.Id, 1, Cents(m, 1), null, "no") })).ToList();
        return new Card { Kind = "market", EventId = e.Id, Title = e.Title, League = LeagueOf(e), Live = e.Live, VolumeText = VolumeText(e.Volume), Image = e.Image, Tags = e.Tags, Rows = rows, Markets = markets.Select(ToCardMarket).ToList() };
    }

    /// <summary>The line the market is most undecided about, judged by the middle of a real bid and ask (lines nobody is bidding on are ignored).</summary>
    static PmMarket? MostEven(IEnumerable<PmMarket> markets)
    {
        var list = markets.ToList();
        var priced = list.Where(x => x.BestBid != null && x.BestAsk != null).ToList();
        return (priced.Count > 0 ? priced : list).OrderBy(x => x.BestBid != null && x.BestAsk != null ? Math.Abs((x.BestBid.Value + x.BestAsk.Value) / 2 - 0.5m) : 1m).FirstOrDefault();
    }

    static string LeagueOf(PmEvent e) => e.League ?? e.Tags.FirstOrDefault(t => !GenericTags.Contains(t)) ?? "";

    static string Abbr(PmTeam t) => t.Abbreviation != "" ? t.Abbreviation : new string(t.Name.Where(char.IsLetter).Take(3).ToArray());

    static string Signed(decimal v) => (v > 0 ? "+" : v < 0 ? "-" : "") + Math.Abs(v).ToString("0.#", CultureInfo.InvariantCulture);
}
