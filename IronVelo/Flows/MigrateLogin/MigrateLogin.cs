using IronVelo.Exceptions;
using IronVelo.Utils;

namespace IronVelo.Flows.MigrateLogin;
using Types;
using Newtonsoft.Json;

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

internal record HelloRes(
    [property: JsonProperty("failure", NullValueHandling = NullValueHandling.Ignore)]
    LoginError? LoginError
);

/// <summary>
/// The ingress state to a login flow
/// </summary>
public record HelloLogin
{
    internal HelloLogin(FlowClient client) { _client = client; }
    private readonly FlowClient _client;
    
    /// <summary>
    /// Initiate a login, requesting access to the <see cref="SetupMfa"/> state
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
    public FutResult<SetupMfa, LoginError> Start(string username, Password password)
    {
        return FutResult<SetupMfa, LoginError>.From(
            _client.SendRequest<HelloLoginTlArgs, HelloRes>(
                new HelloLoginTlArgs(new HelloLoginArgs(username, password.ToString()))
            ).ContinueWith(task => {
                var res = Resolve.Get(task);
                return res.MapOrElse(
                    () => Result<SetupMfa, LoginError>.Success(new SetupMfa(_client, res.Permit)),
                    ret => ret.LoginError is { } error
                        ? Result<SetupMfa, LoginError>.Failure(error)
                        : throw new RequestError(RequestErrorKind.Deserialization)
                );
            })
        );
    }

    /// <summary>
    /// For multipart flows, resume a state from the serializable representation.
    /// </summary>
    public ResumeLoginState Resume() => new ResumeLoginState(_client);
}

/// <summary>
/// Resume the login migration flow from a <see cref="MigrateLoginState"/>, used in multistep flows to avoid the need
/// for tracking state yourself.
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
///     <description><c>State.SetupFirstMfa => ResumeLoginState.SetupFirstMfa(state)</c></description>
/// </item>
/// <item>
///     <description><c>State.VerifyOtpSetup => ResumeLoginState.VerifyOtpSetup(state)</c></description>
/// </item>
/// <item>
///     <description><c>State.VerifyTotpSetup => ResumeLoginState.VerifyTotpSetup(state)</c></description>
/// </item>
/// <item>
///     <description><c>State.NewMfaOrLogin => ResumeLoginState.NewMfaOrLogin(state)</c></description>
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
    /// Resumes the login migration flow from the setup first MFA state.
    /// </summary>
    /// <param name="state">The current state of the login migration flow.</param>
    /// <returns>A new <see cref="SetupMfa"/> instance to handle the setup first MFA step.</returns>
    public SetupMfa SetupFirstMfa(MigrateLoginState state) => new(_client, state.Permit);

    /// <summary>
    /// Resumes the login migration flow from the new MFA or login state.
    /// </summary>
    /// <param name="state">The current state of the login migration flow.</param>
    /// <returns>A new <see cref="NewMfaOrLogin"/> instance to handle the new MFA or login step.</returns>
    public NewMfaOrLogin NewMfaOrLogin(MigrateLoginState state) => new(_client, state.Permit, state.AlreadySetup);

    /// <summary>
    /// Resumes the login migration flow from the TOTP verification setup state.
    /// </summary>
    /// <param name="state">The current state of the login migration flow.</param>
    /// <returns>A new <see cref="JustVerifyTotp"/> instance to handle the TOTP verification setup step.</returns>
    public JustVerifyTotp VerifyTotpSetup(MigrateLoginState state) => new(_client, state.Permit, state.AlreadySetup);

    /// <summary>
    /// Resumes the login migration flow from the OTP verification setup state.
    /// </summary>
    /// <param name="state">The current state of the login migration flow.</param>
    /// <returns>A new <see cref="VerifyMfaSetup"/> instance to handle the OTP verification setup step.</returns>
    public VerifyMfaSetup VerifyOtpSetup(MigrateLoginState state) => new(_client, state.Permit, state.Current, state.AlreadySetup);
}

/// <summary>
/// Equivalent to the <see cref="Flows.Signup.SetupMfa"/> state, just for existing users migrating to the IronVelo IdP's
/// requirements. Can be interacted generically allowing for code reuse with the signup variant via the base abstract
/// record <see cref="SetupMfaBase{TVt,TVs,TF}"/>
/// </summary>
public record SetupMfa : SetupMfaBase<VerifyTotpSetup, VerifyMfaSetup, MigrateLoginState>
{
    internal SetupMfa(FlowClient client, string? permit) : base(client, permit) { }
    
    /// <inheritdoc />
    public override MigrateLoginState GetState() 
        => new(MigrateLoginStateE.SetupFirstMfa, Permit!, new List<MfaKind>(), null);
    /// <inheritdoc />
    protected override VerifyMfaSetup SimpleValidator(FlowClient c, string? p, MfaKind k, List<MfaKind> ps)
        => new(c, p, k, ps);
    /// <inheritdoc />
    protected override VerifyTotpSetup TotpValidator(FlowClient c, string? p, string pUri, List<MfaKind> ps)
        => new(c, p, pUri, ps);
}

/// <summary>
/// State for verifying that the user successfully setup their authenticator app. Equivalent to the
/// <see cref="Flows.Signup.VerifyTotpSetup"/> state, just for existing users migrating to the IronVelo IdP's
/// requirements. Can be interacted with generically allowing for code reuse with the signup variant via the base
/// abstract record <see cref="VerifyTotpBase{TS,TIi,TI,TF}"/>.
/// </summary>
public record VerifyTotpSetup : VerifyTotpBase<NewMfaOrLogin, JustVerifyTotp, VerifyTotpSetup, MigrateLoginState>
{
    internal VerifyTotpSetup(
        FlowClient client, string? permit, string pUri, List<MfaKind> prevSetup
    ) : base(new JustVerifyTotp(client, permit, prevSetup), pUri) { } 
}
/// <summary>
/// Used internally by the <see cref="VerifyTotpSetup"/> and is near equivalent to the <see cref="VerifyTotpSetup"/>
/// state, just does not include the provisioning Uri for authenticator apps. Equivalent to the
/// <see cref="Flows.Signup.JustVerifyTotp"/> state, just for existing users migrating to the IronVelo IdP's
/// requirements. Can be interacted with generically allowing for code reuse with the signup variant via the base
/// abstract record <see cref="JustVerifyTotpBase{TS,TI,TF}"/>.
/// </summary>
public record JustVerifyTotp : JustVerifyTotpBase<NewMfaOrLogin, JustVerifyTotp, MigrateLoginState>
{
    internal JustVerifyTotp(FlowClient client, string? permit, List<MfaKind> prevSetup) : base(client, permit, prevSetup) { }

    /// <inheritdoc/>
    public override MigrateLoginState GetState()
        => new(MigrateLoginStateE.VerifyTotpSetup, Permit!, PrevSetup, MfaKind.Totp);
    /// <inheritdoc/>
    protected override NewMfaOrLogin OkTransition(string? permit) => 
        new(Client, permit, PrevSetup.Append(MfaKind.Totp).ToList());
}

/// <summary>
/// State for verifying that the MFA method is controlled/accessible by the user. Equivalent to the
/// <see cref="Flows.Signup.VerifyMfaSetup"/> state, just for existing users migrating to the IronVelo IdP's
/// requirements. Can be interacted with generically allowing for code reuse with the signup variant via the base
/// abstract record <see cref="VerifyMfaBase{TS,TI,TF}"/>.
/// </summary>
public record VerifyMfaSetup : VerifyMfaBase<NewMfaOrLogin, VerifyMfaSetup, MigrateLoginState>
{
    internal VerifyMfaSetup(FlowClient client, string? permit, MfaKind? kind, List<MfaKind> ps) 
        : base(client, permit, kind, ps) {}
    /// <inheritdoc/>
    public override MigrateLoginState GetState() => new(MigrateLoginStateE.VerifyOtpSetup, Permit!, PrevSetup, Kind);
    /// <inheritdoc/>
    protected override NewMfaOrLogin Success(string? p, List<MfaKind> ns) => new(Client, p, ns);
}

/// <summary>
/// State for completing the login migration, allowing the user to either set up another MFA method or finish the
/// login process. Equivalent to the <see cref="Flows.Signup.NewMfaOrFinalize"/> state, just for existing users
/// migrating to the IronVelo IdP's requirements. Can be interacted with generically allowing for code reuse with the
/// signup variant via the base abstract record <see cref="NewMfaOrFinalizeBase{TVt,TVs,TF}"/>.
/// </summary>
public record NewMfaOrLogin : NewMfaOrFinalizeBase<VerifyTotpSetup, VerifyMfaSetup, MigrateLoginState>
{
    internal NewMfaOrLogin(FlowClient client, string? permit, List<MfaKind> prevSetup) 
        : base(client, permit, prevSetup) { }
    /// <inheritdoc/>
    public override MigrateLoginState GetState() =>
        new(MigrateLoginStateE.NewMfaOrLogin, Permit!, PrevSetup, null);
    /// <inheritdoc/>
    protected override VerifyMfaSetup SimpleValidator(FlowClient c, string? p, MfaKind k, List<MfaKind> ps)
        => new(c, p, k, ps);
    /// <inheritdoc/>
    protected override VerifyTotpSetup TotpValidator(FlowClient c, string? p, string pUri, List<MfaKind> ps)
        => new(c, p, pUri, ps); 
}