using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace MurdocsPoly.Core;

/// <summary>
/// Live order books over Polymarket's public market WebSocket (no login). Subscribe to token ids; it sends a full "book" first, then
/// "price_change" updates that set a level's new total size, and "tick_size_change" when a market's step changes.
/// It reconnects by itself and starts again from fresh books.
/// </summary>
public sealed class MarketStream : IAsyncDisposable
{
    public const string Url = "wss://ws-subscriptions-clob.polymarket.com/ws/market";
    readonly Dictionary<string, OrderBook> _books = new();
    readonly object _gate = new();
    readonly CancellationTokenSource _cts = new();
    Task? _loop;
    string[] _tokens = Array.Empty<string>();

    public event Action<string>? BookChanged;      // token id
    public event Action<string>? Status;

    public OrderBook? Book(string tokenId) { lock (_gate) return _books.GetValueOrDefault(tokenId); }

    public void Start(IEnumerable<string> tokenIds)
    {
        _tokens = tokenIds.Distinct().ToArray();
        _loop ??= Task.Run(() => Run(_cts.Token));
    }

    async Task Run(CancellationToken ct)
    {
        int wait = 1;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri(Url), ct);
                await Send(ws, JsonSerializer.Serialize(new { assets_ids = _tokens, type = "market" }), ct);
                Status?.Invoke("connected"); wait = 1;
                using var ping = new PeriodicTimer(TimeSpan.FromSeconds(10));
                _ = Task.Run(async () => { try { while (await ping.WaitForNextTickAsync(ct) && ws.State == WebSocketState.Open) await Send(ws, "PING", ct); } catch { } }, ct);
                var buf = new byte[1 << 16];
                while (ws.State == WebSocketState.Open)
                {
                    var sb = new StringBuilder(); WebSocketReceiveResult r;
                    do { r = await ws.ReceiveAsync(buf, ct); sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count)); } while (!r.EndOfMessage);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    Apply(sb.ToString());
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { Status?.Invoke("disconnected: " + ex.Message); }
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, wait *= 2)), ct); } catch (OperationCanceledException) { return; }
        }
    }

    static Task Send(ClientWebSocket ws, string text, CancellationToken ct) => ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, ct);

    /// <summary>Applies one message (an object or an array of them). Public so it can be tested with recorded messages.</summary>
    public void Apply(string message)
    {
        if (message is "PONG" or "") return;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(message); } catch (JsonException) { return; }
        using (doc)
        {
            var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : new List<JsonElement> { doc.RootElement };
            foreach (var m in items)
            {
                var type = Json.Str(m, "event_type");
                var changed = new HashSet<string>();
                lock (_gate)
                {
                    if (type == "book")
                    {
                        var fresh = Parse.Book(m);
                        if (_books.TryGetValue(fresh.TokenId, out var old)) fresh.TickSize = old.TickSize;
                        _books[fresh.TokenId] = fresh; changed.Add(fresh.TokenId);
                    }
                    else if (type == "price_change" && m.TryGetProperty("price_changes", out var pcs) && pcs.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var pc in pcs.EnumerateArray())
                        {
                            var id = Json.Str(pc, "asset_id");
                            if (!_books.TryGetValue(id, out var b)) continue;
                            b.SetLevel(Json.Str(pc, "side"), Json.Dec(pc, "price"), Json.Dec(pc, "size")); changed.Add(id);
                        }
                    }
                    else if (type == "tick_size_change")
                    {
                        var id = Json.Str(m, "asset_id");
                        var t = Json.Dec(m, "new_tick_size");
                        if (t > 0 && _books.TryGetValue(id, out var b)) { b.TickSize = t; changed.Add(id); }
                    }
                }
                foreach (var id in changed) BookChanged?.Invoke(id);
            }
        }
    }

    public async ValueTask DisposeAsync() { _cts.Cancel(); if (_loop != null) { try { await _loop; } catch { } } _cts.Dispose(); }
}
