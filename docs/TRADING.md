# Trading on Polymarket: how it works here

Orders are signed, not logged in.

1. **API credentials** (once per key): the key signs an EIP-712 `ClobAuth` message ("This message attests that I control the given wallet"); `GET /auth/derive-api-key` (or `POST /auth/api-key` the first time) returns an API key, secret and passphrase.
2. **Every request** carries an HMAC-SHA256 (L2) signature made with that secret over `timestamp + METHOD + path + body`.
3. **Every order** is also signed by the key as an EIP-712 `Order`. The exchange depends on the market: Protocol V2 (domain version 3), the CTF exchange, or the negative-risk CTF exchange (domain version 2). A Deposit wallet's signature is wrapped for ERC-7739 validation; Proxy, Safe and plain wallets use the plain signature.
4. **Amounts**: a buy of 10 shares at 0.52 is `makerAmount` 5 200 000 and `takerAmount` 10 000 000 (six decimals). Price, size and USD precision depend on the market's tick size and follow Polymarket's rounding rules.

Code: `src/MurdocsPoly.Core/Trading` (`Eip712`, `PolySigner`/`ClobAuth`, `OrderBuilder`, `TradingClient`, `TradeGuard`).

## The location gate

`TradeGuard` asks `https://polymarket.com/api/geoblock` and reuses the answer for two minutes. Credentials, orders, cancels and order listing all pass through it. If the answer is `blocked`, or the check fails, it throws and nothing is sent. Tests cover both cases, and that no HTTP request is made.

## Not built yet

Positions and balance panels, green-up (sell what you hold or buy the opposite outcome), market orders, automation rules, and the one-off wallet approvals for brand-new accounts (an account that has traded on polymarket.com already has them).
