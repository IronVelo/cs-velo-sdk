namespace IronVelo.Exceptions;
using Flows;

/// <summary>
/// The error thrown if a `Result` is unwrapped in the `Err` state
/// </summary>
public class NotOk : Exception
{
    /// <summary>
    /// Create a new <see cref="NotOk"/> error.
    /// </summary>
    /// <param name="message">The message associated with the error.</param>
    public NotOk(string message) : base(message) { }
}

/// <summary>
/// The error thrown if a `Result`'s error is unwrapped in the `Ok` state
/// </summary>>
public class NotErr : Exception
{
    /// <summary>
    /// Create a new <see cref="NotErr"/> error.
    /// </summary>
    /// <param name="message">The message to associate with the error.</param>
    public NotErr(string message) : base(message) { }
}

/// <summary>
/// The type of <see cref="RequestError"/> encountered
/// </summary>
/// <remarks>
/// Variants:
/// <list type="bullet">
/// <item><see cref="RequestErrorKind.Precondition"/></item>
/// <item><see cref="RequestErrorKind.Internal"/></item>
/// <item><see cref="RequestErrorKind.Request"/></item>
/// <item><see cref="RequestErrorKind.State"/></item>
/// <item><see cref="RequestErrorKind.Deserialization"/></item>
/// <item><see cref="RequestErrorKind.General"/></item>
/// </list>
/// </remarks>
public enum RequestErrorKind
{
    /// <summary>
    /// There was an error during deserialization
    /// </summary>
    Deserialization,
    /// <summary>
    /// A precondition was not satisfied and now the associated permit is no longer usable. The user must restart the
    /// flow.
    /// </summary>
    /// <remarks>
    /// Common Causes:
    /// <list type="bullet"><item>
    /// <description>Permit Expiration: The user took too long to continue the flow</description>
    /// </item><item>
    /// Permit Claims: A claim failed to satisfy a precondition, this could be the number of attempts being above the
    /// limit
    /// </item></list>
    /// </remarks>
    Precondition,
    /// <summary>
    /// An unspecified error took place in the IdP. The associated permit may or may not be invalidated depending on the
    /// depth of the error.
    /// </summary>
    Internal,
    /// <summary>
    /// Http status 401 was returned from the IdP. The permit may be able to be used again.
    /// </summary>
    Request,
    /// <summary>
    /// Attempted to access a state without the associated permit
    /// </summary>
    State,
    /// <summary>
    /// Errors from the IdP are considered non-exhaustive, this is a catchall to handle this
    /// </summary>
    General
}

/// <summary>
/// Indicates an error took place during the request
/// </summary>
/// <seealso cref="RequestErrorKind"/>
public class RequestError : Exception
{
    private RequestErrorKind Kind { get; }

    internal RequestError(RequestErrorKind kind)
    {
        Kind = kind;
    }

    internal RequestError(RequestErrorKind kind, string message) : base(message)
    {
        Kind = kind;
    }

    internal static RequestError Deserialization()
    {
        return new RequestError(RequestErrorKind.Deserialization);
    }

    /// <summary>
    /// The reason for the request error.
    /// </summary>
    public RequestErrorKind ErrorKind => Kind;
    /// <summary>
    /// The formatted error.
    /// </summary>
    public override string Message 
        => $"{Enum.GetName(typeof(RequestErrorKind), Kind)}: {base.Message}";
}

/// <summary>
/// Indicates that a user attempted to use an MFA method that they have not previously set up.
/// </summary>
public class IllegalMfa : Exception
{
    private MfaKind[] Available { get; }
    private MfaKind Expected { get; }

    internal IllegalMfa(MfaKind[] available, MfaKind expected)
    {
        Available = available;
        Expected = expected;
    }

    internal IllegalMfa(MfaKind[] available, MfaKind expected, string message) : base(message)
    {
        Available = available;
        Expected = expected;
    }

    /// <summary>
    /// Format the expected MFA kind in a human-readable format.
    /// </summary>
    public string FmtExpected
        => Enum.GetName(typeof(MfaKind), Expected)! /* Cannot be null */;
    
    /// <summary>
    /// The formatted error.
    /// </summary>
    public override string Message => 
        $"User does not support Mfa kind: {FmtExpected}, available: {string.Join(", ", Available)}";
}

/// <summary>
/// Error returned when deserializing invalid Base64.
/// </summary>
public class Base64Error : Exception
{
    private Base64Error(string message) : base(message) { }

    internal static Base64Error InvalidEncoding => new("Invalid encoding detected.");
}