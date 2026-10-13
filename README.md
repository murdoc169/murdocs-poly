# Murdoc's Poly

A ladder trading client for [Polymarket](https://polymarket.com), in the style of Murdoc's Toy (a Betfair ladder client). Clean-room, open source, MIT.

- **Main window** modelled on Polymarket's *Sports > Live* page: dark theme, Inter font, top bar with search and account, category tabs, a sports sidebar, and a card per live game with the score, team logos, records and **moneyline / spread / total** price buttons in the teams' colours. Other tabs (Trending, Politics, Crypto, ...) show their events as cards with Yes / No buttons. Prices refresh every 12 seconds. Logos and team colours come from Polymarket's public data; there is no copied artwork or code.
- **Trade panel** (right): pick a price on any card, then Buy / Sell, *Market* (an order at the best price) or *Limit*, an amount with +$1 / +$5 / +$10 / +$100 chips, and *Trade*. The book icon on a card opens the full **ladder**.
- **Ladder** (book icon): price in cents in the grey column, buy sizes on the left (blue), sell sizes on the right (pink), best prices in bold, depth bars, live updates over Polymarket's public WebSocket. Yes and No switch between the two mirrored books.
- **Ladder trading**: click the blue column of a row to **buy** at that price, the pink column to **sell**. Set the number of shares in the box under the ladder; a confirmation shows the cost and the payout (switch it off with *Confirm*). Your open orders show as orange markers with their remaining size, and *Cancel my orders here* clears them for the outcome.
- Reading prices needs no account, wallet or key.

## Trading account

The **Account** button takes the wallet address (shown in your Polymarket profile menu), the private key of the signer, and the wallet type: a Deposit wallet (accounts created since May 2026), a Proxy wallet (older email or Google logins), a Safe wallet (older MetaMask-style accounts) or a plain wallet. The key is stored encrypted for your Windows user and is only used on this PC to sign. Use a separate account with only what you are ready to risk.

### Where it works

Polymarket does not allow trading from some countries. Every account call in this app first asks Polymarket's own location check (`polymarket.com/api/geoblock`). If it says *blocked*, or cannot be checked, **nothing is sent** and the ladder stays read-only with the reason in the footer. There is no setting to turn this off. Prices can still be read from anywhere.

### How much has been verified

- Signing is checked against the EIP-712 specification's own test vector (digest and signature), and the order, L1 and L2 signatures are checked by recovering the signer from them.
- The order layout, amounts and rounding, exchange selection (Protocol V2, CTF and negative-risk) and the Deposit wallet signature wrapper follow Polymarket's current documentation.
- **Not yet verified against the live order endpoint**: the author is in a country where Polymarket blocks trading, so no order has ever been sent. Your first orders should be the smallest allowed (5 shares), and bug reports are welcome.

## Run

```
dotnet run --project src/MurdocsPoly.Desktop
dotnet test            # offline tests; MT_LIVE_POLYMARKET=1 adds a live read-only API test
```

Needs the .NET 8 SDK on Windows. `--smoke` opens the first sports market and writes `smoke.txt` next to the program.
