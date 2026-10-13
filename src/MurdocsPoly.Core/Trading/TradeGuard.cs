namespace MurdocsPoly.Core.Trading;

/// <summary>Raised instead of sending anything when Polymarket does not allow trading from this location (or that cannot be checked).</summary>
public sealed class TradingBlockedException : Exception { public TradingBlockedException(string message) : base(message) { } }

/// <summary>
/// The location gate. Every account call passes through it, and there is no setting to turn it off: if Polymarket's own check says this
/// address is blocked, or the check cannot be completed, nothing is sent. The answer is reused for two minutes.
/// </summary>
public sealed class TradeGuard
{
    readonly Func<Task<Geoblock>> _check;
    readonly Func<DateTime> _now;
    Geoblock? _last; DateTime _at;

    public TradeGuard(Func<Task<Geoblock>> check, Func<DateTime>? now = null) { _check = check; _now = now ?? (() => DateTime.UtcNow); }

    public static TradeGuard ForClient(PolymarketClient c) => new(c.GeoblockAsync);

    public async Task EnsureAllowedAsync()
    {
        if (_last == null || (_now() - _at).TotalMinutes >= 2)
        {
            try { _last = await _check(); _at = _now(); }
            catch (Exception ex) { _last = null; throw new TradingBlockedException("Could not check whether Polymarket allows trading here, so nothing was sent (" + ex.Message + ")."); }
        }
        if (_last.Blocked) throw new TradingBlockedException($"Polymarket does not allow trading from your location ({_last.Country ?? "unknown"}), so nothing was sent.");
    }
}
