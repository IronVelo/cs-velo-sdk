using System.Diagnostics;
namespace IronVelo.Types;

/// <summary>
/// The error for when <see cref="MfaParser.From"/> was unable to parse the provided raw <see cref="Flows.MfaKind"/>.
/// </summary>
/// <param name="Raw">
/// The provided raw <see cref="Flows.MfaKind"/> which <see cref="MfaParser"/> did not recognize.
/// </param>
public record UnknownMfaKind(string Raw)
{
    /// <summary>
    /// Format the <see cref="UnknownMfaKind"/> error.
    /// </summary>
    /// <returns>The formatted error.</returns>
    public override string ToString() => $"Unknown MFA kind: {Raw}";
}

/// <summary>
/// A utility for safely parsing a <see cref="Flows.MfaKind"/>.
/// </summary>
public static class MfaParser
{
    /// <summary>
    /// Try to parse the provided <see cref="Flows.MfaKind"/>
    /// </summary>
    /// <param name="raw">The raw MFA kind to attempt to parse.</param>
    /// <returns>
    /// A successful <see cref="Result{T,TE}"/> if the provided raw 
    /// <see cref="Flows.MfaKind"/> was valid, an <see cref="UnknownMfaKind"/> otherwise.
    /// </returns>
    public static Result<Flows.MfaKind, UnknownMfaKind> From(string raw)
    {
        return Enum.TryParse(raw, true, out Flows.MfaKind result) 
            ? Result<Flows.MfaKind, UnknownMfaKind>.Success(result) 
            : Result<Flows.MfaKind, UnknownMfaKind>.Failure(new UnknownMfaKind(raw));
    }
}

/// <summary>
/// The OTP was of invalid length.
/// </summary>
/// <param name="Expected">The expected length of the OTP.</param>
/// <param name="Received">The length of the provided OTP.</param>
public record InvalidLength(uint Expected, uint Received)
{
    /// <summary>
    /// Format the <see cref="InvalidLength"/> error.
    /// </summary>
    /// <returns>The formatted <see cref="InvalidLength"/> error.</returns>
    public override string ToString()
        =>  $"InvalidLength {{ Expected: {Expected}, Received: {Received} }}";
}

/// <summary>
/// The reason for the OTP being invalid.
/// </summary>
public enum InvalidOtpKind
{
    /// <summary>
    /// The OTP was of invalid length.
    /// </summary>
    InvalidLength,
    /// <summary>
    /// The OTP contained non-digit characters.
    /// </summary>
    NonNumeric
}

/// <summary>
/// The error returned when an OTP is of invalid length or contains non-digit characters.
/// </summary>
/// <param name="Kind">Describes the reason for the OTP being invalid.</param>
/// <param name="LengthError">
/// If the OTP kind was <see cref="InvalidOtpKind.InvalidLength"/>, the expected and actual length of 
/// the provided OTP.
/// </param>
public record InvalidOtp(InvalidOtpKind Kind, InvalidLength? LengthError = null)
{
    internal static InvalidOtp LenErr(InvalidLength error)
    {
        return new InvalidOtp(InvalidOtpKind.InvalidLength, error);
    }
    
    /// <summary>
    /// Format the <see cref="InvalidOtp"/> error.
    /// </summary>
    /// <returns>The formatted <see cref="InvalidOtp"/> error.</returns>
    public override string ToString()
    {
        var invalidLenMsg = LengthError == null ? "" : $", {LengthError}";
        return $"InvalidOtp {{ Kind: {Kind}{invalidLenMsg}}}";
    }
}

/// <summary>
/// Implemented by the OTP refinements, allowing for reuse of the <see cref="IsNumeric"/> validator.
/// </summary>
/// <typeparam name="T">The OTP type implementing <see cref="OtpValidator{T}"/>.</typeparam>
public abstract record OtpValidator<T>
{
    /// <summary>
    /// SAFETY: The input cannot be null
    /// </summary>
    protected static unsafe Result<T, InvalidOtp> IsNumeric(string input, Func<T> ok)
    {
        Debug.Assert(input != null, "Invariant Violated, the input to unsafe IsNumeric must not be null");
        var isAllNums = true;

        fixed (char* p = input)
        {
            for (var i = 0; i < input.Length; i++)
            {
                // non digits below 0 will underflow thus being false
                isAllNums &= (byte)(p[i] - '0') <= 9;
            }
        }

        return isAllNums 
            ? Result<T, InvalidOtp>.Success(ok()) 
            : Result<T, InvalidOtp>.Failure(new InvalidOtp(InvalidOtpKind.NonNumeric));
    }
}

/// <summary>
/// Represents a potentially valid OTP.
/// </summary>
public record SimpleOtp : OtpValidator<SimpleOtp>
{
    private readonly string _inner;
    private const uint ExpectedLength = 6;
    
    private SimpleOtp(string inner) { _inner = inner; }
    
    /// <summary>
    /// Verify that the provided raw OTP contains only digits and is the correct length (6).
    /// </summary>
    /// <param name="otp">The raw OTP to validate.</param>
    /// <returns>
    /// A successful <see cref="Result{T,TE}"/> if the OTP is potentially valid, an error otherwise 
    /// with the reason.
    /// </returns>
    public static Result<SimpleOtp, InvalidOtp> From(string otp)
    {
        if (otp.Length == ExpectedLength)
        {
            // SAFETY: The OTP length is 6, therefore it cannot be null
            return IsNumeric(otp, () => new SimpleOtp(otp));
        }
        return Result<SimpleOtp, InvalidOtp>.Failure(InvalidOtp.LenErr(new InvalidLength(
            ExpectedLength, (uint) otp.Length
        )));
    }

    /// <summary>
    /// Extract the raw <see cref="SimpleOtp"/>.
    /// </summary>
    /// <returns>The encapsulated raw value.</returns>
    public override string ToString() { return _inner; } 
}

/// <summary>
/// Represents a potentially valid TOTP.
/// </summary>
public record Totp : OtpValidator<Totp>
{
    private readonly string _inner;
    private const uint ExpectedLength = 8;

    private Totp(string inner) { _inner = inner; }

    /// <summary>
    /// Verify that the provided raw TOTP contains only digits and is the correct length (8).
    /// </summary>
    /// <param name="totp">The raw TOTP to validate.</param>
    /// <returns>
    /// A successful <see cref="Result{T,TE}"/> if the TOTP is potentially valid, an error otherwise 
    /// with the reason.
    /// </returns>
    public static Result<Totp, InvalidOtp> From(string totp)
    {
        if (totp.Length == ExpectedLength)
        {
            // SAFETY: The TOTP length is 8, therefore it cannot be null
            return IsNumeric(totp, () => new Totp(totp));
        }
        return Result<Totp, InvalidOtp>.Failure(InvalidOtp.LenErr(new InvalidLength(
            ExpectedLength, (uint) totp.Length
        ))); 
    }
    
    /// <summary>
    /// Extract the raw <see cref="Totp"/>.
    /// </summary>
    /// <returns>The encapsulated value.</returns>
    public override string ToString() { return _inner; }
}

/// <summary>
/// Specifies the types of password validation failures.
/// </summary>
public enum PasswordInvalidKind
{
    /// <summary>
    /// Password has too few characters
    /// </summary>
    TooFewChars,
    /// <summary>
    /// Password has too many characters
    /// </summary>
    TooManyChars,
    /// <summary>
    /// Password contains illegal characters
    /// </summary>
    IllegalCharacter,
    /// <summary>
    /// Password needs at least one uppercase letter
    /// </summary>
    NeedsCapital,
    /// <summary>
    /// Password needs at least one lowercase letter
    /// </summary>
    NeedsLowercase,
    /// <summary>
    /// Password needs at least one numeric digit
    /// </summary>
    NeedsNumeric,
    /// <summary>
    /// Password needs at least one special character
    /// </summary>
    NeedsSpecial
}

/// <summary>
/// Represents an invalid password and the reason for its invalidity
/// </summary>
/// <param name="Reason">The reason why the password is invalid</param>
/// <param name="Len">The length of the password, if applicable</param>
public record PasswordInvalid(PasswordInvalidKind Reason, uint? Len = null)
{
    /// <summary>
    /// Format the password invalid error.
    /// </summary>
    /// <returns>The formatted error.</returns>
    public override string ToString()
    {
        return Reason switch
        {
            PasswordInvalidKind.TooFewChars => $"Password has too few characters: {Len}",
            PasswordInvalidKind.TooManyChars => $"Password has too many characters: {Len}",
            PasswordInvalidKind.IllegalCharacter => "Password contains illegal characters.",
            PasswordInvalidKind.NeedsCapital => "Password needs at least one uppercase letter.",
            PasswordInvalidKind.NeedsLowercase => "Password needs at least one lowercase letter.",
            PasswordInvalidKind.NeedsNumeric => "Password needs at least one numeric digit.",
            PasswordInvalidKind.NeedsSpecial => "Password needs at least one special character.",
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}

/// <summary>
/// Representation of a password which meets the required criteria. For more information regarding this criteria see the
/// <see cref="Password.From"/> associated function.
/// </summary>
public class Password
{
    private readonly string _value;

    private Password(string value)
    {
        _value = value;
    }

    // makes failure state of set possible infallible, port of the IdP's default password validation
    
    /// <summary>
    /// Create a new <see cref="Password"/>
    /// </summary>
    /// <param name="password">The password string to validate and create</param>
    /// <returns>
    /// <list type="bullet">
    /// <item>
    /// <description>Success: The password met the required criteria</description>
    /// </item>
    /// <item>
    /// <description>Failure: The password failed to meet the required criteria</description>
    /// </item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// The password must meet the following criteria:
    /// <list type="bullet"><item>
    /// <description>Be at least 8 characters long</description>
    /// </item><item>
    /// <description>Be no more than 72 characters long</description>
    /// </item><item><description>
    /// Contain only legal characters (uppercase letters, lowercase letters, digits, and special 
    /// characters)
    /// </description></item><item>
    /// <description>Contain at least one uppercase letter</description>
    /// </item><item>
    /// <description>Contain at least one lowercase letter</description>
    /// </item><item>
    /// <description>Contain at least one number</description>
    /// </item><item>
    /// <description>Contain at least one special character</description>
    /// </item></list>
    /// </remarks>
    public static Result<Password, PasswordInvalid> From(string password)
    {
        var len = (uint) password.Length;
        switch (len)
        {
            case < 8:
                return Result<Password, PasswordInvalid>.Failure(
                    new PasswordInvalid(PasswordInvalidKind.TooFewChars, len)
                );
            case > 72:
                return Result<Password, PasswordInvalid>.Failure(
                    new PasswordInvalid(PasswordInvalidKind.TooManyChars, len)
                );
        }
        
        var hasUpper = false;
        var hasLower = false;
        var hasNumber = false;
        var hasSpecial = false;
        var legal = true;
        
        foreach (var c in password)
        {
            var upper = 'A' <= c & c <= 'Z';
            var lower = 'a' <= c & c <= 'z';
            var number = '0' <= c & c <= '9';
            var special = ('!' <= c & c <= '/') | (':' <= c & c <= '@') | ('{' <= c & c <= '~');
            
            hasUpper |= upper;
            hasLower |= lower;
            hasNumber |= number;
            hasSpecial |= special;
            legal &= upper | lower | number | special;
        }
        
        if (!legal) return Result<Password, PasswordInvalid>.Failure(
            new PasswordInvalid(PasswordInvalidKind.IllegalCharacter)
        );
        if (!hasUpper) return Result<Password, PasswordInvalid>.Failure(
            new PasswordInvalid(PasswordInvalidKind.NeedsCapital)
        );
        if (!hasLower) return Result<Password, PasswordInvalid>.Failure(
            new PasswordInvalid(PasswordInvalidKind.NeedsLowercase)
        );
        if (!hasNumber) return Result<Password, PasswordInvalid>.Failure(
            new PasswordInvalid(PasswordInvalidKind.NeedsNumeric)
        );
        
        return !hasSpecial 
            ? Result<Password, PasswordInvalid>.Failure(
                new PasswordInvalid(PasswordInvalidKind.NeedsSpecial)
            ) 
            : Result<Password, PasswordInvalid>.Success(new Password(password));
    }

    /// <summary>
    /// Extract the raw interior.
    /// </summary>
    /// <returns>The raw password.</returns>
    public override string ToString() => _value;
}
