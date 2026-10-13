# Murdoc's Poly

A ladder viewer for [Polymarket](https://polymarket.com), in the style of Murdoc's Toy (a Betfair ladder client). Clean-room, open source, MIT.

**Version 0.1: read-only.** It shows markets and live order books. It cannot place orders.

- **Navigator**: Sports, Politics, Crypto, Culture and Trending, then events, then markets with price in cents and volume (`1 302 212 $`). Search by text. Double-click a market (or press Enter) to open its ladder.
- **Ladder**: price in cents in the grey column, buy sizes on the left (blue), sell sizes on the right (pink), best prices in bold, depth bars, live updates over Polymarket's public WebSocket. Yes and No switch between the two mirrored books. Mouse wheel scrolls, *Centre* returns to the best prices.
- No account, wallet or key is needed to read.
- The footer says whether Polymarket allows trading from your location (it asks Polymarket's own location check).

## Trading

Not built, on purpose for now. Polymarket does not allow trading from some countries (the check currently reports Ireland as blocked), and this app will refuse to trade where Polymarket refuses. See [docs/TRADING.md](docs/TRADING.md) for what trading would need.

## Run

```
dotnet run --project src/MurdocsPoly.Desktop
dotnet test            # offline tests; MT_LIVE_POLYMARKET=1 adds a live API test
```

Needs the .NET 8 SDK on Windows. `--smoke` opens the first sports market and writes `smoke.txt` next to the program.
