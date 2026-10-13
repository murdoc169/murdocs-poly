using MurdocsPoly.Core;

namespace MurdocsPoly.Desktop;

/// <summary>A market's ladder with live books. A Yes/No switch swaps which outcome's book is shown (they mirror each other).</summary>
sealed class LadderForm : Form
{
    readonly PmEvent _event;
    readonly PmMarket _market;
    readonly MarketStream _stream;
    readonly LadderControl _ladder = new() { Dock = DockStyle.Fill };
    readonly Label _head = new() { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8, 6, 8, 0), Font = new Font("Tahoma", 9f, FontStyle.Bold), BackColor = Color.FromArgb(236, 233, 216) };
    readonly Label _foot = new() { Dock = DockStyle.Bottom, Height = 22, Padding = new Padding(8, 3, 8, 0), Font = new Font("Tahoma", 8.25f), ForeColor = Color.DimGray };
    readonly Panel _bar = new() { Dock = DockStyle.Top, Height = 30, BackColor = Color.FromArgb(236, 233, 216) };
    readonly List<RadioButton> _outcomes = new();
    int _shown;

    public LadderForm(PmEvent ev, PmMarket market, string tradingNote)
    {
        _event = ev; _market = market;
        Text = $"{(market.GroupItemTitle ?? market.Question)} | {ev.Title}   {Fmt.Usd(market.Volume)}";
        ClientSize = new Size(520, 760); StartPosition = FormStartPosition.CenterParent;
        _foot.Text = tradingNote;

        int x = 8;
        foreach (var (o, i) in market.Outcomes.Select((o, i) => (o, i)))
        {
            var rb = new RadioButton { Text = o.Name, Left = x, Top = 5, AutoSize = true, Checked = i == 0, Font = new Font("Tahoma", 9f, FontStyle.Bold), Appearance = Appearance.Button, Padding = new Padding(10, 1, 10, 1) };
            int idx = i; rb.CheckedChanged += (_, _) => { if (rb.Checked) Show(idx); };
            _bar.Controls.Add(rb); _outcomes.Add(rb); x += 70;
        }
        var centre = new Button { Text = "Centre", Left = x + 10, Top = 4, Width = 70, Height = 22, FlatStyle = FlatStyle.System };
        centre.Click += (_, _) => _ladder.Centre();
        _bar.Controls.Add(centre);

        Controls.Add(_ladder); Controls.Add(_foot); Controls.Add(_bar); Controls.Add(_head);

        _stream = new MarketStream();
        _stream.BookChanged += id => { if (IsHandleCreated && !IsDisposed) BeginInvoke(() => OnBook(id)); };
        _stream.Status += s => { if (IsHandleCreated && !IsDisposed) BeginInvoke(() => _head.Text = Head() + (s == "connected" ? "" : "\n" + s)); };
        Shown += async (_, _) => await Load();
        FormClosed += async (_, _) => await _stream.DisposeAsync();
    }

    string Head() => $"{_market.Question}\nLast {Fmt.Cents(_market.Outcomes[Math.Min(_shown, _market.Outcomes.Count - 1)].Price ?? 0, _market.TickSize)}   Ticks {_market.TickSize}   Min {_market.MinOrderSize} shares";

    async Task Load()
    {
        _head.Text = Head();
        _stream.Start(_market.Outcomes.Select(o => o.TokenId).Where(t => t != ""));
        try
        {
            using var c = new PolymarketClient();
            var b = await c.GetBookAsync(_market.Outcomes[_shown].TokenId);
            if (_stream.Book(b.TokenId) == null) _ladder.SetBook(b);       // the live stream will take over
        }
        catch (Exception ex) { _foot.Text = ex.Message; }
    }

    void Show(int i) { _shown = i; _head.Text = Head(); _ladder.SetBook(_stream.Book(_market.Outcomes[i].TokenId)); _ladder.Centre(); }

    void OnBook(string tokenId)
    {
        var shownToken = _market.Outcomes[_shown].TokenId;
        if (tokenId != shownToken) return;
        var b = _stream.Book(tokenId);
        _ladder.SetBook(b);
        if (b != null) Text = $"{(_market.GroupItemTitle ?? _market.Question)}   {(b.BestBid is { } bid ? Fmt.Cents(bid, b.TickSize) : "-")} / {(b.BestAsk is { } ask ? Fmt.Cents(ask, b.TickSize) : "-")}";
    }
}
