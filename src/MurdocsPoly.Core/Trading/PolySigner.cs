using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Nethereum.Signer;

namespace MurdocsPoly.Core.Trading;

/// <summary>Polymarket wallet kinds. The number is what goes into a signed order's signatureType.</summary>
public enum WalletType
{
    /// <summary>A plain wallet: the key's own address holds the funds.</summary>
    Eoa = 0,
    /// <summary>Legacy smart wallet from an email or Google login on polymarket.com.</summary>
    PolyProxy = 1,
    /// <summary>Legacy smart wallet created with an external wallet such as MetaMask.</summary>
    GnosisSafe = 2,
    /// <summary>The default smart wallet for accounts created on or after 4 May 2026.</summary>
    DepositWallet = 3,
}

/// <summary>The account to trade with: a signing key and the wallet that holds the funds.</summary>
public sealed record Account(string PrivateKey, string Wallet, WalletType WalletType);

/// <summary>Signs with a private key. The key never leaves this object.</summary>
public sealed class PolySigner
{
    readonly EthECKey _key;
    public string Address { get; }

    public PolySigner(string privateKeyHex)
    {
        var hex = privateKeyHex.Trim();
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex[2..];
        if (hex.Length != 64 || !hex.All(Uri.IsHexDigit)) throw new ArgumentException("A private key is 64 hexadecimal characters.");
        _key = new EthECKey(hex);
        Address = _key.GetPublicAddress().ToLowerInvariant();
    }

    /// <summary>The 65 byte signature (r, s, v with v = 27 or 28) over a 32 byte digest.</summary>
    public byte[] SignDigest(byte[] digest)
    {
        var s = _key.SignAndCalculateV(digest);
        var sig = new byte[65];
        Pad(s.R).CopyTo(sig, 0); Pad(s.S).CopyTo(sig, 32);
        sig[64] = s.V[0];
        return sig;
    }

    static byte[] Pad(byte[] b)
    {
        if (b.Length == 32) return b;
        var w = new byte[32]; Buffer.BlockCopy(b, Math.Max(0, b.Length - 32), w, Math.Max(0, 32 - b.Length), Math.Min(32, b.Length)); return w;
    }

    public static string RecoverAddress(byte[] digest, byte[] sig)
    {
        var s = EthECDSASignatureFactory.FromComponents(sig[..32], sig[32..64], sig[64]);
        return EthECKey.RecoverFromSignature(s, digest).GetPublicAddress().ToLowerInvariant();
    }
}

/// <summary>Request signing for the CLOB: L1 (the key attests to the address) and L2 (an HMAC with the API credentials).</summary>
public static class ClobAuth
{
    public const int ChainId = 137;
    public const string Attestation = "This message attests that I control the given wallet";

    public static byte[] L1Digest(string signerAddress, string timestamp, BigInteger nonce) =>
        Eip712.Digest(
            Eip712.Domain("ClobAuthDomain", "1", ChainId),
            new TypedStruct("ClobAuth",
                new TypedField("address", "address", signerAddress), new TypedField("string", "timestamp", timestamp),
                new TypedField("uint256", "nonce", nonce), new TypedField("string", "message", Attestation)));

    public static Dictionary<string, string> L1Headers(PolySigner signer, long unixSeconds, long nonce = 0)
    {
        var ts = unixSeconds.ToString();
        var sig = signer.SignDigest(L1Digest(signer.Address, ts, nonce));
        return new() { ["POLY_ADDRESS"] = signer.Address, ["POLY_SIGNATURE"] = Eip712.ToHex(sig), ["POLY_TIMESTAMP"] = ts, ["POLY_NONCE"] = nonce.ToString() };
    }

    /// <summary>urlsafe base64 (with padding) of HMAC-SHA256(secret, timestamp + METHOD + path + body). The path has no query string.</summary>
    public static string L2Signature(string secret, string timestamp, string method, string path, string body = "")
    {
        var key = Convert.FromBase64String(secret.Trim().Replace('-', '+').Replace('_', '/').PadRight((secret.Trim().Length + 3) / 4 * 4, '='));
        using var h = new HMACSHA256(key);
        var mac = h.ComputeHash(Encoding.UTF8.GetBytes(timestamp + method.ToUpperInvariant() + path + body));
        return Convert.ToBase64String(mac).Replace('+', '-').Replace('/', '_');
    }
}
