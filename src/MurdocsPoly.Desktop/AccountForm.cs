using MurdocsPoly.Core.Trading;

namespace MurdocsPoly.Desktop;

/// <summary>Where the trading account is entered. The key stays on this PC, encrypted with your Windows login.</summary>
sealed class AccountForm : Form
{
    readonly TextBox _key = new() { UseSystemPasswordChar = true, Dock = DockStyle.Top };
    readonly TextBox _wallet = new() { Dock = DockStyle.Top };
    readonly ComboBox _type = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
    readonly Label _err = new() { Dock = DockStyle.Top, Height = 34, ForeColor = Color.Firebrick };
    static readonly (WalletType Type, string Label)[] Types =
    {
        (WalletType.DepositWallet, "Deposit wallet (accounts created since May 2026)"),
        (WalletType.PolyProxy, "Proxy wallet (older email or Google login)"),
        (WalletType.GnosisSafe, "Safe wallet (older account made with MetaMask or similar)"),
        (WalletType.Eoa, "Plain wallet (the key's own address holds the funds)"),
    };
    public Account? Result { get; private set; }
    public bool Removed { get; private set; }

    public AccountForm(Account? current)
    {
        Text = "Trading account"; ClientSize = new Size(480, 380); StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; Font = new Font("Tahoma", 8.25f);
        foreach (var t in Types) _type.Items.Add(t.Label);
        _type.SelectedIndex = Math.Max(0, Array.FindIndex(Types, t => t.Type == (current?.WalletType ?? WalletType.DepositWallet)));
        _key.Text = current?.PrivateKey ?? ""; _wallet.Text = current?.Wallet ?? "";

        var save = new Button { Text = "Save", Width = 90, Height = 28, DialogResult = DialogResult.None };
        var remove = new Button { Text = "Remove account", Width = 120, Height = 28, Enabled = current != null };
        var cancel = new Button { Text = "Cancel", Width = 80, Height = 28, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        buttons.Controls.AddRange(new Control[] { cancel, save, remove });
        save.Click += (_, _) => Save();
        remove.Click += (_, _) => { if (MessageBox.Show("Forget the saved key on this PC?", "Trading account", MessageBoxButtons.YesNo) == DialogResult.Yes) { Removed = true; DialogResult = DialogResult.OK; } };
        AcceptButton = save; CancelButton = cancel;

        Label L(string t, int h = 22) => new() { Text = t, Dock = DockStyle.Top, Height = h, Padding = new Padding(0, 6, 0, 0) };
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 0) };
        // docked Top: the last control added sits at the top
        body.Controls.Add(_err);
        body.Controls.Add(_type); body.Controls.Add(L("Wallet type"));
        body.Controls.Add(_wallet); body.Controls.Add(L("Wallet address (shown in your Polymarket profile menu)"));
        body.Controls.Add(_key); body.Controls.Add(L("Private key of the signer (from your Polymarket account settings, 64 hex characters)"));
        body.Controls.Add(new Label { Dock = DockStyle.Top, Height = 54, ForeColor = Color.DimGray,
            Text = "Use a separate account with only what you are willing to risk. The key is stored encrypted for this Windows user and is only used to sign orders on this PC. Trading is refused automatically where Polymarket does not allow it." });
        Controls.Add(body); Controls.Add(buttons);
    }

    void Save()
    {
        try
        {
            var wallet = _wallet.Text.Trim();
            if (wallet.Length != 42 || !wallet.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || !wallet[2..].All(Uri.IsHexDigit)) throw new ArgumentException("The wallet address is 0x followed by 40 hexadecimal characters.");
            var key = _key.Text.Trim();
            _ = new PolySigner(key);                          // checks the key
            Result = new Account(key, wallet, Types[_type.SelectedIndex].Type);
            DialogResult = DialogResult.OK;
        }
        catch (ArgumentException ex) { _err.Text = ex.Message; }
    }
}
