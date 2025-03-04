using MustUse;
using AffineTypes;
using IronVelo.Exceptions;
using Newtonsoft.Json;

namespace IronVelo.Types;
using Base64;

/// <summary>
/// Represents the permission to perform some action on a user's own account,
/// granted by an administrator with the associated permissions. This is used
/// for account recovery, and the ticketing feature is not enabled in all IdP
/// builds.
/// </summary>
[JsonConverter(typeof(TicketDeserializer))]
[Affine("Tickets are strictly single use")]
public class Ticket
{
    internal Ticket(string encoded)
    {
        _sealed = Base64.Decode(encoded);
    }

    /// <returns>The raw <see cref="Ticket"/> without any encoding.</returns>
    public ByteArrayContent AsRawContent()
    {
        return new ByteArrayContent(content: _sealed);
    }

    /// <summary>
    /// Encode the <see cref="Ticket"/> in base64 without padding (what the IdP expects).
    /// </summary>
    /// <returns>The base64 encoded <see cref="Ticket"/> without padding.</returns>
    public string Encode()
    {
        return Base64.EncodeCt(_sealed);
    }

    /// <summary>
    /// Try to decode the <see cref="Ticket"/>.
    /// </summary>
    /// <param name="encoded">The encoded <see cref="Ticket"/></param>
    /// <returns>
    /// A successful <see cref="Result{T,TE}"/> if the input was valid base64, otherwise 
    /// a <see cref="Base64Error"/>.
    /// </returns>
    public static Result<Ticket, Base64Error> TryDecode(string encoded)
    {
        try
        {
            return Result<Ticket, Base64Error>.Success(new Ticket(encoded));
        }
        catch (Base64Error err)
        {
            return Result<Ticket, Base64Error>.Failure(err);
        }
    }

    private readonly byte[] _sealed;
}

internal class TicketDeserializer : JsonConverter<Ticket>
{
    public override Ticket ReadJson(
        JsonReader reader, 
        Type objectType, 
        Ticket? existingValue, 
        bool hasExistingValue, 
        JsonSerializer serializer
    )
    {
        if (reader.Value is string encoded)
        {
            return Ticket.TryDecode(encoded)
                .MapOrElse(
                    err => {
                        throw new JsonSerializationException(
                            $"Invalid ticket format: {err}"
                        );
                    },
                    token => token
                );
        }
        throw new JsonSerializationException("Expected string value for Ticket");
    }

    public override void WriteJson(JsonWriter writer, Ticket? value, JsonSerializer serializer)
    {
        if (value is null)
        {
            writer.WriteNull();
            return;
        }
        writer.WriteValue(value.Encode());
    }
}
