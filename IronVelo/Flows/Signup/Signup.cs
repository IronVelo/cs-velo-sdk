using ResultAble;
using IronVelo.Types;
using IronVelo.Utils;
namespace IronVelo.Flows.Signup;
using Newtonsoft.Json;

/// <summary>
/// The desired username was already taken and the signup flow is terminated early
/// </summary>
public record UsernameAlreadyExists
{
    /// <inheritdoc/>
    public override string ToString() => "Requested username already exists";
}

internal record HelloSignupArgs([property: JsonProperty("username")] string Username);
internal record HelloSignupTlArgs([property: JsonProperty("hello_signup")] HelloSignupArgs Args);

[Result]
internal partial record HelloSignupRes(
    [property: Error, JsonProperty("username_exists", NullValueHandling = NullValueHandling.Ignore)] 
    bool UsernameExists
);

/// <summary>
/// The ingress state to a signin flow
/// </summary>
public record HelloSignup
{
    internal HelloSignup(FlowClient client) { _client = client; }
    private readonly FlowClient _client;
    
    /// <summary>
    /// Initiate a signup, requesting reservation of some username for the duration of the permit 
    /// </summary>
    /// <param name="username">The desired username</param>
    /// <returns>
    /// <list type="bullet"><item>
    /// <description>Success: The username is available and now reserved for the lifetime of the permit</description>
    /// </item><item>
    /// <description>Failure: The username is already in use</description>
    /// </item></list>
    /// </returns>
    /// <exception cref="Exceptions.RequestError">
    /// There was an unexpected error encountered when making the request, see <see cref="Exceptions.RequestErrorKind"/>
    /// for more information
    /// </exception>
    public FutResult<SetPassword, UsernameAlreadyExists> Start(string username)
    {
        return FutResult<SetPassword, UsernameAlreadyExists>.From(
            _client.SendRequest<HelloSignupTlArgs, HelloSignupRes>(
                new HelloSignupTlArgs(new HelloSignupArgs(username))
            ).ContinueWith(task => {
                var res = Resolve.Get(task);
                return res.MapOr(
                    Result<SetPassword, UsernameAlreadyExists>.Success(new SetPassword(_client, res.Permit)),
                    _ => Result<SetPassword, UsernameAlreadyExists>.Failure(new UsernameAlreadyExists())
                );
            })
        );
    }
    
    /// <summary>
    /// For multipart flows, resume a state from the serializable representation.
    /// </summary>
    public ResumeSignupState Resume() => new(_client);
}

/// <summary>
/// Resume the signup flow from a <see cref="SignupState"/>, used in multistep flows to avoid the need for tracking
/// state yourself.
/// </summary>
/// <remarks>
/// <b>How do I know which method to invoke?:</b><br/>
/// In order to properly use <see cref="HelloSignup.Resume"/> you should have invoked <see cref="IState{TF}.Serialize"/>
/// in order to provide the state to the client. When the client continues the flow, they should return this serialized
/// representation of the state to your server.
/// <br/><br/>
/// The serialized states all include a <c>State</c> property, which indicates which method to call.
/// <br/><br/>
/// <b>Mapping:</b>
/// <list type="bullet">
/// <item>
///     <description><c>State.Password => ResumeSignupState.Password(state)</c></description>
/// </item>
/// <item>
///     <description><c>State.SetupFirstMfa => ResumeSignupState.SetupFirstMfa(state)</c></description>
/// </item>
/// <item>
///     <description><c>State.VerifyOtpSetup => ResumeSignupState.VerifyOtpSetup(state)</c></description>
/// </item>
/// <item>
///     <description><c>State.VerifyTotpSetup => ResumeSignupState.VerifyTotpSetup(state)</c></description>
/// </item>
/// <item>
///     <description><c>State.SetupMfaOrFinalize => ResumeLoginState.SetupMfaOrFinalize(state)</c></description>
/// </item>
/// </list>
/// <para>
/// <b>Security:</b><br/>
/// There are no security concerns here, but if you would like to catch errors / tampering early, you can sign the
/// serialized representation using <c>HMAC</c>. To clarify, this is not necessary, the IdP will detect any tampering
/// itself.
/// </para>
/// </remarks>
public record ResumeSignupState
{
    internal ResumeSignupState(FlowClient client) { _client = client; }
    private readonly FlowClient _client;

    /// <summary>
    /// Resumes the signup flow from the password setup state.
    /// </summary>
    /// <param name="state">The current state of the signup flow.</param>
    /// <returns>A new <see cref="SetPassword"/> instance to handle the password setup step.</returns>
    public SetPassword Password(SignupState state) => new(_client, state.Permit);

    /// <summary>
    /// Resumes the signup flow from the setup first MFA state.
    /// </summary>
    /// <param name="state">The current state of the signup flow.</param>
    /// <returns>A new <see cref="SetupMfa"/> instance to handle the setup first MFA step.</returns>
    public SetupMfa SetupFirstMfa(SignupState state) => new(_client, state.Permit);

    /// <summary>
    /// Resumes the signup flow from the setup MFA or finalize state.
    /// </summary>
    /// <param name="state">The current state of the signup flow.</param>
    /// <returns>A new <see cref="NewMfaOrFinalize"/> instance to handle the setup MFA or finalize step.</returns>
    public NewMfaOrFinalize SetupMfaOrFinalize(SignupState state) => new(_client, state.Permit, state.AlreadySetup);

    /// <summary>
    /// Resumes the signup flow from the TOTP verification setup state.
    /// </summary>
    /// <param name="state">The current state of the signup flow.</param>
    /// <returns>A new <see cref="JustVerifyTotp"/> instance to handle the TOTP verification setup step.</returns>
    public JustVerifyTotp VerifyTotpSetup(SignupState state) => new(_client, state.Permit, state.AlreadySetup);

    /// <summary>
    /// Resumes the signup flow from the OTP verification setup state.
    /// </summary>
    /// <param name="state">The current state of the signup flow.</param>
    /// <returns>A new <see cref="VerifyMfaSetup"/> instance to handle the OTP verification setup step.</returns>
    public VerifyMfaSetup VerifyOtpSetup(SignupState state) => new(_client, state.Permit, state.Current, state.AlreadySetup);
}

internal record PasswordArgs([property: JsonProperty("password")] string Password);
internal record PasswordTlArgs([property: JsonProperty("password")] PasswordArgs Args);

/// <summary>
/// The state for setting up a password for the user
/// </summary>
public record SetPassword : IState<SignupState>
{
    internal SetPassword(FlowClient client, string? permit)
    {
        _client = client;
        _permit = permit;
    }
    private readonly FlowClient _client;
    private readonly string? _permit;
    
    /// <inheritdoc />
    public SignupState GetState() => new(SignupStateE.Password, _permit!, new List<MfaKind>(), null);
    /// <inheritdoc />
    public string Serialize() => JsonConvert.SerializeObject(GetState());
    
    // no need for result type as the Password type ensures that this is infallible
    /// <summary>
    /// Set the password for the user
    /// </summary>
    /// <param name="password">The users desired password</param>
    /// <exception cref="Exceptions.RequestError">
    /// There was an unexpected error encountered when making the request, see <see cref="Exceptions.RequestErrorKind"/>
    /// for more information
    /// </exception>
    public async Task<SetupMfa> Password(Password password)
    {
        var res = await _client.SendRequest<PasswordTlArgs, Infallible>(
            new PasswordTlArgs(new PasswordArgs(password.ToString())), _permit
        );

        return new SetupMfa(_client, res.Permit);
    }
}

internal record NoMfaRes([property: JsonProperty("issue_token")] string Token);

/// <summary>
/// State for setting up the user's first MFA method
/// </summary>
public record SetupMfa : SetupMfaBase<VerifyTotpSetup, VerifyMfaSetup, SignupState>
{
    internal SetupMfa(FlowClient client, string? permit) : base(client, permit) { }
    /// <inheritdoc />
    public override SignupState GetState() => new(SignupStateE.SetupFirstMfa, Permit!, new List<MfaKind>(), null);
    /// <inheritdoc />
    protected override VerifyMfaSetup SimpleValidator(FlowClient c, string? p, MfaKind k, List<MfaKind> ps)
        => new(c, p, k, ps);
    /// <inheritdoc />
    protected override VerifyTotpSetup TotpValidator(FlowClient c, string? p, string pUri, List<MfaKind> ps)
        => new(c, p, pUri, ps);

    /// <summary>
    /// Set an account up without any MFA. We strongly recommend not using this feature, and this feature is not ever
    /// controlled by the SDK, it is enforced via the IdP itself. Simply using this API without setting up your 
    /// IdP to handle this will yield errors. That is why this branch is not published to nuget, we simply do not 
    /// recommend ever using it under any circumstance. It also improves the cognitive complexity of our SDK, 
    /// violating the correct by construction properties we shot for. 
    /// </summary>
    public async Task<Token> NoMfa()
    {
        var ret = await MakeRequest<NoMfaRes>(new SetupMfaKind.Null(StateIdent));
        return new Token(ret.Ret.Token);
    }
}

/// <summary>
/// State for verifying that the user successfully setup their authenticator app
/// </summary>
public record VerifyTotpSetup: VerifyTotpBase<NewMfaOrFinalize, JustVerifyTotp, VerifyTotpSetup, SignupState>
{
    internal VerifyTotpSetup(
        FlowClient client, string? permit, string pUri, List<MfaKind> prevSetup
    ) : base(new JustVerifyTotp(client, permit, prevSetup), pUri) { }
}

/// <summary>
/// Used internally by the <see cref="VerifyTotpSetup"/> and is near equivalent to the <see cref="VerifyTotpSetup"/>
/// state, just does not include the provisioning Uri for authenticator apps.
/// </summary>
public record JustVerifyTotp : JustVerifyTotpBase<NewMfaOrFinalize, JustVerifyTotp, SignupState>
{
    internal JustVerifyTotp(FlowClient client, string? permit,  List<MfaKind> prevSetup) : base(client, permit, prevSetup) { }
    /// <inheritdoc />
    public override SignupState GetState() => new(SignupStateE.VerifyTotpSetup, Permit!, PrevSetup, MfaKind.Totp);
    /// <inheritdoc />
    protected override NewMfaOrFinalize OkTransition(string? permit) => 
        new(Client, permit, PrevSetup.Append(MfaKind.Totp).ToList()); 
}

/// <summary>
/// State for verifying that the MFA method is controlled/accessible by the user.
/// </summary>
public record VerifyMfaSetup : VerifyMfaBase<NewMfaOrFinalize, VerifyMfaSetup, SignupState>
{
    internal VerifyMfaSetup(FlowClient client, string? permit, MfaKind? kind, List<MfaKind> ps) 
        : base(client, permit, kind, ps) {}
    /// <inheritdoc />
    public override SignupState GetState() => new(SignupStateE.VerifyOtpSetup, Permit!, PrevSetup, Kind);
    /// <inheritdoc />
    protected override NewMfaOrFinalize Success(string? p, List<MfaKind> ns) => new(Client, p, ns);
}

/// <summary>
/// State for completing the signup process or setting up another MFA method
/// </summary>
public record NewMfaOrFinalize : NewMfaOrFinalizeBase<VerifyTotpSetup, VerifyMfaSetup, SignupState>
{
    internal NewMfaOrFinalize(FlowClient client, string? permit, List<MfaKind> prevSetup) 
        : base(client, permit, prevSetup) { }
    /// <inheritdoc />
    public override SignupState GetState() => new(SignupStateE.SetupMfaOrFinalize, Permit!, PrevSetup, null);
    /// <inheritdoc />
    protected override VerifyMfaSetup SimpleValidator(FlowClient c, string? p, MfaKind k, List<MfaKind> ps)
        => new(c, p, k, ps);
    /// <inheritdoc />
    protected override VerifyTotpSetup TotpValidator(FlowClient c, string? p, string pUri, List<MfaKind> ps)
        => new(c, p, pUri, ps); 
}
