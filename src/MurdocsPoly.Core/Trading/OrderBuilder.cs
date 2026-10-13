using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace MurdocsPoly.Core.Trading;

public enum Side { Buy = 0, Sell = 1 }

/// <summary>Why an order was refused before it was sent.</summary>
public sealed class OrderRejectedException : Exception { public OrderRejectedException(string message) : base(message) { } }

/// <summary>A signed order ready to send, with the exact JSON body that was signed over (the HMAC must use these exact bytes).</summary>
public sealed record SignedOrder(string Body, decimal Price, decimal Shares, decimal Usd);

/// <summary>
/// Builds and signs Polymarket limit orders as the protocol documents them (EIP-712 "Order", with the ERC-7739 wrapper for Deposit Wallets).
/// Pure: no network, so the arithmetic and the signature can be tested.
/// </summary>
public static class OrderBuilder
{
    public const string V2Exchange = "0xe3333700cA9d93003F00f0F71f8515005F6c00Aa", CtfExchange = "0xE111180000d2663C0091e4f400237545B87B996B", NegRiskExchange = "0xe2222d279d744050d28e00520010520000310F59";
    public const string Zero32 = "0x0000000000000000000000000000000000000000000000000000000000000000";
    const string OrderType = "Order(uint256 salt,address maker,address signer,uint256 tokenId,uint256 makerAmount,uint256 takerAmount,uint8 side,uint8 signatureType,uint256 timestamp,bytes32 metadata,bytes32 builder)";

    /// <summary>Decimals allowed for price, size and the USD amount for a tick size.</summary>
    public static (int Price, int Size, int Amount) Decimals(decimal tick) => tick switch
    {
        0.1m => (1, 2, 3),
        0.01m => (2, 2, 4),
        0.005m => (3, 2, 5),
        0.0025m => (4, 2, 6),
        0.001m => (3, 2, 5),
        0.0001m => (4, 2, 6),
        _ => throw new OrderRejectedException($"Unknown tick size {tick}"),
    };

    static decimal Pow10(int dec) { decimal f = 1; for (int i = 0; i < dec; i++) f *= 10; return f; }
    static decimal Floor(decimal v, int dec) => Math.Floor(v * Pow10(dec)) / Pow10(dec);
    static decimal Ceil(decimal v, int dec) => Math.Ceiling(v * Pow10(dec)) / Pow10(dec);

    /// <summary>The share count (rounded down) and USD amount the order is for, following the documented rounding.</summary>
    public static (decimal Shares, decimal Usd) Round(decimal price, decimal shares, decimal tick)
    {
        var (_, sizeDec, amountDec) = Decimals(tick);
        var s = Floor(shares, sizeDec);
        var usd = price * s;
        if (Math.Round(usd, amountDec) != usd) usd = Floor(Ceil(usd, amountDec + 4), amountDec);
        return (s, usd);
    }

    static BigInteger Micro(decimal v) => new(decimal.Round(v * 1_000_000m, 0, MidpointRounding.AwayFromZero));

    public static string ExchangeFor(OrderBook b) => b.Version == "v2" ? V2Exchange : b.NegRisk ? NegRiskExchange : CtfExchange;
    public static string DomainVersionFor(OrderBook b) => b.Version == "v2" ? "3" : "2";

    /// <summary>Validates, signs and serialises a GTC (or, with an expiry, GTD) limit order. Salt and time are inputs so tests are exact.</summary>
    public static SignedOrder BuildLimit(PolySigner signer, Account account, OrderBook book, Side side, decimal price, decimal shares,
        string apiKey, long salt, long nowMs, long? expirationUnixSeconds = null)
    {
        var tick = book.TickSize;
        var (priceDec, _, _) = Decimals(tick);
        if (price < tick || price > 1 - tick) throw new OrderRejectedException($"The price must be between {tick} and {1 - tick}.");
        if (Math.Round(price, priceDec) != price || price % tick != 0) throw new OrderRejectedException($"The price {price} is not a multiple of the tick {tick}.");
        var (s, usd) = Round(price, shares, tick);
        if (s < book.MinOrderSize) throw new OrderRejectedException($"The smallest order is {book.MinOrderSize} shares.");
        if (account.WalletType == WalletType.Eoa && !string.Equals(account.Wallet, signer.Address, StringComparison.OrdinalIgnoreCase))
            throw new OrderRejectedException("For a plain wallet the wallet address must be the key's own address.");

        var makerAmount = side == Side.Buy ? Micro(usd) : Micro(s);
        var takerAmount = side == Side.Buy ? Micro(s) : Micro(usd);
        var maker = account.Wallet;
        var orderSigner = account.WalletType is WalletType.DepositWallet or WalletType.Eoa ? account.Wallet : signer.Address;
        var tokenId = BigInteger.Parse(book.TokenId, CultureInfo.InvariantCulture);
        int sigType = (int)account.WalletType;

        TypedStruct Order() => new("Order",
            new TypedField("uint256", "salt", new BigInteger(salt)), new TypedField("address", "maker", maker), new TypedField("address", "signer", orderSigner),
            new TypedField("uint256", "tokenId", tokenId), new TypedField("uint256", "makerAmount", makerAmount), new TypedField("uint256", "takerAmount", takerAmount),
            new TypedField("uint8", "side", new BigInteger((int)side)), new TypedField("uint8", "signatureType", new BigInteger(sigType)),
            new TypedField("uint256", "timestamp", new BigInteger(nowMs)), new TypedField("bytes32", "metadata", Zero32), new TypedField("bytes32", "builder", Zero32));

        var domain = Eip712.Domain("Polymarket CTF Exchange", DomainVersionFor(book), ClobAuth.ChainId, ExchangeFor(book));
        byte[] signature;
        if (account.WalletType == WalletType.DepositWallet)
        {
            var wrapper = new TypedStruct("TypedDataSign",
                new TypedField("Order", "contents", Order()), new TypedField("string", "name", "DepositWallet"), new TypedField("string", "version", "1"),
                new TypedField("uint256", "chainId", new BigInteger(ClobAuth.ChainId)), new TypedField("address", "verifyingContract", maker), new TypedField("bytes32", "salt", Zero32));
            var inner = signer.SignDigest(Eip712.Digest(domain, wrapper));
            var typeBytes = Encoding.UTF8.GetBytes(OrderType);
            var all = new List<byte>(inner);
            all.AddRange(Eip712.HashStruct(domain)); all.AddRange(Eip712.HashStruct(Order())); all.AddRange(typeBytes);
            all.Add((byte)(typeBytes.Length >> 8)); all.Add((byte)(typeBytes.Length & 0xff));
            signature = all.ToArray();
        }
        else signature = signer.SignDigest(Eip712.Digest(domain, Order()));

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteBoolean("deferExec", false);
            w.WriteStartObject("order");
            w.WriteString("builder", Zero32);
            w.WriteString("expiration", (expirationUnixSeconds ?? 0).ToString(CultureInfo.InvariantCulture));
            w.WriteString("maker", maker);
            w.WriteString("makerAmount", makerAmount.ToString(CultureInfo.InvariantCulture));
            w.WriteString("metadata", Zero32);
            w.WriteNumber("salt", salt);
            w.WriteString("side", side == Side.Buy ? "BUY" : "SELL");
            w.WriteString("signature", Eip712.ToHex(signature));
            w.WriteNumber("signatureType", sigType);
            w.WriteString("signer", orderSigner);
            w.WriteString("takerAmount", takerAmount.ToString(CultureInfo.InvariantCulture));
            w.WriteString("timestamp", nowMs.ToString(CultureInfo.InvariantCulture));
            w.WriteString("tokenId", book.TokenId);
            w.WriteEndObject();
            w.WriteString("orderType", expirationUnixSeconds == null ? "GTC" : "GTD");
            w.WriteString("owner", apiKey);
            w.WriteEndObject();
        }
        return new SignedOrder(Encoding.UTF8.GetString(ms.ToArray()), price, s, usd);
    }
}
