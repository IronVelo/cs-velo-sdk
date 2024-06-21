using IronVelo.Types;
using IronVelo.Utils;
using Newtonsoft.Json;
using ResultAble;
namespace IronVelo.Flows.Login;

internal record HelloLoginArgs(
    [property: JsonProperty("username")]
    string Username,
    [property: JsonProperty("password")]
    string Password
);

internal record HelloLoginTlArgs(
    [property: JsonProperty("hello_login")]
    HelloLoginArgs HelloLogin
);

[Result]
internal partial record HelloRes
(
    [property: Ok, JsonProperty("hello_login", NullValueHandling = NullValueHandling.Ignore)]
    MfaKind[] MfaKinds,
    [property: Error, JsonProperty("failure", NullValueHandling = NullValueHandling.Ignore)]
    LoginError LoginError
);

/// <summary>
/// The ingress state to a login flow
/// </summary>
public record HelloLogin
{
    internal HelloLogin(FlowClient client) { _client = client; }
    private readonly FlowClient _client;
    
    /// <summary>
    /// Initiate a login, requesting access to the <see cref="InitMfa"/> state
    /// </summary>
    /// <param name="username">The username for the account the user wishes to sign into</param>
    /// <param name="password">What the user believes the password associated with username is</param>
    /// <returns>
    /// <list type="bullet"><item>
    /// <description>Success: The provided password was associated with the username</description>
    /// </item><item>
    /// <description>Failure: The initiation of the login failed, and the reason is provided</description>
    /// </item></list>
    /// </returns>
    /// <exception cref="Exceptions.RequestError">
    /// There was an unexpected error encountered when making the request, see <see cref="Exceptions.RequestErrorKind"/>
    /// for more information
    /// </exception> 
    public FutResult<InitMfa, LoginError> Start(string username, Password password)
    {
        return FutResult<InitMfa, LoginError>.From(_client.SendRequest<HelloLoginTlArgs, HelloRes>(
            new HelloLoginTlArgs(new HelloLoginArgs(username, password.ToString()))
        ).ContinueWith(task => { 
            var res = Resolve.Get(task);
            return res
                .UnwrapRet()
                .ToResult()
                .Map(val => new InitMfa(_client, res.Permit, val));
            })
        );
    }
    
    /// <summary>
    /// For multipart flows, resume a state from the serializable representation.
    /// </summary>
    public ResumeLoginState Resume() => new(_client);
}

/// <summary>
/// Resume the login flow from a <see cref="LoginState"/>, used in multistep flows to avoid the need for tracking state
/// yourself. Initiated via <see cref="HelloLogin.Resume"/>.
/// </summary>
/// <remarks>
/// <b>How do I know which method to invoke?:</b><br/>
/// In order to properly use <see cref="HelloLogin.Resume"/> you should have invoked <see cref="IState{TF}.Serialize"/>
/// in order to provide the state to the client. When the client continues the flow, they should return this serialized
/// representation of the state to your server.
/// <br/><br/>
/// The serialized states all include a <c>State</c> property, which indicates which method to call.
/// <br/><br/>
/// <b>Mapping:</b>
/// <list type="bullet">
/// <item>
///     <description><c>State.InitMfa => ResumeLoginState.InitMfa(state)</c></description>
/// </item>
/// <item>
///     <description><c>State.RetryInitMfa => ResumeLoginState.RetryInitMfa(state)</c></description>
/// </item>
/// <item>
///     <description><c>State.VerifyOtp => ResumeLoginState.VerifyOtp(state)</c></description>
/// </item>
/// <item>
///     <description><c>State.VerifyTotp => ResumeLoginState.VerifyTotp(state)</c></description>
/// </item>
/// </list>
/// <para>
/// <b>Security:</b><br/>
/// There are no security concerns here, but if you would like to catch errors / tampering early, you can sign the
/// serialized representation using <c>HMAC</c>. To clarify, this is not necessary, the IdP will detect any tampering
/// itself.
/// </para>
/// </remarks>
public record ResumeLoginState
{
    internal ResumeLoginState(FlowClient client) { _client = client; }
    private readonly FlowClient _client;

    /// <summary>
    /// Resumes the login flow from the initial MFA state.
    /// </summary>
    /// <param name="state">The current state of the login flow.</param>
    /// <returns>A new <see cref="InitMfa"/> instance to handle the initial MFA step.</returns>
    public InitMfa InitMfa(LoginState state) => new(_client, state.Permit, state.AvailableMfa);

    /// <summary>
    /// Resumes the login flow from the retry initial MFA state.
    /// </summary>
    /// <param name="state">The current state of the login flow.</param>
    /// <returns>A new <see cref="RetryInitMfa"/> instance to handle the retry initial MFA step.</returns>
    public RetryInitMfa RetryInitMfa(LoginState state) => new(_client, state.Permit, state.AvailableMfa);

    /// <summary>
    /// Resumes the login flow from the OTP verification state.
    /// </summary>
    /// <param name="state">The current state of the login flow.</param>
    /// <returns>A new <see cref="VerifyMfa"/> instance to handle the OTP verification step.</returns>
    public VerifyMfa VerifyOtp(LoginState state) => new(_client, state.Permit, state.AvailableMfa);

    /// <summary>
    /// Resumes the login flow from the TOTP verification state.
    /// </summary>
    /// <param name="state">The current state of the login flow.</param>
    /// <returns>A new <see cref="VerifyTotp"/> instance to handle the TOTP verification step.</returns>
    public VerifyTotp VerifyTotp(LoginState state) => new(_client, state.Permit, state.AvailableMfa);
}

internal record InitMfaArgs([property: JsonProperty("kind")] MfaKind InitMfa);

internal class InitMfaTlArgs
{
    [JsonProperty("init_mfa", NullValueHandling = NullValueHandling.Ignore)]
    public InitMfaArgs? InitMfa { get; }

    [JsonProperty("retry_init_mfa", NullValueHandling = NullValueHandling.Ignore)]
    public InitMfaArgs? RetryInitMfa { get; }

    public InitMfaTlArgs(InitMfaArgs args, string state)
    {
        switch (state)
        {
            case "init_mfa":
                InitMfa = args;
                break;
            case "retry_init_mfa":
                RetryInitMfa = args;
                break;
            default:
                throw new ArgumentException("Invalid state", nameof(state));
        }
    }
}

/// <summary>
/// Abstract record for initiating MFA, transitioning to verification states. This allows one to generically handle
/// retry initialization and the first initialization of MFA.
/// <br/><br/>
/// Implemented by <see cref="InitMfa"/> and <see cref="RetryInitMfa"/>.
/// </summary>
/// <typeparam name="TF">
/// The state implementing <c>MfaBase</c>. To generically reference this use <c>IState&lt;LoginState&gt;</c>/>.
/// </typeparam>
public abstract record MfaBase<TF> : IState<LoginState>
{
    internal MfaBase(FlowClient client, string? permit, MfaKind[] mfaKinds)
    {
        _client = client;
        Permit = permit;
        MfaKinds = mfaKinds;
    }

    private readonly FlowClient _client;
    
    /// <summary>
    /// Representation of the future state
    /// </summary>
    protected readonly string? Permit;
    
    /// <summary>
    /// The available MFA kinds for the user
    /// </summary>
    protected MfaKind[] MfaKinds { get; }
    
    /// <inheritdoc />
    public abstract LoginState GetState();
    /// <inheritdoc />
    public string Serialize() => JsonConvert.SerializeObject(GetState());
    
    private async Task<Result<T, TE>> Guard<T, TE>(MfaKind mfaKind, Func<Task<T>> ok, Func<TE> err)
    {
        return Array.Exists(MfaKinds, kind => kind == mfaKind)
            ? Result<T, TE>.Success(await ok())
            : Result<T, TE>.Failure(err());
    }

    private static InitMfaTlArgs CreateInitMfaTlArgs(MfaKind mfaKind, string state) =>
        new(new InitMfaArgs(mfaKind), state);
    
    private async Task<T> PerformActionUnchecked<T>(MfaKind mfaKind, Func<string?, T> createResponse, string state)
    {
        var ret = await _client.SendRequest<InitMfaTlArgs, Infallible>(
            CreateInitMfaTlArgs(mfaKind, state),
            Permit
        );
        return createResponse(ret.Permit);
    }
    
    /// <summary>
    /// Used internally by <see cref="MfaAction{T}"/> for transitioning states
    /// </summary>
    protected FutResult<T, TE> PerformMfaAction<T, TE>(
        MfaKind mfaKind, Func<string?, T> createResponse, string state, Func<TE> err
    ) => FutResult<T, TE>.From(Guard(mfaKind, () => PerformActionUnchecked(mfaKind, createResponse, state), err));

    /// <summary>
    /// Used internally for transitioning states
    /// </summary>
    protected abstract FutResult<T, TF> MfaAction<T>(MfaKind mfaKind, Func<string?, T> ok);
        
    /// <summary>
    /// Communicate to the server that SMS MFA is being used. Send the OTP to the user's phone number
    /// </summary>
    /// <returns>
    /// <list type="bullet"><item>
    /// <description>
    /// Success: SMS OTP has been set up as an MFA method for the user and the OTP text has been dispatched, the
    /// verification state where the user must submit the received OTP is returned.
    /// </description>
    /// </item><item>
    /// <description>
    /// Failure: The user has not previously set up SMS OTP as an MFA method, the current state is returned allowing you
    /// to try another MFA method.
    /// </description>
    /// </item></list>
    /// </returns>
    /// <exception cref="Exceptions.RequestError">
    /// There was an unexpected error encountered when making the request, see <see cref="Exceptions.RequestErrorKind"/>
    /// for more information
    /// </exception> 
    public FutResult<VerifyMfa, TF> Sms() 
        => MfaAction(MfaKind.Sms, permit => new VerifyMfa(_client, permit, MfaKinds));
    
    /// <summary>
    /// Communicate to the IdP that email MFA is being used. Send the OTP to the user's email address
    /// </summary>
    /// <returns>
    /// <list type="bullet"><item>
    /// <description>
    /// Success: Email OTP has been set up as an MFA method for the user and the OTP email has been dispatched, the
    /// verification state where the user must submit the received OTP is returned.
    /// </description>
    /// </item><item>
    /// <description>
    /// Failure: The user has not previously set up email OTP as an MFA method, the current state is returned allowing
    /// you to try another MFA method.
    /// </description>
    /// </item></list>
    /// </returns>
    /// <exception cref="Exceptions.RequestError">
    /// There was an unexpected error encountered when making the request, see <see cref="Exceptions.RequestErrorKind"/>
    /// for more information
    /// </exception>
    public FutResult<VerifyMfa, TF> Email() 
        => MfaAction(MfaKind.Email, permit => new VerifyMfa(_client, permit, MfaKinds));

    /// <summary>
    /// Communicate to the IdP that TOTP is being used.
    /// </summary>
    /// <returns>
    /// <list type="bullet"><item>
    /// <description>
    /// Success: The user has set up TOTP (authenticator apps) as an MFA method, the verification state where the user
    /// must submit the current code is returned.
    /// </description>
    /// </item><item>
    /// <description>
    /// Failure: The user has not previously set up TOTP (authenticator apps) as an MFA method, the current state is
    /// returned allowing you to try another MFA method.
    /// </description>
    /// </item></list>
    /// </returns>
    /// <exception cref="Exceptions.RequestError">
    /// There was an unexpected error encountered when making the request, see <see cref="Exceptions.RequestErrorKind"/>
    /// for more information
    /// </exception>
    public FutResult<VerifyTotp, TF> Totp()
        => MfaAction(MfaKind.Totp, permit => new VerifyTotp(_client, permit, MfaKinds));
}

/// <summary>
/// State for initiating/selecting an MFA method
/// </summary>
public record InitMfa : MfaBase<InitMfa>
{
    internal InitMfa(FlowClient client, string? permit, MfaKind[] mfaKinds) : base(client, permit, mfaKinds) { }
    private const string StateName = "init_mfa";
    
    /// <inheritdoc />
    public override LoginState GetState() => new(LoginStateE.InitMfa, Permit!, MfaKinds);

    /// <inheritdoc />
    protected override FutResult<T, InitMfa> MfaAction<T>(MfaKind mfaKind, Func<string?, T> ok) 
        => PerformMfaAction(mfaKind, ok, StateName, () => this);
}

/// <summary>
/// State where the last MFA method verification failed, allowing for re-selecting the MFA method and retrying 
/// </summary>
public record RetryInitMfa : MfaBase<RetryInitMfa>
{
    internal RetryInitMfa(FlowClient client, string? permit, MfaKind[] mfaKinds) : base(client, permit, mfaKinds) { }
    private const string StateName = "retry_init_mfa";
    
    /// <inheritdoc />
    public override LoginState GetState() => new(LoginStateE.RetryInitMfa, Permit!, MfaKinds);
    
    /// <inheritdoc />
    protected override FutResult<T, RetryInitMfa> MfaAction<T>(MfaKind mfaKind, Func<string?, T> ok) 
        => PerformMfaAction(mfaKind, ok, StateName, () => this);
}

[Result]
internal partial record OtpCheckRes(
    [property: Ok, JsonProperty("issue_token", NullValueHandling = NullValueHandling.Ignore)]
    string Token,
    [property: Error, JsonProperty("incorrect_otp", NullValueHandling = NullValueHandling.Ignore)]
    bool Failure
);

internal record VerifyMfaArgs([property: JsonProperty("guess")] string Guess);

/// <summary>
/// Abstract record for verifying the selected MFA method. This is an implementation detail and should be ignored.
/// </summary>
public abstract record VerifyMfaBase : IState<LoginState>
{
    internal VerifyMfaBase(FlowClient client, string? permit, MfaKind[] mfaKinds)
    {
        Client = client;
        Permit = permit;
        MfaKinds = mfaKinds;
    }
    /// <inheritdoc cref="FlowClient"/>
    protected readonly FlowClient Client;
    /// <summary>
    /// The permit representing a future state transition. An implementation detail and should be ignored.
    /// </summary>
    protected readonly string? Permit;
    /// <summary>
    /// The MFA kinds that the user has previously set up.
    /// </summary>
    protected readonly MfaKind[] MfaKinds;
    
    /// <inheritdoc />
    public abstract LoginState GetState();
    /// <inheritdoc />
    public string Serialize() => JsonConvert.SerializeObject(GetState());
    
    internal Result<Token, RetryInitMfa> CheckRes(Response<OtpCheckRes> res)
    {
        return res
            .UnwrapRet()
            .ToResult()
            .MapOr(
                Result<Token, RetryInitMfa>.Failure(new RetryInitMfa(Client, res.Permit, MfaKinds)), 
                rawToken => Result<Token, RetryInitMfa>.Success(new Token(rawToken)
            )
        );
    }
}

internal record VerifyMfaTlArgs([property: JsonProperty("check_simple_mfa")] VerifyMfaArgs Args);

/// <summary>
/// State for verifying SMS and Email MFA kinds. The main method being <see cref="Guess"/>.
/// </summary>
public record VerifyMfa : VerifyMfaBase
{
    internal VerifyMfa(FlowClient client, string? permit, MfaKind[] mfaKinds) : base(client, permit, mfaKinds) { }
    /// <inheritdoc />
    public override LoginState GetState() => new(LoginStateE.VerifyOtp, Permit!, MfaKinds);

    /// <summary>
    /// Verify the Email or SMS OTP dispatched in <see cref="InitMfa"/> or <see cref="RetryInitMfa"/> and finalize the
    /// login process iif correct.
    /// </summary>
    /// <param name="guess">What the user believes the current OTP is</param>
    /// <returns>
    /// <list type="bullet"><item>
    /// <description>
    /// Success: The OTP was correct and now the user is logged in with the returned <see cref="Token"/>
    /// </description>
    /// </item><item>
    /// <description>
    /// Failure: The guess was incorrect, the state transitions to <see cref="RetryInitMfa"/>, allowing them to
    /// re-select an MFA method for this login and try again.
    /// </description>
    /// </item></list>
    /// </returns>
    /// <exception cref="Exceptions.RequestError">
    /// There was an unexpected error encountered when making the request, see <see cref="Exceptions.RequestErrorKind"/>
    /// for more information
    /// </exception> 
    public FutResult<Token, RetryInitMfa> Guess(SimpleOtp guess)
    {
        return FutResult<Token, RetryInitMfa>
            .From(Client.SendRequest<VerifyMfaTlArgs, OtpCheckRes>(
                new VerifyMfaTlArgs(new VerifyMfaArgs(guess.ToString())), Permit
            )
                .ContinueWith(task => CheckRes(Resolve.Get(task)))
            );
    }
}

internal record VerifyTotpTlArgs([property: JsonProperty("check_totp")] VerifyMfaArgs Args);

/// <summary>
/// State for verifying the code from the user's authenticator app (TOTP). The main method being <see cref="Guess"/>.
/// </summary>
public record VerifyTotp : VerifyMfaBase
{
    internal VerifyTotp(FlowClient client, string? permit, MfaKind[] mfaKinds) : base(client, permit, mfaKinds) { }
    
    /// <inheritdoc />
    public override LoginState GetState() => new(LoginStateE.VerifyTotp, Permit!, MfaKinds);

    /// <summary>
    /// Verify the TOTP and finalize the login process iif correct
    /// </summary>
    /// <param name="guess">What the user believes the current code is</param>
    /// <returns>
    /// <list type="bullet"><item>
    /// <description>
    /// Success: The guess was correct and now the user is logged in with the returned <see cref="Token"/>
    /// </description>
    /// </item><item>
    /// <description>
    /// Failure: The guess was incorrect, the state transitions to <see cref="RetryInitMfa"/>, allowing them to
    /// re-select an MFA method for this login and try again.
    /// </description>
    /// </item></list>
    /// </returns> 
    /// <exception cref="Exceptions.RequestError">
    /// There was an unexpected error encountered when making the request, see <see cref="Exceptions.RequestErrorKind"/>
    /// for more information
    /// </exception>
    public FutResult<Token, RetryInitMfa> Guess(Totp guess)
    {
        return FutResult<Token, RetryInitMfa>
            .From(Client.SendRequest<VerifyTotpTlArgs, OtpCheckRes>(
                new VerifyTotpTlArgs(new VerifyMfaArgs(guess.ToString())), Permit
            )
                .ContinueWith(res => CheckRes(Resolve.Get(res)))
            );
    }
}