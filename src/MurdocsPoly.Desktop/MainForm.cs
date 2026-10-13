using MurdocsPoly.Core;

namespace MurdocsPoly.Desktop;

/// <summary>
/// The navigator: categories > events > markets. Open a market (double-click or Enter) for its ladder. Everything here is read-only.
/// </summary>
sealed class MainForm : Form
{
    static readonly (string Name, string? Tag)[] Categories =
    {
        ("Sports", "sports"), ("Politics", "politics"), ("Crypto", "crypto"), ("Culture", "culture"), ("Trending", null),
    };
    const string Loading = "loading";

    readonly PolymarketClient _api = new();
    readonly TreeView _tree = new() { Dock = DockStyle.Fill, Font = new Font("Tahoma", 8.25f), HideSelection = false, ShowLines = true, FullRowSelect = true };
    readonly TextBox _search = new() { Dock = DockStyle.Top, Font = new Font("Tahoma", 9f), PlaceholderText = "Search events, press Enter" };
    readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 36, Padding = new Padding(6, 3, 6, 0), Font = new Font("Tahoma", 8.25f), ForeColor = Color.DimGray };
    readonly bool _smoke;
    string _tradingNote = "Checking whether trading is allowed from here...";
    readonly Dictionary<string, PmEvent> _events = new();

    public MainForm(bool smoke)
    {
        _smoke = smoke;
        Text = "Murdoc's Poly"; ClientSize = new Size(420, 760); StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(_tree); Controls.Add(_status); Controls.Add(_search);
        foreach (var (name, tag) in Categories) _tree.Nodes.Add(Placeholder(new TreeNode(name) { Tag = tag ?? "" , NodeFont = new Font("Tahoma", 8.25f, FontStyle.Bold) }));
        _tree.BeforeExpand += async (_, e) => await Fill(e.Node!);
        _tree.NodeMouseDoubleClick += (_, e) => OpenMarket(e.Node);
        _tree.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) OpenMarket(_tree.SelectedNode); };
        _search.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await Search(_search.Text.Trim()); } };
        Shown += async (_, _) => { await CheckLocation(); if (_smoke) await Smoke(); };
    }

    static TreeNode Placeholder(TreeNode n) { n.Nodes.Add(new TreeNode("Loading...") { Name = Loading }); return n; }

    async Task CheckLocation()
    {
        try
        {
            var g = await _api.GeoblockAsync();
            _tradingNote = g.Blocked
                ? $"Read-only. Polymarket does not allow trading from your location ({g.Country ?? "unknown"})."
                : "Read-only in this version.";
        }
        catch (Exception) { _tradingNote = "Read-only in this version."; }
        _status.Text = _tradingNote;
    }

    async Task Fill(TreeNode n)
    {
        if (n.Nodes.Count != 1 || n.Nodes[0].Name != Loading) return;
        try
        {
            if (n.Tag is string tag && n.Level == 0)
            {
                _status.Text = "Loading...";
                var events = await _api.ListEventsAsync(tag == "" ? null : tag, limit: 60);
                n.Nodes.Clear();
                foreach (var e in events) n.Nodes.Add(EventNode(e));
                _status.Text = _tradingNote;
            }
            else if (n.Tag is PmEvent ev)
            {
                n.Nodes.Clear();
                foreach (var m in ev.Markets.Where(m => m.Active && !m.Closed && m.Outcomes.Count > 0 && m.Outcomes[0].TokenId != "").OrderByDescending(m => m.Volume))
                    n.Nodes.Add(new TreeNode(MarketText(m)) { Tag = m });
                if (n.Nodes.Count == 0) n.Nodes.Add(new TreeNode("(no open markets)"));
            }
        }
        catch (Exception ex) { n.Nodes.Clear(); Placeholder(n); _status.Text = ex.Message; }
    }

    TreeNode EventNode(PmEvent e)
    {
        _events[e.Id] = e;
        var node = new TreeNode($"{e.Title}  ({Fmt.Usd(e.Volume)})") { Tag = e };
        if (e.Markets.Count == 1) node.Tag = e;   // still expandable: the single market is inside
        return Placeholder(node);
    }

    static string MarketText(PmMarket m)
    {
        var first = m.Outcomes[0];
        var price = first.Price is { } p ? $"  {Fmt.Cents(p, m.TickSize)}c {first.Name}" : "";
        return $"{m.GroupItemTitle ?? m.Question}{price}  ({Fmt.Usd(m.Volume)})";
    }

    async Task Search(string text)
    {
        if (text == "") return;
        try
        {
            _status.Text = "Searching...";
            var found = await _api.SearchAsync(text);
            var root = new TreeNode($"Search: {text}") { NodeFont = new Font("Tahoma", 8.25f, FontStyle.Bold) };
            foreach (var e in found) root.Nodes.Add(EventNode(e));
            _tree.Nodes.Insert(0, root); root.Expand(); _tree.SelectedNode = root;
            _status.Text = found.Count == 0 ? "Nothing found" : _tradingNote;
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    void OpenMarket(TreeNode? n)
    {
        if (n?.Tag is not PmMarket m || n.Parent?.Tag is not PmEvent ev) return;
        new LadderForm(ev, m, _tradingNote).Show(this);
    }

    /// <summary>--smoke: open the first market of the first sports event and write what was found next to the program.</summary>
    async Task Smoke()
    {
        string result;
        try
        {
            _tree.Nodes[0].Expand(); await Fill(_tree.Nodes[0]);
            var ev = _tree.Nodes[0].Nodes.Cast<TreeNode>().First(x => x.Tag is PmEvent);
            ev.Expand(); await Fill(ev);
            var m = ev.Nodes.Cast<TreeNode>().First(x => x.Tag is PmMarket);
            OpenMarket(m);
            await Task.Delay(4000);
            result = $"ok | {ev.Text} | {m.Text} | {_tradingNote}";
        }
        catch (Exception ex) { result = "failed: " + ex.Message; }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "smoke.txt"), result);
        Close();
    }
}
