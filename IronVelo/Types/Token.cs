using MustUse;
using AffineTypes;
using IronVelo.Exceptions;
using Newtonsoft.Json;

namespace IronVelo.Types;
using Base64;

/// <summary>
/// Represents the login state for a user, should be validated on each request with
/// <see cref="VeloSdk.CheckToken"/> unless being used in a flow.
/// </summary>
[JsonConverter(typeof(TokenDeserializer))]
[Affine("On each successful operation outside of revocation tokens are rotated")]
public class Token
{
    internal Token(string encoded)
    {
        // If a token is issued to a user it is not considered a secret, plus it is encrypted. 
        // No need for the constant time implementation here.
        _sealed = Base64.Decode(encoded);
    }

    /// <returns>The raw <see cref="Token"/> without any encoding.</returns>
    public ByteArrayContent AsRawContent()
    {
        return new ByteArrayContent(content: _sealed);
    }

    /// <summary>
    /// Encode the <see cref="Token"/> in base64 without padding (what the IdP expects).
    /// </summary>
    /// <returns>The base64 encoded <see cref="Token"/> without padding.</returns>
    public string Encode()
    {
        return Base64.EncodeCt(_sealed);
    }

    /// <summary>
    /// Try to decode the <see cref="Token"/>.
    /// </summary>
    /// <param name="encoded">The encoded <see cref="Token"/></param>
    /// <returns>
    /// A successful <see cref="Result{T,TE}"/> if the input was valid base64, otherwise 
    /// a <see cref="Base64Error"/>.
    /// </returns>
    public static Result<Token, Base64Error> TryDecode(string encoded)
    {
        try
        {
            return Result<Token, Base64Error>.Success(new Token(encoded));
        }
        catch (Base64Error err)
        {
            return Result<Token, Base64Error>.Failure(err);
        }
    }

    private readonly byte[] _sealed;
}

internal class TokenDeserializer : JsonConverter<Token>
{
    public override Token ReadJson(
        JsonReader reader, 
        Type objectType, 
        Token? existingValue, 
        bool hasExistingValue, 
        JsonSerializer serializer
    )
    {
        if (reader.Value is string encoded)
        {
            return Token.TryDecode(encoded)
                .MapOrElse(
                    err => {
                        throw new JsonSerializationException(
                            $"Invalid token format: {err}"
                        );
                    },
                    token => token
                );
        }
        throw new JsonSerializationException("Expected string value for Token");
    }

    public override void WriteJson(JsonWriter writer, Token? value, JsonSerializer serializer)
    {
        if (value is null)
        {
            writer.WriteNull();
            return;
        }
        writer.WriteValue(value.Encode());
    }
}

// todo: non-exhaustive as users can decide what they would like to store in a token.
// Our current users are not taking advantage of this functionality. In the future as our IdP is a compiler these SDKs 
// will be generated in a compilation layer, so this limitation purely on the SDK will not impact future users and there
// requirements.
/// <summary>
/// The result of <see cref="VeloSdk.CheckToken"/>, containing a new <see cref="Token"/> and the user's identifier.
/// </summary>
/// <param name="UserId">The user identifier associated with the <see cref="Token"/></param>
/// <param name="CurrentToken">The new <see cref="Token"/> as the old <see cref="Token"/> is no longer usable.</param>
[MustUse("When a `Token` is used to get a `PeekedToken`, the old `Token` is invalid. Use `PeekedToken.CurrentToken`")]
public record PeekedToken
(
    [property: JsonProperty("user_id")]
    string UserId,
    [property: JsonProperty("new")]
    Token CurrentToken
);

/// <summary>
/// An opaque error to prevent leaking information about the server's internal state to a potentially malicious client.
/// </summary>
public record Opaque
{
    /// <summary>
    /// Represent the <see cref="Opaque"/> type as the error of a <see cref="Result{T,TE}"/>.
    /// </summary>
    /// <typeparam name="T">The success type of the <see cref="Result{T,TE}"/></typeparam>
    /// <returns>A new <see cref="Result{T,TE}"/> in the error state.</returns>
    public static Result<T, Opaque> AsResult<T>()
    {
        return Result<T, Opaque>.Failure(new Opaque());
    }
}
