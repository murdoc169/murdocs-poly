using MurdocsPoly.Core;
using MurdocsPoly.Core.Trading;

namespace MurdocsPoly.Desktop;

/// <summary>
/// One outcome's order book as a ladder: price in cents in the grey column, the sizes people want to BUY at that price on the left (blue)
/// and the sizes people want to SELL on the right (pink). The mouse wheel scrolls; "Centre" jumps back to the best prices.
/// Clicking the left (blue) column of a row means BUY at that price, the right (pink) column means SELL at that price. Your own open
/// orders show as an orange marker with their size.
/// </summary>
sealed class LadderControl : Control
{
    const int RowH = 18;
    static readonly Color Grey = Color.FromArgb(215, 215, 215), BidBack = Color.FromArgb(173, 214, 255), AskBack = Color.FromArgb(250, 200, 215),
        BidDeep = Color.FromArgb(120, 185, 250), AskDeep = Color.FromArgb(240, 150, 175), Mine = Color.FromArgb(255, 190, 70);
    readonly Font _f = new("Tahoma", 8.25f), _b = new("Tahoma", 8.25f, FontStyle.Bold);
    OrderBook? _book;
    List<OpenOrder> _orders = new();
    List<decimal> _prices = new();
    int _scroll;                       // rows the view is moved away from the middle (positive = towards higher prices)

    /// <summary>Raised with the clicked price and the side the click means.</summary>
    public event Action<decimal, Side>? Order;

    public LadderControl()
    {
        DoubleBuffered = true; BackColor = Color.White;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    public void SetBook(OrderBook? book) { _book = book; Invalidate(); }
    public void SetOrders(IEnumerable<OpenOrder> orders) { _orders = orders.ToList(); Invalidate(); }
    public void Centre() { _scroll = 0; Invalidate(); }

    protected override void OnMouseWheel(MouseEventArgs e) { _scroll += e.Delta > 0 ? 3 : -3; Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _book == null) return;
        int row = e.Y / RowH, third = Width / 3;
        if (row < 0 || row >= _prices.Count) return;
        if (e.X < third) Order?.Invoke(_prices[row], Side.Buy);
        else if (e.X >= Width - third) Order?.Invoke(_prices[row], Side.Sell);
    }

    protected override void OnMouseMove(MouseEventArgs e) { Cursor = e.X < Width / 3 || e.X >= Width - Width / 3 ? Cursors.Hand : Cursors.Default; base.OnMouseMove(e); }

    decimal CentrePrice() => _book?.Mid ?? _book?.BestBid ?? _book?.BestAsk ?? 0.5m;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Color.White);
        if (_book == null) { TextRenderer.DrawText(g, "Loading...", _f, new Point(8, 8), Color.Gray); return; }
        var tick = _book.TickSize;
        int rows = Math.Max(1, Height / RowH);
        var middle = PriceGrid.Snap(PriceGrid.Snap(CentrePrice(), tick) + _scroll * tick, tick);
        _prices = PriceGrid.Rows(middle, tick, rows / 2, rows - rows / 2 - 1);
        var maxSize = Math.Max(1m, Math.Max(_book.Bids.Values.DefaultIfEmpty(0).Max(), _book.Asks.Values.DefaultIfEmpty(0).Max()));
        int third = Width / 3, priceX = third, priceW = Width - 2 * third;
        for (int i = 0; i < _prices.Count; i++)
        {
            var p = _prices[i]; int y = i * RowH;
            var priceRect = new Rectangle(priceX, y, priceW, RowH);
            using (var br = new SolidBrush(Grey)) g.FillRectangle(br, priceRect);
            bool best = p == _book.BestBid || p == _book.BestAsk;
            TextRenderer.DrawText(g, Fmt.Cents(p, tick), best ? _b : _f, priceRect, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            var bidRect = new Rectangle(0, y, third, RowH); var askRect = new Rectangle(priceX + priceW, y, third, RowH);
            if (_book.Bids.TryGetValue(p, out var bid)) DrawSize(g, bidRect, bid, maxSize, BidBack, BidDeep, true);
            if (_book.Asks.TryGetValue(p, out var ask)) DrawSize(g, askRect, ask, maxSize, AskBack, AskDeep, false);
            foreach (var o in _orders.Where(o => o.Price == p))
            {
                bool buy = o.Side.Equals("BUY", StringComparison.OrdinalIgnoreCase);
                var r = buy ? new Rectangle(bidRect.X, y, 44, RowH) : new Rectangle(askRect.Right - 44, y, 44, RowH);
                using (var br = new SolidBrush(Mine)) g.FillRectangle(br, r);
                TextRenderer.DrawText(g, (o.Size - o.Matched).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), _b, r, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
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
