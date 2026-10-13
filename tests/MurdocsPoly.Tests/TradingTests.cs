using System.Numerics;
using System.Text.Json;
using MurdocsPoly.Core;
using MurdocsPoly.Core.Trading;
using Xunit;

namespace MurdocsPoly.Tests;

public class TradingTests
{
    static readonly string CowKey = Eip712.ToHex(Eip712.Keccak("cow"));

    [Fact]
    public void Eip712MatchesTheSpecificationExample()
    {
        // the "Ether Mail" example from EIP-712 itself
        var domain = Eip712.Domain("Ether Mail", "1", 1, "0xCcCCccccCCCCcCCCCCCcCcCccCcCCCcCcccccccC");
        var cow = new TypedStruct("Person", new TypedField("string", "name", "Cow"), new TypedField("address", "wallet", "0xCD2a3d9F938E13CD947Ec05AbC7FE734Df8DD826"));
        var bob = new TypedStruct("Person", new TypedField("string", "name", "Bob"), new TypedField("address", "wallet", "0xbBbBBBBbbBBBbbbBbbBbbbbBBbBbbbbBbBbbBBbB"));
        var mail = new TypedStruct("Mail", new TypedField("Person", "from", cow), new TypedField("Person", "to", bob), new TypedField("string", "contents", "Hello, Bob!"));

        Assert.Equal("Mail(Person from,Person to,string contents)Person(string name,address wallet)", Eip712.EncodeType(mail));
        var digest = Eip712.Digest(domain, mail);
        Assert.Equal("0xbe609aee343fb3c4b28e1df9e632fca64fcfaede20f02e86244efddf30957bd2", Eip712.ToHex(digest));

        var signer = new PolySigner(CowKey);
        Assert.Equal("0xcd2a3d9f938e13cd947ec05abc7fe734df8dd826", signer.Address);
        var sig = Eip712.ToHex(signer.SignDigest(digest));
        Assert.Equal("0x4355c47d63924e8a72e509b65029052eb6c299d53a04e167c5775fd466751c9d07299936d304c153f6443dfa05f40ff007d72911b6f72307f996231605b915621c", sig);
        Assert.Equal(signer.Address, PolySigner.RecoverAddress(digest, signer.SignDigest(digest)));
    }

    [Fact]
    public void PrivateKeysAreChecked() => Assert.Throws<ArgumentException>(() => new PolySigner("not a key"));

    [Fact]
    public void L2SignatureIsUrlSafeBase64OfTheHmac()
    {
        var secret = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 250, 251, 252 });
        var a = ClobAuth.L2Signature(secret, "1700000000", "post", "/order", "{\"a\":1}");
        Assert.DoesNotContain('+', a); Assert.DoesNotContain('/', a);
        Assert.Equal(44, a.Length);                                      // 32 bytes -> 44 characters with padding
        Assert.Equal(a, ClobAuth.L2Signature(secret.Replace('+', '-').Replace('/', '_'), "1700000000", "POST", "/order", "{\"a\":1}"));
        Assert.NotEqual(a, ClobAuth.L2Signature(secret, "1700000001", "POST", "/order", "{\"a\":1}"));
    }

    [Fact]
    public void L1HeadersCarryASignatureThatRecoversTheSigner()
    {
        var signer = new PolySigner(CowKey);
        var h = ClobAuth.L1Headers(signer, 1700000000);
        var sig = Eip712.FromHex(h["POLY_SIGNATURE"]);
        Assert.Equal(signer.Address, PolySigner.RecoverAddress(ClobAuth.L1Digest(signer.Address, "1700000000", 0), sig));
        Assert.Equal("0", h["POLY_NONCE"]);
    }

    [Theory]
    [InlineData(0.01, 10, 0.52, 10, "5200000", "10000000")]
    [InlineData(0.001, 7.777, 0.123, 7.77, "955710", "7770000")]
    [InlineData(0.1, 5, 0.3, 5, "1500000", "5000000")]
    public void BuyAmountsFollowTheDocumentedRounding(double tick, double shares, double price, double expectShares, string maker, string taker)
    {
        var (s, usd) = OrderBuilder.Round((decimal)price, (decimal)shares, (decimal)tick);
        Assert.Equal((decimal)expectShares, s);
        Assert.Equal(maker, ((long)(usd * 1_000_000m)).ToString());
        Assert.Equal(taker, ((long)(s * 1_000_000m)).ToString());
    }

    static OrderBook Book(string version = "v2", bool neg = false, decimal tick = 0.01m) =>
        new() { TokenId = "123456789012345678901234567890", TickSize = tick, MinOrderSize = 5, Version = version == "" ? null : version, NegRisk = neg };

    static JsonElement Order(SignedOrder o) => JsonDocument.Parse(o.Body).RootElement.GetProperty("order");

    [Fact]
    public void EoaOrderIsSignedByTheKeyOverTheDocumentedStructure()
    {
        var signer = new PolySigner(CowKey);
        var acct = new Account(CowKey, signer.Address, WalletType.Eoa);
        var book = Book();
        var o = OrderBuilder.BuildLimit(signer, acct, book, Side.Buy, 0.52m, 10m, "key-1", 479249096354, 1700000000000);
        var root = JsonDocument.Parse(o.Body).RootElement;
        var ord = root.GetProperty("order");
        Assert.Equal("5200000", ord.GetProperty("makerAmount").GetString());
        Assert.Equal("10000000", ord.GetProperty("takerAmount").GetString());
        Assert.Equal("BUY", ord.GetProperty("side").GetString());
        Assert.Equal(479249096354, ord.GetProperty("salt").GetInt64());          // a JSON number, as the API expects
        Assert.Equal(0, ord.GetProperty("signatureType").GetInt32());
        Assert.Equal("GTC", root.GetProperty("orderType").GetString());
        Assert.Equal("key-1", root.GetProperty("owner").GetString());
        Assert.Equal("0", ord.GetProperty("expiration").GetString());

        var order = new TypedStruct("Order",
            new TypedField("uint256", "salt", new BigInteger(479249096354)), new TypedField("address", "maker", acct.Wallet), new TypedField("address", "signer", acct.Wallet),
            new TypedField("uint256", "tokenId", BigInteger.Parse(book.TokenId)), new TypedField("uint256", "makerAmount", new BigInteger(5200000)), new TypedField("uint256", "takerAmount", new BigInteger(10000000)),
            new TypedField("uint8", "side", BigInteger.Zero), new TypedField("uint8", "signatureType", BigInteger.Zero),
            new TypedField("uint256", "timestamp", new BigInteger(1700000000000)), new TypedField("bytes32", "metadata", OrderBuilder.Zero32), new TypedField("bytes32", "builder", OrderBuilder.Zero32));
        var digest = Eip712.Digest(Eip712.Domain("Polymarket CTF Exchange", "3", 137, OrderBuilder.V2Exchange), order);
        Assert.Equal(signer.Address, PolySigner.RecoverAddress(digest, Eip712.FromHex(ord.GetProperty("signature").GetString()!)));
    }

    [Fact]
    public void SellPutsSharesFirstAndTheDomainFollowsTheMarketKind()
    {
        var signer = new PolySigner(CowKey);
        var acct = new Account(CowKey, signer.Address, WalletType.Eoa);
        var o = Order(OrderBuilder.BuildLimit(signer, acct, Book(), Side.Sell, 0.4m, 10m, "k", 1, 1));
        Assert.Equal("10000000", o.GetProperty("makerAmount").GetString());
        Assert.Equal("4000000", o.GetProperty("takerAmount").GetString());
        Assert.Equal("SELL", o.GetProperty("side").GetString());
        Assert.Equal(OrderBuilder.CtfExchange, OrderBuilder.ExchangeFor(Book("")));
        Assert.Equal(OrderBuilder.NegRiskExchange, OrderBuilder.ExchangeFor(Book("", true)));
        Assert.Equal("2", OrderBuilder.DomainVersionFor(Book("")));
        Assert.Equal("3", OrderBuilder.DomainVersionFor(Book("v2")));
    }

    [Fact]
    public void ProxyOrdersNameTheProxyAsMakerAndTheKeyAsSigner()
    {
        var signer = new PolySigner(CowKey);
        const string proxy = "0x1111111111111111111111111111111111111111";
        var o = Order(OrderBuilder.BuildLimit(signer, new Account(CowKey, proxy, WalletType.PolyProxy), Book(), Side.Buy, 0.5m, 6m, "k", 1, 1));
        Assert.Equal(proxy, o.GetProperty("maker").GetString());
        Assert.Equal(signer.Address, o.GetProperty("signer").GetString());
        Assert.Equal(1, o.GetProperty("signatureType").GetInt32());
    }

    [Fact]
    public void DepositWalletSignatureIsWrappedForErc7739()
    {
        var signer = new PolySigner(CowKey);
        const string dw = "0x2222222222222222222222222222222222222222";
        var o = Order(OrderBuilder.BuildLimit(signer, new Account(CowKey, dw, WalletType.DepositWallet), Book(), Side.Buy, 0.5m, 6m, "k", 1, 1));
        Assert.Equal(dw, o.GetProperty("signer").GetString());
        Assert.Equal(3, o.GetProperty("signatureType").GetInt32());
        var sig = Eip712.FromHex(o.GetProperty("signature").GetString()!);
        const string type = "Order(uint256 salt,address maker,address signer,uint256 tokenId,uint256 makerAmount,uint256 takerAmount,uint8 side,uint8 signatureType,uint256 timestamp,bytes32 metadata,bytes32 builder)";
        Assert.Equal(65 + 32 + 32 + type.Length + 2, sig.Length);
        Assert.Equal(type, System.Text.Encoding.UTF8.GetString(sig[129..^2]));
        Assert.Equal(type.Length, (sig[^2] << 8) | sig[^1]);
    }

    [Theory]
    [InlineData(0.525, 10)]      // off the tick
    [InlineData(0.5, 4)]         // below the minimum size
    [InlineData(0.0, 10)]        // outside the range
    [InlineData(1.0, 10)]
    public void BadOrdersAreRefusedBeforeSigning(double price, double shares)
    {
        var signer = new PolySigner(CowKey);
        Assert.Throws<OrderRejectedException>(() => OrderBuilder.BuildLimit(signer, new Account(CowKey, signer.Address, WalletType.Eoa), Book(), Side.Buy, (decimal)price, (decimal)shares, "k", 1, 1));
    }

    [Fact]
    public void AnEoaWalletMustBeTheKeysOwnAddress()
    {
        var signer = new PolySigner(CowKey);
        Assert.Throws<OrderRejectedException>(() => OrderBuilder.BuildLimit(signer, new Account(CowKey, "0x1111111111111111111111111111111111111111", WalletType.Eoa), Book(), Side.Buy, 0.5m, 10m, "k", 1, 1));
    }

    sealed class CountingHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.Method + " " + request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }

    [Fact]
    public async Task NothingIsSentWhenPolymarketSaysBlocked()
    {
        var h = new CountingHandler(); var http = new HttpClient(h);
        var signer = new PolySigner(CowKey);
        using var c = new TradingClient(new Account(CowKey, signer.Address, WalletType.Eoa), new PolymarketClient(http), new TradeGuard(() => Task.FromResult(new Geoblock(true, "IE", "1.2.3.4"))), http);
        await Assert.ThrowsAsync<TradingBlockedException>(() => c.PlaceLimitAsync("1", Side.Buy, 0.5m, 10m));
        await Assert.ThrowsAsync<TradingBlockedException>(() => c.CancelAsync("x"));
        await Assert.ThrowsAsync<TradingBlockedException>(() => c.OpenOrdersAsync());
        await Assert.ThrowsAsync<TradingBlockedException>(() => c.CredentialsAsync());
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task NothingIsSentWhenTheLocationCannotBeChecked()
    {
        var h = new CountingHandler(); var http = new HttpClient(h);
        var signer = new PolySigner(CowKey);
        using var c = new TradingClient(new Account(CowKey, signer.Address, WalletType.Eoa), new PolymarketClient(http), new TradeGuard(() => throw new HttpRequestException("offline")), http);
        await Assert.ThrowsAsync<TradingBlockedException>(() => c.CancelAsync("x"));
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task GuardReusesAnAnswerForTwoMinutesOnly()
    {
        int calls = 0; var t = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var g = new TradeGuard(() => { calls++; return Task.FromResult(new Geoblock(false, "US", "1.1.1.1")); }, () => t);
        await g.EnsureAllowedAsync(); await g.EnsureAllowedAsync();
        Assert.Equal(1, calls);
        t = t.AddMinutes(3); await g.EnsureAllowedAsync();
        Assert.Equal(2, calls);
    }

    [Fact]
    public void ResponsesAreReadForSuccessAndFailure()
    {
        var ok = TradingClient.ParseResult(200, """{"success":true,"errorMsg":"","orderID":"0xabc","status":"live"}""");
        Assert.True(ok.Success); Assert.Equal("0xabc", ok.OrderId); Assert.Equal("live", ok.Status);
        var bad = TradingClient.ParseResult(400, """{"success":false,"errorMsg":"not enough balance / allowance","orderID":"","status":""}""");
        Assert.False(bad.Success); Assert.Equal("not enough balance / allowance", bad.Error);
        Assert.False(TradingClient.ParseResult(502, "<html>").Success);
        var orders = TradingClient.ParseOrders("""[{"id":"1","asset_id":"9","side":"BUY","price":"0.52","original_size":"10","size_matched":"2","status":"LIVE"},{"id":"2","status":"CANCELED"}]""");
        Assert.Single(orders); Assert.Equal(0.52m, orders[0].Price); Assert.Equal(2m, orders[0].Matched);
    }
}
