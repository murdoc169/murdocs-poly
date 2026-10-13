using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using MurdocsPoly.Core;
using MurdocsPoly.Core.Trading;

namespace MurdocsPoly.Desktop;

/// <summary>
/// The main window: a web page (ui/) that looks like Polymarket's live sports page, hosted in WebView2. The page asks this form for data and
/// to trade through a small message bridge; everything that touches Polymarket, the key or the location check stays in C#.
/// </summary>
sealed class MainForm : Form
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    readonly TradingSession _session = new();
    readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(12, 14, 19) };
    readonly Dictionary<string, (PmEvent Event, PmMarket Market)> _markets = new();
    readonly Dictionary<string, PmEvent> _events = new();
    readonly string? _smokeShot;
    readonly bool _smoke;

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins m);
    [DllImport("user32.dll")] static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [StructLayout(LayoutKind.Sequential)] struct Margins { public int L, R, T, B; }
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct NcCalcSize { public Rect R0, R1, R2; public IntPtr Pos; }

    const int ResizeStrip = 5;   // a few pixels of form above the page so the top edge can be grabbed to resize

    /// <summary>No title bar: the page draws its own header and window buttons. Keeping the normal window frame styles keeps resizing, snapping and the shadow.</summary>
    protected override void WndProc(ref Message m)
    {
        const int WM_NCCALCSIZE = 0x83, WM_NCHITTEST = 0x84;
        if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
        {
            var p = Marshal.PtrToStructure<NcCalcSize>(m.LParam);
            int top = p.R0.Top;
            base.WndProc(ref m);                                  // sides and bottom as normal
            p = Marshal.PtrToStructure<NcCalcSize>(m.LParam);
            p.R0.Top = top;                                       // but no title bar
            if (WindowState == FormWindowState.Maximized)         // a maximized window overhangs the screen by the frame width
            {
                uint dpi = GetDpiForWindow(Handle);
                p.R0.Top += GetSystemMetricsForDpi(33, dpi) + GetSystemMetricsForDpi(92, dpi);   // SM_CYSIZEFRAME + SM_CXPADDEDBORDER
            }
            Marshal.StructureToPtr(p, m.LParam, false);
            m.Result = IntPtr.Zero;
            return;
        }
        if (m.Msg == WM_NCHITTEST)
        {
            base.WndProc(ref m);
            if (m.Result == (IntPtr)1 && WindowState == FormWindowState.Normal)   // HTCLIENT: the strip above the page is the top resize edge
            {
                var pt = PointToClient(new Point((short)(m.LParam.ToInt64() & 0xffff), (short)((m.LParam.ToInt64() >> 16) & 0xffff)));
                if (pt.Y < ResizeStrip) m.Result = (IntPtr)(pt.X < 8 ? 13 : pt.X > ClientSize.Width - 8 ? 14 : 12);   // HTTOPLEFT / HTTOPRIGHT / HTTOP
            }
            return;
        }
        base.WndProc(ref m);
    }

    public MainForm(bool smoke, string? shot)
    {
        _smoke = smoke; _smokeShot = shot;
        Text = "Murdoc's Poly"; BackColor = Color.FromArgb(12, 14, 19);
        ClientSize = new Size(1500, 960); StartPosition = FormStartPosition.CenterScreen;
        Padding = new Padding(0, ResizeStrip, 0, 0);
        Controls.Add(_web);
        HandleCreated += (_, _) =>
        {
            int on = 1; DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int));
            var mg = new Margins { T = 1 }; DwmExtendFrameIntoClientArea(Handle, ref mg);          // keeps the drop shadow
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x27);                                    // recalculate the frame now (no move, size, z-order)
        };
        Shown += async (_, _) => await Start();
        FormClosed += (_, _) => _session.Dispose();
    }

    async Task Start()
    {
        var profile = Environment.GetEnvironmentVariable("MP_PROFILE_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MurdocsPoly");
        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(profile, "webview"));
        await _web.EnsureCoreWebView2Async(env);
        var core = _web.CoreWebView2;
        core.Settings.IsNonClientRegionSupportEnabled = true; core.Settings.AreDevToolsEnabled = false; core.Settings.IsStatusBarEnabled = false; core.Settings.AreDefaultContextMenusEnabled = false;
        core.SetVirtualHostNameToFolderMapping("poly.local", Path.Combine(AppContext.BaseDirectory, "ui"), CoreWebView2HostResourceAccessKind.Allow);
        core.WebMessageReceived += async (_, e) => await OnMessage(e.WebMessageAsJson);
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.Navigate("https://poly.local/index.html");
        if (_smoke) _ = Smoke();
    }

    async Task OnMessage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var id = root.GetProperty("id").GetInt32();
        object reply;
        try
        {
            var data = await Dispatch(root.GetProperty("cmd").GetString()!, root.GetProperty("args"));
            reply = new { id, ok = true, data };
        }
        catch (Exception ex) { reply = new { id, ok = false, error = ex.Message }; }
        if (!IsDisposed) _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(reply, Json));
    }

    async Task<object?> Dispatch(string cmd, JsonElement a)
    {
        switch (cmd)
        {
            case "cards": return await Cards(a);
            case "status": return new { why = await _session.WhyNotAsync() };
            case "account":
                {
                    using var f = new AccountForm(_session.Account);
                    if (f.ShowDialog(this) == DialogResult.OK) _session.SetAccount(f.Removed ? null : f.Result);
                    return new { why = await _session.WhyNotAsync() };
                }
            case "ladder":
                {
                    var marketId = a.GetProperty("marketId").GetString() ?? "";
                    if (!_markets.TryGetValue(marketId, out var hit)) throw new InvalidOperationException("That market is not loaded any more.");
                    new LadderForm(hit.Event, hit.Market, _session).Show(this);
                    return new { };
                }
            case "trade": return await Trade(a);
            case "win":
                switch (a.GetProperty("op").GetString())
                {
                    case "min": WindowState = FormWindowState.Minimized; break;
                    case "max": WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; break;
                    case "close": Close(); break;
                }
                return new { max = WindowState == FormWindowState.Maximized };
            default: throw new InvalidOperationException("Unknown command " + cmd);
        }
    }

    async Task<object> Cards(JsonElement a)
    {
        string nav = a.GetProperty("nav").GetString() ?? "sports", view = a.GetProperty("view").GetString() ?? "live", query = a.GetProperty("query").GetString() ?? "";
        List<PmEvent> events; List<Card> cards;
        if (query != "") { events = await _session.Api.SearchAsync(query, 24); cards = events.Select(CardBuilder.Plain).ToList(); }
        else if (nav == "sports" && view == "live") { events = await _session.Api.ListLiveGamesAsync(); cards = CardBuilder.LiveGames(events); }
        else
        {
            events = await _session.Api.ListEventsAsync(nav == "trending" ? null : nav, 40);
            cards = events.Select(CardBuilder.Plain).Where(c => c.Rows.Count > 0).ToList();
        }
        foreach (var e in events) { _events[e.Id] = e; foreach (var m in e.Markets) _markets[m.Id] = (e, m); }
        return new { cards };
    }

    /// <summary>"Market" here means a limit order at the best price on the other side of the book, which fills at once while that price is there.</summary>
    async Task<object> Trade(JsonElement a)
    {
        var marketId = a.GetProperty("marketId").GetString() ?? "";
        int index = a.GetProperty("index").GetInt32();
        bool buy = a.GetProperty("side").GetString() == "buy", limit = a.GetProperty("mode").GetString() == "limit";
        decimal amount = a.GetProperty("amount").GetDecimal(), limitCents = a.GetProperty("limitCents").GetDecimal();
        if (!_markets.TryGetValue(marketId, out var hit)) return new { ok = false, text = "That market is not loaded any more." };
        var why = await _session.WhyNotAsync();
        if (why != "") return new { ok = false, text = why };
        if (amount <= 0) return new { ok = false, text = "Enter an amount." };

        var outcome = hit.Market.Outcomes[index];
        var book = await _session.Api.GetBookAsync(outcome.TokenId);
        decimal? price = limit ? (limitCents > 0 ? PriceGrid.Snap(limitCents / 100m, book.TickSize) : null) : (buy ? book.BestAsk : book.BestBid);
        if (price == null) return new { ok = false, text = limit ? "Enter a limit price." : "There is no price on that side of the book right now." };
        bool marketBuy = buy && !limit;     // a dollar amount, filled or killed at the best ask (1 $ minimum), like the site's Market button
        var (shares, usd) = marketBuy ? (Math.Floor(amount * 100m / price.Value) / 100m, Math.Floor(amount * 100m) / 100m) : OrderBuilder.Round(price.Value, amount / price.Value, book.TickSize);
        if (marketBuy && usd < 1m) return new { ok = false, text = "The smallest market order is 1 $." };
        if (!marketBuy && shares < book.MinOrderSize) return new { ok = false, text = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"That is {shares} shares; the smallest order is {book.MinOrderSize}.") };

        var text = buy
            ? $"BUY {shares} \"{outcome.Name}\" at {Fmt.Cents(price.Value, book.TickSize)}c\n{hit.Market.GroupItemTitle ?? hit.Market.Question}\n\nCosts {usd:0.00##} $, pays {shares:0.##} $ if it wins."
            : $"SELL {shares} \"{outcome.Name}\" at {Fmt.Cents(price.Value, book.TickSize)}c\n{hit.Market.GroupItemTitle ?? hit.Market.Question}\n\nYou receive {usd:0.00##} $ for shares you hold.";
        if (MessageBox.Show(this, text, buy ? "Buy" : "Sell", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return new { ok = false, text = "Cancelled." };
        try
        {
            var r = marketBuy ? await _session.Client.PlaceMarketBuyAsync(outcome.TokenId, price.Value, usd)
                              : await _session.Client.PlaceLimitAsync(outcome.TokenId, buy ? Side.Buy : Side.Sell, price.Value, shares);
            return new { ok = r.Success, text = r.Success ? $"{(buy ? "Bought" : "Sold")} order {r.Status}: {shares} at {Fmt.Cents(price.Value, book.TickSize)}c" : "Rejected: " + r.Error };
        }
        catch (OrderRejectedException ex) { return new { ok = false, text = ex.Message }; }
        catch (TradingBlockedException ex) { return new { ok = false, text = "Read-only. " + ex.Message }; }
    }

    /// <summary>--smoke: let the page load, count the cards and (with --shot=file.png) save a picture of the page.</summary>
    async Task Smoke()
    {
        await Task.Delay(9000);
        string result;
        try
        {
            if (Environment.GetEnvironmentVariable("MP_TEST_JS") is { Length: > 0 } js) { await _web.CoreWebView2.ExecuteScriptAsync(js); await Task.Delay(3000); }       // test seam: drive the page before the picture
            var cards = await _web.CoreWebView2.ExecuteScriptAsync("document.querySelectorAll('.card').length");
            var first = await _web.CoreWebView2.ExecuteScriptAsync("document.querySelector('.card')?.innerText?.slice(0,80) ?? ''");
            result = $"cards={cards} first={first} status={await _web.CoreWebView2.ExecuteScriptAsync("document.getElementById('status').textContent")}";
            if (_smokeShot != null)
            {
                using var fs = File.Create(_smokeShot);
                await _web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs);
            }
        }
        catch (Exception ex) { result = "failed: " + ex; }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke.txt"), result);
        Close();
    }
}
