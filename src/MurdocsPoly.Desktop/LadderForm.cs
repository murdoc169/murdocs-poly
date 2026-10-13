using MurdocsPoly.Core;
using MurdocsPoly.Core.Trading;

namespace MurdocsPoly.Desktop;

/// <summary>
/// A market's ladder with live books. A Yes/No switch swaps which outcome's book is shown (they mirror each other).
/// Click the blue column to BUY at a price, the pink column to SELL at a price. Orders are only sent when the trading session says
/// trading is possible (an account is saved and Polymarket allows this location).
/// </summary>
sealed class LadderForm : Form
{
    readonly PmEvent _event;
    readonly PmMarket _market;
    readonly TradingSession _session;
    readonly MarketStream _stream;
    readonly LadderControl _ladder = new() { Dock = DockStyle.Fill };
    readonly Label _head = new() { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8, 6, 8, 0), Font = new Font("Tahoma", 9f, FontStyle.Bold), BackColor = Color.FromArgb(236, 233, 216) };
    readonly Label _foot = new() { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(8, 3, 8, 0), Font = new Font("Tahoma", 8.25f), ForeColor = Color.DimGray };
    readonly Panel _bar = new() { Dock = DockStyle.Top, Height = 30, BackColor = Color.FromArgb(236, 233, 216) };
    readonly Panel _tools = new() { Dock = DockStyle.Bottom, Height = 34, BackColor = Color.FromArgb(236, 233, 216) };
    readonly TextBox _shares = new() { Left = 56, Top = 6, Width = 60, TextAlign = HorizontalAlignment.Right };
    readonly CheckBox _confirm = new() { Text = "Confirm", Left = 124, Top = 7, AutoSize = true, Checked = true };
    readonly Button _cancelAll = new() { Text = "Cancel my orders here", Left = 200, Top = 4, Width = 140, Height = 24, FlatStyle = FlatStyle.System };
    readonly System.Windows.Forms.Timer _poll = new() { Interval = 4000 };
    int _shown;
    string _whyNot = "Checking...";
    bool _busy;

    public LadderForm(PmEvent ev, PmMarket market, TradingSession session)
    {
        _event = ev; _market = market; _session = session;
        Text = $"{(market.GroupItemTitle ?? market.Question)} | {ev.Title}   {Fmt.Usd(market.Volume)}";
        ClientSize = new Size(520, 800); StartPosition = FormStartPosition.CenterParent;

        int x = 8;
        foreach (var (o, i) in market.Outcomes.Select((o, i) => (o, i)))
        {
            var rb = new RadioButton { Text = o.Name, Left = x, Top = 5, AutoSize = true, Checked = i == 0, Font = new Font("Tahoma", 9f, FontStyle.Bold), Appearance = Appearance.Button, Padding = new Padding(10, 1, 10, 1) };
            int idx = i; rb.CheckedChanged += (_, _) => { if (rb.Checked) Show(idx); };
            _bar.Controls.Add(rb); x += 70;
        }
        var centre = new Button { Text = "Centre", Left = x + 10, Top = 4, Width = 70, Height = 22, FlatStyle = FlatStyle.System };
        centre.Click += (_, _) => _ladder.Centre();
        _bar.Controls.Add(centre);

        _shares.Text = _market.MinOrderSize.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        _tools.Controls.Add(new Label { Text = "Shares", Left = 8, Top = 9, AutoSize = true });
        _tools.Controls.AddRange(new Control[] { _shares, _confirm, _cancelAll });
        _cancelAll.Click += async (_, _) => await CancelHere();
        _ladder.Order += async (price, side) => await PlaceAsync(price, side);

        Controls.Add(_ladder); Controls.Add(_tools); Controls.Add(_foot); Controls.Add(_bar); Controls.Add(_head);

        _stream = new MarketStream();
        _stream.BookChanged += id => { if (IsHandleCreated && !IsDisposed) BeginInvoke(() => OnBook(id)); };
        _stream.Status += s => { if (IsHandleCreated && !IsDisposed) BeginInvoke(() => _head.Text = Head() + (s == "connected" ? "" : "\n" + s)); };
        _poll.Tick += async (_, _) => await RefreshOrders();
        Shown += async (_, _) => await Load();
        FormClosed += async (_, _) => { _poll.Stop(); await _stream.DisposeAsync(); };
    }

    Outcome Current => _market.Outcomes[Math.Min(_shown, _market.Outcomes.Count - 1)];

    string Head() => $"{_market.Question}\nLast {Fmt.Cents(Current.Price ?? 0, _market.TickSize)}   Ticks {_market.TickSize}   Min {_market.MinOrderSize} shares";

    async Task Load()
    {
        _head.Text = Head();
        _stream.Start(_market.Outcomes.Select(o => o.TokenId).Where(t => t != ""));
        _whyNot = await _session.WhyNotAsync();
        SetTradingState();
        try
        {
            var b = await _session.Api.GetBookAsync(Current.TokenId);
            if (_stream.Book(b.TokenId) == null) _ladder.SetBook(b);       // the live stream will take over
        }
        catch (Exception ex) { _foot.Text = ex.Message; }
        if (_whyNot == "") { await RefreshOrders(); _poll.Start(); }
    }

    void SetTradingState()
    {
        bool can = _whyNot == "";
        _shares.Enabled = _confirm.Enabled = _cancelAll.Enabled = can;
        _foot.Text = can ? "Click the blue column to BUY at that price, the pink column to SELL." : _whyNot;
    }

    void Show(int i) { _shown = i; _head.Text = Head(); _ladder.SetBook(_stream.Book(Current.TokenId)); _ladder.Centre(); _ = RefreshOrders(); }

    void OnBook(string tokenId)
    {
        if (tokenId != Current.TokenId) return;
        var b = _stream.Book(tokenId);
        _ladder.SetBook(b);
        if (b != null) Text = $"{(_market.GroupItemTitle ?? _market.Question)}   {(b.BestBid is { } bid ? Fmt.Cents(bid, b.TickSize) : "-")} / {(b.BestAsk is { } ask ? Fmt.Cents(ask, b.TickSize) : "-")}";
    }

    async Task RefreshOrders()
    {
        if (_whyNot != "" || _busy) return;
        try { _ladder.SetOrders(await _session.Client.OpenOrdersAsync(Current.TokenId)); }
        catch (TradingBlockedException ex) { _whyNot = "Read-only. " + ex.Message; SetTradingState(); _poll.Stop(); }
        catch (Exception) { /* a missed refresh is harmless */ }
    }

    async Task PlaceAsync(decimal price, Side side)
    {
        if (_whyNot != "") { MessageBox.Show(this, _whyNot, "Read-only", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (_busy) return;
        if (!decimal.TryParse(_shares.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var shares) || shares <= 0)
        { _foot.Text = "Enter a number of shares."; return; }
        var tick = _stream.Book(Current.TokenId)?.TickSize ?? _market.TickSize;
        var (s, usd) = OrderBuilder.Round(price, shares, tick);
        var verb = side == Side.Buy ? "BUY" : "SELL";
        var text = side == Side.Buy
            ? $"BUY {s} {Current.Name} at {Fmt.Cents(price, tick)}c\nCosts {usd:0.00##} $ and pays {s:0.##} $ if {Current.Name} wins (profit {s - usd:0.00##} $)."
            : $"SELL {s} {Current.Name} at {Fmt.Cents(price, tick)}c\nYou receive {usd:0.00##} $ for shares you hold.";
        if (_confirm.Checked && MessageBox.Show(this, text, $"{verb} {Current.Name}", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        _busy = true;
        try
        {
            _foot.Text = "Sending...";
            var r = await _session.Client.PlaceLimitAsync(Current.TokenId, side, price, shares);
            _foot.Text = r.Success ? $"{verb} {s} @ {Fmt.Cents(price, tick)}c: {r.Status}" : "Rejected: " + r.Error;
        }
        catch (OrderRejectedException ex) { _foot.Text = ex.Message; }
        catch (TradingBlockedException ex) { _whyNot = "Read-only. " + ex.Message; SetTradingState(); }
        catch (Exception ex) { _foot.Text = "Failed: " + ex.Message; }
        finally { _busy = false; }
        await RefreshOrders();
    }

    async Task CancelHere()
    {
        if (_whyNot != "") return;
        try { _foot.Text = await _session.Client.CancelOutcomeAsync(Current.TokenId) ? "Cancelled my orders on " + Current.Name : "Could not cancel"; }
        catch (Exception ex) { _foot.Text = ex.Message; }
        await RefreshOrders();
    }
}
