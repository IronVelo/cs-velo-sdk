using System.Runtime.CompilerServices;
using Xunit.Abstractions;

namespace IronVelo.Tests;
using Types;

internal abstract record EnsureNumeric : OtpValidator<uint>
{
    public static Result<uint, InvalidOtp> IsNumeric(string? toCheck)
    {
        return toCheck == null 
            ? Result<uint, InvalidOtp>.Failure(new InvalidOtp(InvalidOtpKind.InvalidLength, new InvalidLength(1, 0))) 
            : IsNumeric(toCheck, () => 1);
    }
}

public record AllNumbers
{
    internal readonly string Value;

    private AllNumbers(string value)
    {
        Value = value;
    }

    public static Gen<AllNumbers> Generator()
    {
        return Gen.Choose(1, 1024)
            .SelectMany(len => Gen.ArrayOf(len, Gen.Choose(0, 9).Select(d => (char)(d + '0'))))
            .Select(chars => new string(chars))
            .Select(numString => new AllNumbers(numString));
    }
}

public class AllNumbersArbitrary
{
    public static Arbitrary<AllNumbers> CreateArb()
    {
        return AllNumbers.Generator().ToArbitrary();
    }
}

public record NotNumbers
{
    internal readonly string Value;

    private NotNumbers(string value)
    {
        Value = value;
    }
    
    public static Gen<NotNumbers> Generator()
    {
        return Arb.Generate<string>()
            .Where(gStr => gStr != null && !gStr.All(char.IsDigit))
            .Select(nonNumStr => new NotNumbers(nonNumStr));
    } 
}

public class NotAllNumbersArbitrary
{
    public static Arbitrary<NotNumbers> CreateArb()
    {
        return NotNumbers.Generator().ToArbitrary();
    }
}

public class OtpRefined 
{
    private readonly ITestOutputHelper _testOutputHelper;
    public OtpRefined(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
        Arb.Register<AllNumbersArbitrary>();
        Arb.Register<NotAllNumbersArbitrary>();
    }
    
    [Fact]
    public void EnsureNumericSmoke()
    {
        const string valid = "12323429834982347293487234987234928347239487234";
        Assert.True(EnsureNumeric.IsNumeric(valid).IsOk());

        const string invalid = "118231928371239811230198230192380192380192a";
        Assert.True(EnsureNumeric.IsNumeric(invalid).IsErr());
    }

    [Property(MaxTest = 1000)]
    public void AllNumbersAlwaysPositive(AllNumbers numbers)
    {
        var res = EnsureNumeric.IsNumeric(numbers.Value);
        Assert.True(res.IsOk());
    }

    [Property(MaxTest = 1000)]
    public void NotAllNumbersAlwaysFails(NotNumbers input)
    {
        var res = EnsureNumeric.IsNumeric(input.Value);
        Assert.True(res.IsErr());
    }

    [Fact]
    public void SimpleOtpInvalidLengthSmoke()
    {
        const string tooShort = "12345";
        Assert.True(SimpleOtp.From(tooShort).IsErr());
        const string tooLong = "1234567";
        Assert.True(SimpleOtp.From(tooLong).IsErr());
    }

    [Fact]
    public void SimpleOtpValidSmoke()
    {
        const string validOtp = "123456";
        Assert.True(SimpleOtp.From(validOtp).IsOk());
    }

    [Fact]
    public void TotpInvalidLengthSmoke()
    {
        const string tooShort = "1234567";
        Assert.True(Totp.From(tooShort).IsErr());
        const string tooLong = "123456789";
        Assert.True(Totp.From(tooLong).IsErr());
    }

    [Fact]
    public void TotpValidSmoke()
    {
        const string validTotp = "12345678";
        Assert.True(Totp.From(validTotp).IsOk());
    }
}

public record ValidPassword
{
    internal readonly string Value;

    private ValidPassword(string password)
    {
        Value = password;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool OfValidLength(string s)
    {
        return s.Length is >= 8 and <= 72;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasNumber(string s)
    {
        return s.Any(char.IsDigit);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasUpper(string s)
    {
        return s.Any(char.IsUpper);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasLower(string s)
    {
        return s.Any(char.IsLower);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CharSpecial(char c)
    {
        return ('!' <= c & c <= '/') | (':' <= c & c <= '@') | ('{' <= c & c <= '~');
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasSpecial(string s)
    {
        return s.Any(CharSpecial);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CharPredicate(char c)
    {
        return CharSpecial(c) || char.IsLetter(c) || char.IsDigit(c);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ValidPredicate(string? s)
    {
        if (s == null) {
            return false; 
        }

        if (!(OfValidLength(s) && HasNumber(s) && HasLower(s) && HasUpper(s) && HasSpecial(s)))
        {
            return false;
        }

        return s.All(CharPredicate);
    }

    public static Gen<ValidPassword> Generator()
    {
        return Arb.Generate<string>()
            .Where(ValidPredicate)
            .Select(s => new ValidPassword(s));
    }

    public override string ToString()
    {
        return Value;
    }
}

public class ValidPasswordArbitrary
{
    public static Arbitrary<ValidPassword> CreateArb()
    {
        return ValidPassword.Generator().ToArbitrary();
    }
}

public class PasswordRefined
{
    public PasswordRefined()
    {
        Arb.Register<ValidPasswordArbitrary>();
    }

    [Fact]
    public void InvalidLengthSmoke()
    {
        // password len is guarded against, so no need to worry about other criteria
        const uint tooShortLen = 7;
        var tooShort = new string('a', (int)tooShortLen);
        var shortErr = Password.From(tooShort).UnwrapErr();
        
        Assert.Equal(PasswordInvalidKind.TooFewChars, shortErr.Reason);
        Assert.Equal(tooShortLen, shortErr.Len);

        const uint tooLongLen = 73;
        var tooLong = new string('a', (int)tooLongLen);
        var longErr = Password.From(tooLong).UnwrapErr();
        
        Assert.Equal(PasswordInvalidKind.TooManyChars, longErr.Reason);
        Assert.Equal(tooLongLen, longErr.Len);
    }

    [Fact]
    public void RequiresCapitalSmoke()
    {
        const string needsCapital = "abcdefg12345!";
        var err = Password.From(needsCapital).UnwrapErr();
        
        Assert.Equal(PasswordInvalidKind.NeedsCapital, err.Reason);
        Assert.Null(err.Len);
    }

    [Fact]
    public void RequiresLowerSmoke()
    {
        const string needsLower = "ABCDEFG12345!";
        var err = Password.From(needsLower).UnwrapErr();
        
        Assert.Equal(PasswordInvalidKind.NeedsLowercase, err.Reason);
        Assert.Null(err.Len);
    }

    [Fact]
    public void RequiresSpecialSmoke()
    {
        const string needsSpecial = "abcDefg12345";
        var err = Password.From(needsSpecial).UnwrapErr();
        
        Assert.Equal(PasswordInvalidKind.NeedsSpecial, err.Reason);
        Assert.Null(err.Len);
    }

    [Fact]
    public void RequiresNumberSmoke()
    {
        const string needsNumber = "abcDefg!";
        var err = Password.From(needsNumber).UnwrapErr();
        
        Assert.Equal(PasswordInvalidKind.NeedsNumeric, err.Reason);
        Assert.Null(err.Len);
    }

    [Property]
    public void ValidPasswordNoFalseErrors(ValidPassword password)
    {
        Assert.True(Password.From(password.Value).IsOk());
    }
}