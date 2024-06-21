using IronVelo.Types;
using IronVelo.Utils;
namespace IronVelo.Flows;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Exceptions;

/// <summary>
/// The interface which all non-ingress states must implement.
/// </summary>
/// <typeparam name="TF">The serializable representation of states in the current flow.</typeparam>
public interface IState<out TF>
{
    /// <summary>
    /// Get the current state in a serializable form. Used in multistep flows to avoid the need for tracking state
    /// yourself.
    /// </summary>
    /// <returns>The current state in a serializable form.</returns>
    public TF GetState();
    
    /// <summary>
    /// Get a serialized representation of the current state. Used in multistep flows to avoid the need for tracking
    /// state yourself.
    /// </summary>
    /// <returns>The serialized representation of the current state.</returns>
    public string Serialize() => JsonConvert.SerializeObject(GetState());
}

/// <summary>
/// Possible MFA Kinds
/// </summary>
public enum MfaKind
{
    /// <summary>
    /// Authenticator apps
    /// </summary>
    Totp,
    /// <summary>
    /// SMS OTP (not recommended due to intrinsic security vulnerabilities in SMS technology.
    /// SMS OTPs are inherently insecure and should not be used for sensitive authentication processes.
    /// The insecurity of SMS OTPs is due to factors such as SIM swapping, interception, and phishing,
    /// and is not related to our implementation).
    /// </summary>
    Sms,
    /// <summary>
    /// Email OTP
    /// </summary>
    Email
}

/// <summary>
/// Client for interacting with the IdP's flows.
/// </summary>
public record FlowClient
{
    internal FlowClient(RouteType route, H2Client client)
    {
        Route = route;
        Client = client;
    }
    
    private RouteType Route { get; }
    private H2Client Client { get; }

    /// <summary>
    /// Send a request to the IdP, continuing whatever flow you're currently in
    /// </summary>
    /// <param name="args">The arguments that the state expects</param>
    /// <param name="permit">The permit associated with the state transition</param>
    /// <param name="jsonConverter">An optional JsonConverter for custom serialization</param>
    /// <typeparam name="TA">The type of the arguments that the state expects</typeparam>
    /// <typeparam name="TR">The expected return type of the state</typeparam>
    /// <returns>A response with the <c>TR></c> as the <c>Ret</c> field</returns>
    /// <exception cref="RequestError">
    /// There was an error encountered when making the request, see <see cref="RequestErrorKind"/> for more information
    /// </exception>
    public async Task<Response<TR>> SendRequest<TA, TR>(TA args, string? permit = null, JsonConverter? jsonConverter = null)
    {
        var req = new Request<TA>(args, permit).Serialize(jsonConverter);
        var response = await Client.MakeDefaultRequest(
            new StringContent(
                content: req,
                encoding: Encoding.UTF8,
                mediaType: "application/json"
            ),
            Route
        );

        CheckStatus(response.StatusCode);

        var result = JsonConvert.DeserializeObject<Response<TR>>(
            await response.Content.ReadAsStringAsync()
        );
        
        if (result == null) { throw new RequestError(RequestErrorKind.Deserialization); }

        return result;
    }

    private static void CheckStatus(HttpStatusCode statusCode)
    {
        switch (statusCode)
        {
            case HttpStatusCode.OK:
                break;
            case HttpStatusCode.Unauthorized:
                throw new RequestError(RequestErrorKind.State, "Attempted to transition to an unauthorized state");
            case HttpStatusCode.PreconditionFailed:
                throw new RequestError(RequestErrorKind.Precondition, "Permit expired or arguments were illegal");
            case HttpStatusCode.BadRequest:
                throw new RequestError(RequestErrorKind.Request, "Malformed request");
            case HttpStatusCode.InternalServerError:
                throw new RequestError(RequestErrorKind.Internal, "Internal Server Error");
            default:
                throw new RequestError(RequestErrorKind.General, $"Unexpected Status from Velo: ${statusCode}");
        }
    }
}

/// <summary>
/// Default request structure for any flow in the IdP (non-ingress).
/// </summary>
/// <param name="Args">The arguments for the state.</param>
/// <param name="Permit">The permit allowing access to the state.</param>
/// <typeparam name="TA">The type of arguments to provide to the state.</typeparam>
public record Request<TA>(
    [property: JsonProperty("args")] TA Args, 
    [property: JsonProperty("permit")] string? Permit
)
{
    /// <summary>
    /// Serialize a request to a flow in the IdP.
    /// </summary>
    /// <param name="jsonConverter">Any options to enable for the serialization.</param>
    /// <returns>The serialized request.</returns>
    public string Serialize(JsonConverter? jsonConverter = null)
    {
        var settings = new JsonSerializerSettings();
        settings.Converters.Add(new StringEnumConverter());
        
        if (jsonConverter != null)
        {
            settings.Converters.Add(jsonConverter);
        }
        
        return JsonConvert.SerializeObject(this, settings);
    }
}

/// <summary>
/// Response from all flows in the IdP.
/// </summary>
/// <param name="Ret">The return value of the state.</param>
/// <param name="Permit">The permit representing a future state transition.</param>
/// <typeparam name="TR">The return value type.</typeparam>
public record Response<TR>(
    [property: JsonProperty("ret", NullValueHandling = NullValueHandling.Ignore)]
    TR Ret,
    [property: JsonProperty("permit", NullValueHandling = NullValueHandling.Ignore)]
    string? Permit
)
{
    /// <summary>
    /// Maps the return value to a new type using the specified mapping function.
    /// If the return value is null, the provided default value function is called instead.
    /// </summary>
    /// <typeparam name="T">The type to map to.</typeparam>
    /// <param name="d">A function that provides the default value if the return value is null.</param>
    /// <param name="map">A function that maps the return value to the new type.</param>
    /// <returns>The mapped value or the default value.</returns>
    public T MapOrElse<T>(Func<T> d, Func<TR, T> map) => Ret != null ? map(Ret) : d();
    /// <summary>
    /// Maps the return value to a new type using the specified mapping function.
    /// If the return value is null, the provided default value is returned instead.
    /// </summary>
    /// <typeparam name="T">The type to map to.</typeparam>
    /// <param name="d">The default value if the return value is null.</param>
    /// <param name="map">A function that maps the return value to the new type.</param>
    /// <returns>The mapped value or the default value.</returns>
    public T MapOr<T>(T d, Func<TR, T> map) => Ret != null ? map(Ret) : d;
    /// <summary>
    /// Unwraps the return value, returning it if it is not null.
    /// </summary>
    /// <returns>The return value.</returns>
    /// <exception cref="RequestError">Thrown when the return value is null.</exception>
    public TR UnwrapRet() => Ret != null ? Ret! : throw new RequestError(RequestErrorKind.Deserialization);
}

internal record Infallible { }

/// <summary>
/// Indicates an error that took place on initiation of the login flow
/// </summary>
/// <remarks>
/// Variants:
/// <list type="bullet">
/// <item><see cref="LoginError.UsernameNotFound"/></item>
/// <item><see cref="LoginError.IncorrectPassword"/></item>
/// <item><see cref="LoginError.IllegalMfaKinds"/></item>
/// </list>
/// </remarks>
public enum LoginError
{
    /// <summary>
    /// No user was found with the provided username
    /// </summary>
    UsernameNotFound,
    /// <summary>
    /// The password was incorrect
    /// </summary>
    IncorrectPassword,
    /// <summary>
    /// The user had no MFA kinds setup. This is the standard login flow's equivalent of <see cref="WrongFlow"/>.
    /// If you receive this error message, it means you should be using the MigrateLogin.
    /// </summary>
    IllegalMfaKinds,
    /// <summary>
    /// The user attempted to use the MigrateLogin when they already had MFA setup
    /// </summary>
    WrongFlow
}

internal abstract class SetupMfaKind
{
    private SetupMfaKind(string ident)
    {
        Ident = ident;
    }
    public readonly string Ident;

    public sealed class Totp : SetupMfaKind
    {
        public Totp(string ident) : base(ident) { }
    }

    public sealed class Sms : SetupMfaKind
    {
        public Sms(string ident, string phoneNumber) : base(ident)
        {
            PhoneNumber = phoneNumber;
        }
        public string PhoneNumber { get; }
    }

    public sealed class Email : SetupMfaKind
    {
        public Email(string ident, string emailAddress) : base(ident)
        {
            EmailAddress = emailAddress;
        }
        public string EmailAddress { get; }
    }

    public sealed class Null : SetupMfaKind
    {
        public Null(string ident) : base(ident) { }
    }
}

internal class SetupMfaArgsSerializer : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return typeof(SetupMfaKind).IsAssignableFrom(objectType);
    }

    private void WriteNonNull(JsonWriter writer, Action<JsonWriter> write)
    {
        writer.WriteStartObject();
        write(writer);
        writer.WriteEndObject();
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is SetupMfaKind setupMfaKind)
        {
            writer.WriteStartObject();
            writer.WritePropertyName(setupMfaKind.Ident);
            writer.WriteStartObject();
            writer.WritePropertyName("kind");
            switch (setupMfaKind)
            {
                case SetupMfaKind.Totp:
                    WriteNonNull(writer, w =>
                    {
                        w.WritePropertyName("Totp"); 
                        w.WriteNull();
                    });
                    break;
                case SetupMfaKind.Sms sms:
                    WriteNonNull(writer, w =>
                    {
                        w.WritePropertyName("Sms"); 
                        w.WriteValue(sms.PhoneNumber);
                    });
                    break;
                case SetupMfaKind.Email email:
                    WriteNonNull(writer, w =>
                    {
                        w.WritePropertyName("Email"); 
                        w.WriteValue(email.EmailAddress);
                    });
                    break;
                case SetupMfaKind.Null:
                    writer.WriteNull();
                    break;
                default:
                    throw new JsonSerializationException("Unknown SetupMfaKind variant");
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        else
        {
            throw new JsonSerializationException("Expected SetupMfaKind object value.");
        }
    }

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        throw new NotImplementedException("Deserialization is not supported for SetupMfaKind.");
    }
    
    public override bool CanRead => false;
    public override bool CanWrite => true; 
}

internal record SetupMfaArgs(
    [property: JsonProperty("kind", NullValueHandling = NullValueHandling.Ignore)] SetupMfaKind? Kind
);
internal record SetupFirstMfaTlArgs([property: JsonProperty("setup_first_mfa")] SetupMfaArgs Args);
internal record SetupTotpRes([property: JsonProperty("setup_totp")] string ProvisioningUri);

/// <summary>
/// Used for generically interacting with MFA initiation in <c>MigrateLogin</c> as well as <c>Signup</c> states.
/// <br/>
/// <para>
/// <b>Implementors:</b>
/// <list type="bullet">
/// <item>
///     <term><b>MigrateLogin:</b> <see cref="Flows.MigrateLogin.SetupMfa"/></term>
/// </item>
/// <item>
///     <term><b>MigrateLogin:</b> <see cref="Flows.MigrateLogin.NewMfaOrLogin"/></term>
/// </item>
/// <item>
///     <term><b>Signup:</b> <see cref="Flows.Signup.SetupMfa"/></term>
/// </item>
/// <item>
///     <term><b>Signup:</b> <see cref="Flows.Signup.NewMfaOrFinalize"/></term>
/// </item>
/// </list>
/// </para>
/// 
/// </summary>
/// <typeparam name="TVt">The state to transition to under a success.</typeparam>
/// <typeparam name="TVs">The current state to transition back to for retries.</typeparam>
/// <typeparam name="TF">The serializable representation of states in the current flow.</typeparam>
public abstract record SetupMfaBase<TVt, TVs, TF> : IState<TF>
{
    internal SetupMfaBase(FlowClient client, string? permit, List<MfaKind>? ps = null, string stateIdent = "setup_first_mfa")
    {
        PrevSetup = ps ?? new List<MfaKind>();
        _client = client;
        Permit = permit;
        StateIdent = stateIdent;
    }
    
    /// <summary>
    /// The identifier for the current state, used in making requests to the IdP. Implementation detail and should be
    /// ignored.
    /// </summary>
    protected readonly string StateIdent;
    private readonly FlowClient _client;
    /// <summary>
    /// The permit representing a future state transition. An implementation detail and should be ignored.
    /// </summary>
    protected readonly string? Permit;
    /// <summary>
    /// The MFA kinds which have already been set up.
    /// </summary>
    public List<MfaKind> PrevSetup { get; }

    /// <inheritdoc/>
    public abstract TF GetState();
    /// <inheritdoc/>
    public string Serialize() => JsonConvert.SerializeObject(GetState());
    
    internal Task<Response<TR>> MakeRequest<TR>(SetupMfaKind kind)
    {
        return _client.SendRequest<SetupMfaKind, TR>(
            kind, Permit, new SetupMfaArgsSerializer()
        );
    }

    /// <summary>
    /// The initializer for the TOTP verification state.
    /// </summary>
    /// <param name="c">The client for communicating with the IdP.</param>
    /// <param name="p">The current permit.</param>
    /// <param name="pUri">The provisioning Uri (scanned as a QR code for authenticator apps).</param>
    /// <param name="ps">The previously-setup MFA kinds.</param>
    /// <returns>The TOTP verification state.</returns>
    protected abstract TVt TotpValidator(FlowClient c, string? p, string pUri, List<MfaKind> ps);
    /// <summary>
    /// The initializer for the simple MFA verification state. Simple MFA kinds are Email and SMS OTP.
    /// </summary>
    /// <param name="c">The client for communicating with the IdP.</param>
    /// <param name="p">The current permit.</param>
    /// <param name="k">The simple MFA kind to verify.</param>
    /// <param name="ps">The previously-setup MFA kinds.</param>
    /// <returns>The simple MFA verification state.</returns>
    protected abstract TVs SimpleValidator(FlowClient c, string? p, MfaKind k, List<MfaKind> ps);
    
    /// <summary>
    /// Set up TOTP (for authenticator apps) as an available MFA method for the user
    /// </summary>
    /// <exception cref="Exceptions.RequestError">
    /// There was an unexpected error encountered when making the request, see <see cref="Exceptions.RequestErrorKind"/>
    /// for more information
    /// </exception>
    public async Task<TVt> Totp()
    {
        var ret = await MakeRequest<SetupTotpRes>(new SetupMfaKind.Totp(StateIdent));
        return TotpValidator(_client, ret.Permit, ret.Ret.ProvisioningUri, PrevSetup);
    }
    
    /// <summary>
    /// Set up SMS OTPs as an available MFA method for the user
    /// </summary>
    /// <param name="phoneNumber">The users phone number (callers responsibility to validate)</param>
    /// <exception cref="Exceptions.RequestError">
    /// There was an unexpected error encountered when making the request, see <see cref="Exceptions.RequestErrorKind"/>
    /// for more information
    /// </exception>
    public async Task<TVs> Sms(string phoneNumber)
    {
        var ret = await MakeRequest<Infallible>(new SetupMfaKind.Sms(StateIdent, phoneNumber));
        return SimpleValidator(_client, ret.Permit, MfaKind.Sms, PrevSetup);
    }
    
    /// <summary>
    /// Set up email OTPs as an available MFA method for the user
    /// </summary>
    /// <param name="emailAddress">The users email address</param>
    /// <exception cref="Exceptions.RequestError">
    /// There was an unexpected error encountered when making the request, see <see cref="Exceptions.RequestErrorKind"/>
    /// for more information
    /// </exception>
    public async Task<TVs> Email(string emailAddress)
    {
        var ret = await MakeRequest<Infallible>(new SetupMfaKind.Email(StateIdent, emailAddress));
        return SimpleValidator(_client, ret.Permit, MfaKind.Email, PrevSetup);
    }
}

internal record VerifyTotpSetupArgs([property: JsonProperty("guess")] string Guess);
internal record VerifyTotpSetupTlArgs([property: JsonProperty("verify_totp")] VerifyTotpSetupArgs Args);
internal record VerifyTotpSetupRes([property: JsonProperty("verify_totp")] bool Success);

/// <summary>
/// The base abstract record for <see cref="Flows.Signup.JustVerifyTotp"/> and
/// <see cref="Flows.MigrateLogin.JustVerifyTotp"/>. Used internally by the base abstract record
/// <see cref="VerifyTotpBase{TS,TIi,TI,TF}"/>, the only difference being that
/// <see cref="VerifyTotpBase{TS,TIi,TI,TF}"/> requires the provisioning URI exist. This can be used to generically
/// handle TOTP verification for the Signup and MigrateLogin flows.
/// </summary>
/// <typeparam name="TS">The state to transition to if successful.</typeparam>
/// <typeparam name="TI">The implementor of this state, returned to under failures.</typeparam>
/// <typeparam name="TF">The serializable state for the current flow, e.g. <see cref="SignupState"/>.</typeparam>
public abstract record JustVerifyTotpBase<TS, TI, TF> : IState<TF>
    where TI : JustVerifyTotpBase<TS, TI, TF>
{
    internal JustVerifyTotpBase(FlowClient client, string? permit,  List<MfaKind> prevSetup)
    {
        Client = client;
        Permit = permit;
        PrevSetup = prevSetup;
    }
    
    /// <inheritdoc cref="FlowClient"/>
    protected readonly FlowClient Client;
    /// <summary>
    /// The permit representing a future state transition. An implementation detail and should be ignored.
    /// </summary>
    protected string? Permit;
    /// <summary>
    /// The MFA kinds which have already been set up.
    /// </summary>
    protected readonly List<MfaKind> PrevSetup;

    /// <inheritdoc/>
    public abstract TF GetState();
    /// <inheritdoc/> 
    public string Serialize() => JsonConvert.SerializeObject(GetState());

    /// <summary>
    /// Initializes the success state when the guess is correct.
    /// </summary>
    /// <param name="permit">The permit representing a future state transition.</param>
    /// <returns>The success state.</returns>
    protected abstract TS OkTransition(string? permit);
    
    /// <summary>
    /// Verify that TOTP is properly setup for the user by having them submit the code from their authenticator app
    /// </summary>
    /// <param name="guess">What the user believes the current TOTP code is</param>
    /// <returns>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// Success: The guess was correct and TOTP is now set as one of the users available MFA methods and they can either
    /// finish the flow or set up another MFA method
    /// </description>
    /// </item>
    /// <item>
    /// <description>Failure: The guess was incorrect, the user must try again</description>
    /// </item>
    /// </list>
    /// </returns>
    /// <exception cref="Exceptions.RequestError">
    /// There was an unexpected error encountered when making the request, see <see cref="Exceptions.RequestErrorKind"/>
    /// for more information
    /// </exception> 
    public FutResult<TS, TI> Guess(Totp guess)
    {
        return FutResult<TS, TI>.From(
            Client.SendRequest<VerifyTotpSetupTlArgs, VerifyTotpSetupRes>(
                new VerifyTotpSetupTlArgs(new VerifyTotpSetupArgs(guess.ToString())), Permit
            )
            .ContinueWith(task => {
                var ret = Resolve.Get(task);
                return ret.UnwrapRet().Success
                    ? Result<TS, TI>.Success(OkTransition(ret.Permit))
                    : Result<TS, TI>.Failure((TI)this with { Permit = ret.Permit });
            })
        );
    } 
}

/// <summary>
/// Near equivalent to <see cref="JustVerifyTotpBase{TS,TI,TF}"/>, with the only exception being this includes the
/// provisioning URI while <see cref="JustVerifyTotpBase{TS,TI,TF}"/> does not.
/// </summary>
/// <typeparam name="TS">The state to transition to if successful.</typeparam>
/// <typeparam name="TIi">The implementor of <see cref="JustVerifyTotpBase{TS,TI,TF}"/></typeparam>
/// <typeparam name="TI">The implementor of this state, returned to under failures.</typeparam>
/// <typeparam name="TF">The serializable state for the current flow, e.g. <see cref="SignupState"/>.</typeparam>
public abstract record VerifyTotpBase<TS, TIi, TI, TF> : IState<TF>
    where TIi : JustVerifyTotpBase<TS, TIi, TF>
    where TI : VerifyTotpBase<TS, TIi, TI, TF>
{
    internal VerifyTotpBase(TIi inner, string pUri)
    {
        ProvisioningUri = pUri;
        _inner = inner;
    }
    
    /// <summary>
    /// The provisioning URI for the user's authenticator app. One should display this in their UI as a QR code. It
    /// should always be transmitted via a secure channel, but that is a given when interacting with this SDK.
    /// </summary>
    public string ProvisioningUri { get; }
    private TIi _inner;

    /// <inheritdoc/>
    public TF GetState() => _inner.GetState();
    /// <inheritdoc/>
    public string Serialize() { return JsonConvert.SerializeObject(GetState()); }    
    private TI FromInner(TIi inner) => (TI)(this with { _inner = inner });
    
    /// <inheritdoc cref="JustVerifyTotpBase{TS,TI,TF}.Guess"/>
    public FutResult<TS, TI> Guess(Totp guess)
    {
        return _inner.Guess(guess).MapErr(FromInner);
    }
}

internal record VerifyMfaSetupArgs([property: JsonProperty("guess")] string Guess);
internal record VerifyMfaSetupTlArgs([property: JsonProperty("verify_simple_otp")] VerifyMfaSetupArgs Args);
internal record VerifyMfaSetupRet([property: JsonProperty("maybe_retry_simple")] bool Retry);

/// <summary>
/// The base abstract record for <see cref="Flows.Signup.VerifyMfaSetup"/> and
/// <see cref="Flows.MigrateLogin.VerifyMfaSetup"/>, allowing for reuse of logic between the Signup and MigrateLogin
/// flows. The only relevant method to users being <see cref="Guess"/>.
/// </summary>
/// <typeparam name="TS">The state to transition to when the guess is correct.</typeparam>
/// <typeparam name="TI">The implementor of this state, transitioned back to under incorrect guesses for retrying.</typeparam>
/// <typeparam name="TF">The serializable state for the current flow, e.g. <see cref="SignupState"/>.</typeparam>
public abstract record VerifyMfaBase<TS, TI, TF> : IState<TF>
    where TI : VerifyMfaBase<TS, TI, TF>
{
    internal VerifyMfaBase(FlowClient client, string? permit, MfaKind? kind, List<MfaKind> ps)
    {
        Client = client;
        Permit = permit;
        Kind = kind;
        PrevSetup = ps;
    }

    /// <inheritdoc cref="FlowClient"/>
    protected readonly FlowClient Client;
    /// <summary>
    /// The permit representing a future state transition. An implementation detail and should be ignored.
    /// </summary>
    protected string? Permit;
    /// <summary>
    /// The MFA kinds which have already been set up.
    /// </summary>
    protected readonly List<MfaKind> PrevSetup;
    /// <summary>
    /// The MFA kind currently being verified.
    /// </summary>
    protected readonly MfaKind? Kind;

    /// <inheritdoc/>
    public abstract TF GetState();
    /// <inheritdoc/>
    public string Serialize() => JsonConvert.SerializeObject(GetState());
    /// <summary>
    /// Initializes the success state when the guess is correct.
    /// </summary>
    /// <param name="permit">The permit representing a future state transition.</param>
    /// <param name="nowSetup">The MFA kinds the user has now set up.</param>
    /// <returns>The success state.</returns>
    protected abstract TS Success(string? permit, List<MfaKind> nowSetup);
    
    private TS OkTransition(string? permit)
    {
        var updatedSetup = PrevSetup.ToList();
        if (Kind.HasValue)
        {
            updatedSetup.Add(Kind.Value);
        }

        return Success(permit, updatedSetup);
    }
    
    /// <summary>
    /// Verify that the MFA method is controlled/accessible by the user
    /// </summary>
    /// <param name="guess">What the user believes the OTP is</param>
    /// <returns>
    /// <list type="bullet"><item>
    /// <description>
    /// Success: The OTP was correct and now the MFA method is set as one of the users available MFA methods
    /// </description>
    /// </item><item>
    /// <description>Failure: The guess was incorrect, the user must try again</description>
    /// </item></list>
    /// </returns>
    /// <exception cref="Exceptions.RequestError">
    /// There was an unexpected error encountered when making the request, see <see cref="Exceptions.RequestErrorKind"/>
    /// for more information
    /// </exception>
    public FutResult<TS, TI> Guess(SimpleOtp guess)
    {
        return FutResult<TS, TI>.From(
            Client.SendRequest<VerifyMfaSetupTlArgs, VerifyMfaSetupRet?>(
                new VerifyMfaSetupTlArgs(new VerifyMfaSetupArgs(guess.ToString())), Permit
            ).ContinueWith(task => {
                var ret = Resolve.Get(task);
                return ret.Ret is { Retry: true } 
                    ? Result<TS, TI>.Failure((TI)(this with { Permit = ret.Permit })) 
                    : Result<TS, TI>.Success(OkTransition(ret.Permit));
            })
        );
    }
}

internal record NewMfaOrFinalizeTlArgs([property: JsonProperty("setup_mfa_or_issue_token")] SetupMfaArgs Args);
internal record FinalizeSignupRet([property: JsonProperty("issue_token")] string Token);

/// <summary>
/// The base abstract record for potentially terminal states in the Signup and MigrateLogin flows, implemented by
/// <see cref="Flows.MigrateLogin.NewMfaOrLogin"/> and <see cref="Flows.Signup.NewMfaOrFinalize"/>. Useful for sharing
/// logic between the signup and migrate login flows.
/// <br/>
/// <para>
/// At this state the user can either <see cref="Finish"/> or set up a new MFA kind. This implements
/// <see cref="SetupMfaBase{TVt,TVs,TF}"/> for initiating the setup of a new MFA kind, allowing one to reuse their
/// existing logic in handling this.
/// </para>
/// </summary>
/// <typeparam name="TVt">The state to transition to under a success.</typeparam>
/// <typeparam name="TVs">The current state to transition back to for retries.</typeparam>
/// <typeparam name="TF">The serializable representation of states in the current flow.</typeparam>
public abstract record NewMfaOrFinalizeBase<TVt, TVs, TF> : SetupMfaBase<TVt, TVs, TF>
{
    internal NewMfaOrFinalizeBase(FlowClient c, string? p, List<MfaKind> ps) 
        : base(c, p, ps, "setup_mfa_or_issue_token") { }

    /// <inheritdoc/>
    public abstract override TF GetState();
    
    /// <summary>
    /// Finish the flow, logging the user into the account
    /// </summary>
    /// <returns>
    /// A token associated with the user's account
    /// </returns>
    /// <exception cref="Exceptions.RequestError">
    /// There was an unexpected error encountered when making the request, see <see cref="Exceptions.RequestErrorKind"/>
    /// for more information
    /// </exception>
    public async Task<Token> Finish()
    {
        var ret = await MakeRequest<FinalizeSignupRet>(new SetupMfaKind.Null(StateIdent));
        return new Token(ret.Ret.Token);
    }
}
