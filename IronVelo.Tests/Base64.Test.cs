using Xunit.Abstractions;

namespace IronVelo.Tests;
using Base64;
using Exceptions;

public class Generators
{
    public static Arbitrary<byte[]> LimitedSizeByteArray()
    {
        var byteArrayGenerator = from size in Gen.Choose(0, 32_767)
            from bytes in Gen.ArrayOf(size, Arb.Generate<byte>())
            select bytes;
        return Arb.From(byteArrayGenerator);
    }
}

public class Base64Tests
{
    private readonly ITestOutputHelper _testOutputHelper;

    public Base64Tests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
        Arb.Register<Generators>();
    }
    
    [Property(MaxTest = 9999)]
    public void EncodeCtDecodeCtIsBijective(byte[] data)
    {
        var encoded = Base64.EncodeCt(data);
        var decoded = Base64.DecodeCt(encoded);

        Assert.Equal(data, decoded);
    }

    [Property(MaxTest = 9999)]
    public void EncodeCtDecodeIsBijective(byte[] data)
    {
        var encoded = Base64.EncodeCt(data);
        var decoded = Base64.Decode(encoded);
        
        Assert.Equal(data, decoded);
    }

    [Property(MaxTest = 9999)]
    public void CheckAgainstBuiltin(byte[]? data)
    {
        if (data == null) { return; }
        var encoded = Convert.ToBase64String(data).TrimEnd('=');

        var decodedBytes = Base64.DecodeCt(encoded);
        Assert.Equal(decodedBytes, data);

        var reEncoded = Base64.EncodeCt(decodedBytes);

        Assert.Equal(encoded, reEncoded);
    }

    [Property(MaxTest = 9999)]
    public void EncodePaddedCtAgainstBuiltin(byte[]? data)
    {
        if (data == null) { return; }

        var builtinEncoded = Convert.ToBase64String(data);
        var ctEncoded = Base64.EncodePaddedCt(data);
        
        Assert.Equal(builtinEncoded, ctEncoded);
    }

    private static void EnsureDecodeFailure(string data, Func<string, byte[]> decode)
    {
        try
        {
            decode(data);
        }
        catch (Base64Error)
        {
            // OK
            return;
        }

        throw new Exception("Decoding should have thrown an error");
    }

    [Fact]
    public void Smoke()
    {
        var plain = "hello world"u8.ToArray();
        var encoded = Base64.EncodeCt(plain);
        var decoded = Base64.DecodeCt(encoded);
        Assert.Equal(plain, decoded);
    }

    [Fact]
    public void EncodedPaddedSmoke()
    {
        var plain = "hello world"u8.ToArray();
        var encodedBuiltin = Convert.ToBase64String(plain);
        var encodeCt = Base64.EncodePaddedCt(plain);
        Assert.Equal(encodedBuiltin, encodeCt);
    }

    [Fact]
    public void CatchesInvalidOnDecode()
    {
        EnsureDecodeFailure("!!invalid!!", Base64.Decode);
        EnsureDecodeFailure("!!invalid!!", Base64.DecodeCt);
        EnsureDecodeFailure("@@@", Base64.Decode);
        EnsureDecodeFailure("@@@", Base64.DecodeCt);
    }
}