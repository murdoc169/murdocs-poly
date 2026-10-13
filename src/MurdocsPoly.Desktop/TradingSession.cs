using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MurdocsPoly.Core;
using MurdocsPoly.Core.Trading;

namespace MurdocsPoly.Desktop;

/// <summary>The saved account (Windows-encrypted for this user on this PC). The private key is never written in plain text or sent anywhere.</summary>
static class AccountStore
{
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MurdocsPoly.Account.v1");
    static string FilePath => Path.Combine(Environment.GetEnvironmentVariable("MP_PROFILE_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MurdocsPoly"), "account.bin");

    public static Account? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var json = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser));
            return JsonSerializer.Deserialize<Account>(json);
        }
        catch (Exception) { return null; }
    }

    public static void Save(Account a)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllBytes(FilePath, ProtectedData.Protect(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(a)), Entropy, DataProtectionScope.CurrentUser));
    }

    public static void Forget() { try { File.Delete(FilePath); } catch (IOException) { } }
}

/// <summary>Shared by the windows: the account, the location gate and the (lazily created) trading client.</summary>
sealed class TradingSession : IDisposable
{
    public PolymarketClient Api { get; } = new();
    public TradeGuard Guard { get; }
    TradingClient? _client;

    public TradingSession() { Guard = TradeGuard.ForClient(Api); }

    public Account? Account { get; private set; } = AccountStore.Load();
    public bool Configured => Account != null;

    public void SetAccount(Account? a)
    {
        _client?.Dispose(); _client = null; Account = a;
        if (a == null) AccountStore.Forget(); else AccountStore.Save(a);
    }

    public TradingClient Client => _client ??= new TradingClient(Account ?? throw new InvalidOperationException("No account"), Api, Guard);

    /// <summary>Empty when trading is possible, otherwise the reason (no account, or Polymarket refuses this location).</summary>
    public async Task<string> WhyNotAsync()
    {
        if (Account == null) return "Read-only. Add your account (Account button) to trade.";
        try { await Guard.EnsureAllowedAsync(); return ""; }
        catch (TradingBlockedException ex) { return "Read-only. " + ex.Message; }
    }

    public void Dispose() { _client?.Dispose(); Api.Dispose(); }
}
