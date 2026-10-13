using MurdocsPoly.Core;

namespace MurdocsPoly.Desktop;

/// <summary>
/// One outcome's order book as a ladder: price in cents in the grey column, the sizes people want to BUY at that price on the left (blue)
/// and the sizes people want to SELL on the right (pink). The mouse wheel scrolls; "Centre" jumps back to the best prices.
/// Read-only: there is no clicking to trade in this version.
/// </summary>
sealed class LadderControl : Control
{
    const int RowH = 18;
    static readonly Color Grey = Color.FromArgb(215, 215, 215), BidBack = Color.FromArgb(173, 214, 255), AskBack = Color.FromArgb(250, 200, 215),
        BidDeep = Color.FromArgb(120, 185, 250), AskDeep = Color.FromArgb(240, 150, 175);
    readonly Font _f = new("Tahoma", 8.25f), _b = new("Tahoma", 8.25f, FontStyle.Bold);
    OrderBook? _book;
    int _scroll;                       // rows the view is moved away from the middle (positive = towards higher prices)

    public LadderControl()
    {
        DoubleBuffered = true; BackColor = Color.White;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    public void SetBook(OrderBook? book) { _book = book; Invalidate(); }
    public void Centre() { _scroll = 0; Invalidate(); }

    protected override void OnMouseWheel(MouseEventArgs e) { _scroll += e.Delta > 0 ? 3 : -3; Invalidate(); }

    decimal CentrePrice(decimal tick) => _book?.Mid ?? _book?.BestBid ?? _book?.BestAsk ?? 0.5m;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Color.White);
        if (_book == null) { TextRenderer.DrawText(g, "Loading...", _f, new Point(8, 8), Color.Gray); return; }
        var tick = _book.TickSize;
        int rows = Math.Max(1, Height / RowH);
        var middle = PriceGrid.Snap(CentrePrice(tick), tick) + _scroll * tick;
        middle = PriceGrid.Snap(middle, tick);
        var prices = PriceGrid.Rows(middle, tick, rows / 2, rows - rows / 2 - 1);
        var maxSize = Math.Max(1m, Math.Max(_book.Bids.Values.DefaultIfEmpty(0).Max(), _book.Asks.Values.DefaultIfEmpty(0).Max()));
        int third = Width / 3, priceX = third, priceW = Width - 2 * third;
        for (int i = 0; i < prices.Count; i++)
        {
            var p = prices[i]; int y = i * RowH;
            var priceRect = new Rectangle(priceX, y, priceW, RowH);
            using (var br = new SolidBrush(Grey)) g.FillRectangle(br, priceRect);
            bool best = p == _book.BestBid || p == _book.BestAsk;
            TextRenderer.DrawText(g, Fmt.Cents(p, tick), best ? _b : _f, priceRect, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            if (_book.Bids.TryGetValue(p, out var bid)) DrawSize(g, new Rectangle(0, y, third, RowH), bid, maxSize, BidBack, BidDeep, true);
            if (_book.Asks.TryGetValue(p, out var ask)) DrawSize(g, new Rectangle(priceX + priceW, y, third, RowH), ask, maxSize, AskBack, AskDeep, false);
            using var line = new Pen(Color.FromArgb(235, 235, 235)); g.DrawLine(line, 0, y + RowH - 1, Width, y + RowH - 1);
        }
    }

    void DrawSize(Graphics g, Rectangle r, decimal size, decimal max, Color back, Color deep, bool alignRight)
    {
        using (var br = new SolidBrush(back)) g.FillRectangle(br, r);
        int w = (int)(r.Width * Math.Min(1m, size / max));
        using (var br = new SolidBrush(deep)) g.FillRectangle(br, alignRight ? new Rectangle(r.Right - w, r.Y, w, r.Height) : new Rectangle(r.X, r.Y, w, r.Height));
        TextRenderer.DrawText(g, size.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', ' '), _f, r, Color.Black,
            (alignRight ? TextFormatFlags.Right : TextFormatFlags.Left) | TextFormatFlags.VerticalCenter);
    }
}
