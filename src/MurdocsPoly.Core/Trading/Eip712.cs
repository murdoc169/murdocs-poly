using System.Numerics;
using System.Text;
using Nethereum.Util;

namespace MurdocsPoly.Core.Trading;

/// <summary>One field of an EIP-712 struct. Value is a hex string for address and bytes32, a plain string for string, a BigInteger for uintN, or a <see cref="TypedStruct"/>.</summary>
public sealed record TypedField(string Type, string Name, object Value);

/// <summary>An EIP-712 struct with its values. Only the types Polymarket uses are supported: address, uintN, string, bytes32 and nested structs.</summary>
public sealed class TypedStruct
{
    public string Name { get; }
    public IReadOnlyList<TypedField> Fields { get; }
    public TypedStruct(string name, params TypedField[] fields) { Name = name; Fields = fields; }
}

/// <summary>EIP-712 typed data hashing, written out so it can be checked against the specification's own test vector.</summary>
public static class Eip712
{
    public static byte[] Keccak(byte[] data) => new Sha3Keccack().CalculateHash(data);
    public static byte[] Keccak(string text) => Keccak(Encoding.UTF8.GetBytes(text));

    public static string EncodeType(TypedStruct s)
    {
        var referenced = new SortedDictionary<string, TypedStruct>(StringComparer.Ordinal);
        Collect(s, referenced);
        referenced.Remove(s.Name);
        var sb = new StringBuilder(Own(s));
        foreach (var r in referenced.Values) sb.Append(Own(r));
        return sb.ToString();
    }

    static string Own(TypedStruct s) => s.Name + "(" + string.Join(",", s.Fields.Select(f => f.Type + " " + f.Name)) + ")";

    static void Collect(TypedStruct s, SortedDictionary<string, TypedStruct> into)
    {
        if (into.ContainsKey(s.Name)) return;
        into[s.Name] = s;
        foreach (var f in s.Fields) if (f.Value is TypedStruct n) Collect(n, into);
    }

    public static byte[] HashStruct(TypedStruct s)
    {
        var data = new List<byte>(Keccak(EncodeType(s)));
        foreach (var f in s.Fields) data.AddRange(EncodeValue(f));
        return Keccak(data.ToArray());
    }

    static byte[] EncodeValue(TypedField f)
    {
        switch (f.Value)
        {
            case TypedStruct nested: return HashStruct(nested);
            case BigInteger n when f.Type.StartsWith("uint", StringComparison.Ordinal): return Word(n);
            case string text when f.Type == "string": return Keccak(text);
            case string hex when f.Type == "address":
                { var b = FromHex(hex); if (b.Length != 20) throw new ArgumentException("address must be 20 bytes: " + f.Name); return Pad(b); }
            case string hex when f.Type == "bytes32":
                { var b = FromHex(hex); if (b.Length != 32) throw new ArgumentException("bytes32 must be 32 bytes: " + f.Name); return b; }
            default: throw new NotSupportedException($"Cannot encode {f.Type} {f.Name}");
        }
    }

    public static byte[] Word(BigInteger n)
    {
        if (n.Sign < 0) throw new ArgumentException("negative number");
        var raw = n.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (raw.Length > 32) throw new ArgumentException("number too large");
        return Pad(raw);
    }

    static byte[] Pad(byte[] b) { var w = new byte[32]; Buffer.BlockCopy(b, 0, w, 32 - b.Length, b.Length); return w; }

    public static byte[] FromHex(string hex)
    {
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex[2..];
        return Convert.FromHexString(hex);
    }

    public static string ToHex(byte[] b) => "0x" + Convert.ToHexString(b).ToLowerInvariant();

    /// <summary>The 32 byte digest that gets signed: keccak(0x1901 || domainSeparator || hashStruct(message)).</summary>
    public static byte[] Digest(TypedStruct domain, TypedStruct message)
    {
        var data = new List<byte> { 0x19, 0x01 };
        data.AddRange(HashStruct(domain)); data.AddRange(HashStruct(message));
        return Keccak(data.ToArray());
    }

    public static TypedStruct Domain(string name, string version, int chainId, string? verifyingContract = null)
    {
        var fields = new List<TypedField> { new("string", "name", name), new("string", "version", version), new("uint256", "chainId", new BigInteger(chainId)) };
        if (verifyingContract != null) fields.Add(new TypedField("address", "verifyingContract", verifyingContract));
        return new TypedStruct("EIP712Domain", fields.ToArray());
    }
}
