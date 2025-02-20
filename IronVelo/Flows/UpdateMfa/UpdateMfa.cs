using IronVelo.Types;
using IronVelo.Utils;
using MustUse;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ResultAble;
using System.Diagnostics;


namespace IronVelo.Flows.UpdateMfa;

internal record HelloArgs(
    [property: JsonProperty("token")] string Token
);

internal record HelloTlArgs(
    [property: JsonProperty("hello")] HelloArgs Hello
);

internal record HelloRes(
    [property: JsonProperty("mfa")] MfaKind[] MfaKinds,
    [property: JsonProperty("token")] string Token
);

internal record HelloTlRes(
    [property: JsonProperty("hello")] HelloRes Result
);

/// <summary>
/// The return type of the update MFA state machine's ingress function.
/// 
/// This provides access to the following state (<c>State</c>) as well as the
/// new <c>Token</c>, ensuring the user is not logged out from the process.
/// </summary>
[MustUse("Ignoring the wrapped `Token` will log the user out")]
public record StartUpdateWithToken(
    Token Token,
    StartUpdate State
);

/// <summary>
/// Represents the core Update MFA flow that allows users to add, modify, or remove MFA methods.
/// The flow enforces security by requiring verification of existing MFA before changes can be made.
/// </summary>
/// <remarks>
/// The update MFA flow follows this general sequence:
/// <list type="number">
///   <item><description>
///   User initiates flow and verifies identity with existing MFA
///   </description></item>
///   <item><description>
///     User chooses to add/update or remove MFA methods
///   </description></item>
///   <item><description>
///     For additions/updates:
///     <list type="bullet">
///       <item><description>
///         User sets up new MFA method
///       </description></item>
///       <item><description>
///         User verifies access to new method
///       </description></item>
///       <item><description>
///         Changes are finalized
///       </description></item>
///     </list>
///   </description></item>
///   <item><description>
///     For removals:
///     <list type="bullet">
///       <item><description>
///         System verifies user has other MFA methods
///       </description></item>
///       <item><description>
///         Removal is finalized
///       </description></item>
///     </list>
///   </description></item>
/// </list>
/// 
/// Security constraints:
/// <list type="bullet">
///   <item><description>
///     Users must verify existing MFA before making changes
///   </description></item>
///   <item><description>
///     Users cannot remove their only MFA method
///   </description></item>
///   <item><description>
///     All changes require final confirmation with a valid token
///   </description></item>
///   <item><description>
///     No updates to the user account will happen in any intermediary state,
///     all updates are atomic / have mutual exclusion, and will never open user
///     invariants. The update will only <b>ever</b> take place in <c>Finalize</c>* states.
///   </description></item>
/// </list>
/// </remarks>
public record HelloUpdateMfa 
{
    internal HelloUpdateMfa(FlowClient client) { _client = client; }
    private readonly FlowClient _client;

    /// <summary>
    /// Initiates the MFA update flow by verifying the user's identity and retrieving their current 
    /// MFA configuration.
    /// </summary>
    /// <param name="token">The user's current authentication token</param>
    /// <returns>
    /// A StartUpdateWithToken containing:
    /// - Updated Token: New token for continued authentication
    /// - State: Initial state for the update flow with available MFA methods
    /// </returns>
    /// <exception cref="Exceptions.RequestError">
    /// The request failed due to network issues or invalid token
    /// </exception>
    public Task<StartUpdateWithToken> Start(Token token)
    {
        return _client
            .SendRequest<HelloTlArgs, HelloTlRes>(
                new HelloTlArgs(new HelloArgs(token.Encode()))
            )
            .ContinueWith(task => {
                var res = Resolve.Get<Response<HelloTlRes>>(task);
                return new StartUpdateWithToken(
                    new Token(res.Ret.Result.Token),
                    new StartUpdate(_client, res.Permit, res.Ret.Result.MfaKinds)
                );
            });
    }

    /// <summary>
    /// For multipart flows, resume a state from the serializable representation.
    /// </summary>
    public ResumeUpdateState Resume() => new(_client);
}

/// <summary>
/// Resume the MFA update flow from a serialized <see cref="UpdateMfaState"/>.
/// </summary>
/// <remarks>
/// <b>How to Resume:</b>
/// The serialized states include a <c>State</c> property that indicates which method to call.
/// 
/// <b>Mapping:</b>
/// <list type="bullet">
/// <item><description>
///   <c>State.StartUpdate => ResumeUpdateState.InitMfaCheck(state)</c>
/// </description></item>
/// <item><description>
///   <c>State.CheckOtp => ResumeUpdateState.CheckOtp(state)</c>
/// </description></item>
/// <item><description>
///   <c>State.CheckTotp => ResumeUpdateState.CheckTotp(state)</c>
/// </description></item>
/// <item><description>
///   <c>State.DecideUpdate => ResumeUpdateState.DecideUpdate(state)</c>
/// </description></item>
/// <item><description>
///   <c>State.FinalizeRemoval => ResumeUpdateState.FinalizeRemoval(state)</c>
/// </description></item>
/// <item><description>
///   <c>State.EnsureOtp => ResumeUpdateState.EnsureOtp(state)</c>
/// </description></item>
/// <item><description>
///   <c>State.EnsureTotp => ResumeUpdateState.EnsureTotp(state)</c>
/// </description></item>
/// <item><description>
///   <c>State.FinalizeUpdate => ResumeUpdateData.FinalizeUpdate(state)</c>
/// </description></item>
/// </list>
/// 
/// <b>Security:</b>
/// If necessary, use a message authentication code (MAC) to sign the serialized state for 
/// integrity validation. The IdP will catch any and all issues here itself, however, it
/// may be desirable to catch this downstream.
/// </remarks>
public record ResumeUpdateState
{
    internal ResumeUpdateState(FlowClient client) { _client = client; }
    private readonly FlowClient _client;

    /// <summary>
    /// Resumes the MFA update from the initial verification state.
    /// </summary>
    public StartUpdate InitMfaCheck(UpdateMfaState state) => new(
        _client,
        state.Permit,
        state.OldMfa
    );

    /// <summary>
    /// Resumes the MFA update from the OTP verification state.
    /// </summary>
    public CheckOtp CheckOtp(UpdateMfaState state) => new(
        _client,
        state.Permit,
        state.OldMfa
    );

    /// <summary>
    /// Resumes the MFA update from the TOTP verification state.
    /// </summary>
    public CheckTotp CheckTotp(UpdateMfaState state) => new(
        _client,
        state.Permit,
        state.OldMfa
    );

    /// <summary>
    /// Resumes the MFA update from the decision state where users choose their next step.
    /// </summary>
    public Decide DecideUpdate(UpdateMfaState state) => new(
        _client,
        state.Permit,
        state.OldMfa
    );

    /// <summary>
    /// Resumes the MFA update from the final removal confirmation state.
    /// </summary>
    public FinalizeRemoval FinalizeRemoval(UpdateMfaState state) => new(
        _client,
        state.Permit,
        state.OldMfa
    );

    /// <summary>
    /// Resumes the MFA update from the OTP ensure state. This state is not for
    /// authentication, but ensuring that using this new MFA kind is sound and
    /// will not result in the user being locked out of their account.
    /// </summary>
    public EnsureOtp EnsureOtp(UpdateMfaState state) => new(
        _client,
        state.Permit,
        state.OldMfa
    );

    /// <summary>
    /// Resumes the MFA update from the TOTP ensure state. This state is not for
    /// authentication, but ensuring that using this new MFA kind is sound and
    /// will not result in the user being locked out of their account.
    /// </summary>
    public JustEnsureTotp EnsureTotp(UpdateMfaState state) => new(
        _client,
        state.Permit,
        state.OldMfa
    );

    /// <summary>
    /// Resumes the MFA update from the final update confirmation state.
    /// </summary>
    public FinalizeUpdate FinalizeUpdate(UpdateMfaState state) => new(
        _client,
        state.Permit,
        state.OldMfa
    );
}

[Result]
internal partial record StartUpdateRes
(
    [property: Error, JsonProperty("invalid_mfa", NullValueHandling = NullValueHandling.Ignore)]
    string Error
);

/// <summary>
/// Error returned when the user did not have the MFA kind being used to authenticate
/// set up.
/// </summary>
public record MfaNotFound 
{
    /// <inheritdoc/>
    public override string ToString() => "The user did not have the requested MFA set up";
}

/// <summary>
/// Common structure implemented by all states in the MFA update flow.
/// This is an internal implementation detail and should not be implemented by users.
/// </summary>
public abstract record UpdateMfaStateBase : IState<UpdateMfaState>
{
    /// <summary>
    /// The client used for making flow-related API calls.
    /// Protected for use by derived state implementations only.
    /// </summary>
    protected readonly FlowClient _client;
    /// <summary>
    /// The current permit token for this flow.
    /// Protected for use by derived state implementations only.
    /// </summary>
    protected string? _permit { get; init; }
    /// <summary>
    /// The array of MFA kinds originally configured.
    /// Protected for use by derived state implementations only.
    /// </summary>
    protected readonly MfaKind[] _mfa;
    
    internal UpdateMfaStateBase(FlowClient client, string? permit, MfaKind[] mfa)
    {
        _client = client;
        _permit = permit;
        _mfa = mfa;
    }
    
    /// <summary>
    /// Returns the enum variant associated with the current state.
    /// </summary>
    public abstract UpdateMfaStateE StateType { get; }
    
    /// <inheritdoc />
    public UpdateMfaState GetState() => new(StateType, _permit!, _mfa);
    
    /// <inheritdoc />
    public string Serialize() => JsonConvert.SerializeObject(GetState());
}

internal record StartUpdateArgs([property: JsonProperty("kind")] MfaKind InitMfa);
internal record StartUpdateTlArgs([property: JsonProperty("start_update")] StartUpdateArgs Args);

/// <summary>
/// Represents the entry point for updating a user's MFA settings. From here, the user can choose 
/// which type of MFA (SMS, Email, or TOTP) they want to use for verification.
/// </summary>
public record StartUpdate : UpdateMfaStateBase
{
    internal StartUpdate(FlowClient client, string? permit, MfaKind[] mfa) 
        : base(client, permit, mfa) {}
    
    /// <inheritdoc/>
    public override UpdateMfaStateE StateType => UpdateMfaStateE.StartUpdate;

    /// <summary>
    /// Performs the unchecked MFA action by sending the request to the server
    /// </summary>
    /// <typeparam name="T">The type of response to create</typeparam>
    /// <param name="mfaKind">The type of MFA method being initiated</param>
    /// <param name="createResponse">Function to create the response from the permit</param>
    /// <returns>A task containing the created response</returns>
    /// <exception cref="Exceptions.RequestError">
    /// There was an unexpected error encountered when making the request
    /// </exception>
    private Task<T> PerformActionUnchecked<T>(
        MfaKind mfaKind,
        Func<string?, T> createResponse
    )
    {
        return _client
            .SendRequest<StartUpdateTlArgs, Infallible>(
                new StartUpdateTlArgs(new StartUpdateArgs(mfaKind)),
                _permit
            )
            .ContinueWith(task => {
                var res = Resolve.Get(task);
                return createResponse(res.Permit);
            });
    }

    /// <summary>
    /// Validates if the requested MFA method is available for the user
    /// </summary>
    /// <param name="mfaKind">The MFA method to validate</param>
    /// <returns>
    /// Success with None if the MFA method is available, or Failure with the current state if not
    /// </returns>
    private Result<None, StartUpdate> Guard(MfaKind mfaKind)
    {
        return Array.Exists(_mfa, kind => kind == mfaKind)
            ? None.AsOk<StartUpdate>()
            : Result<None, StartUpdate>.Failure(new(_client, _permit, _mfa));
    }

    /// <summary>
    /// Dispatches an MFA action after validating the requested method
    /// </summary>
    /// <typeparam name="T">The type of response to create</typeparam>
    /// <param name="mfaKind">The type of MFA method being initiated</param>
    /// <param name="createResponse">Function to create the response from the permit</param>
    /// <returns>
    /// A future result containing either the next state or the current state on failure
    /// </returns>
    private FutResult<T, StartUpdate> DispatchMfa<T>(
        MfaKind mfaKind,
        Func<string?, T> createResponse
    ) => Guard(mfaKind).MapFut(_none => PerformActionUnchecked(mfaKind, createResponse));

    /// <summary>
    /// Initiates SMS verification by sending an OTP to the user's registered phone number
    /// </summary>
    /// <returns>
    /// <list type="bullet"><item>
    /// <description>
    /// Success: SMS OTP has been initiated and the message has been sent. Returns the CheckOtp 
    /// state where the user must submit the received code.
    /// </description>
    /// </item><item>
    /// <description>
    /// Failure: SMS is not configured as an available MFA method for this user. Returns the 
    /// current state to allow trying a different method.
    /// </description>
    /// </item></list>
    /// </returns>
    /// <exception cref="Exceptions.RequestError">
    /// There was an unexpected error encountered when making the request
    /// </exception>
    public FutResult<CheckOtp, StartUpdate> Sms()
        => DispatchMfa(MfaKind.Sms, permit => new CheckOtp(_client, permit, _mfa));
    
    /// <summary>
    /// Initiates email verification by sending an OTP to the user's registered email address
    /// </summary>
    /// <returns>
    /// <list type="bullet"><item>
    /// <description>
    /// Success: Email OTP has been initiated and the message has been sent. Returns the CheckOtp
    /// state where the user must submit the received code.
    /// </description>
    /// </item><item>
    /// <description>
    /// Failure: Email is not configured as an available MFA method for this user. Returns the
    /// current state to allow trying a different method.
    /// </description>
    /// </item></list>
    /// </returns>
    /// <exception cref="Exceptions.RequestError">
    /// There was an unexpected error encountered when making the request
    /// </exception>
    public FutResult<CheckOtp, StartUpdate> Email()
        => DispatchMfa(MfaKind.Email, permit => new CheckOtp(_client, permit, _mfa));
    
    /// <summary>
    /// Initiates TOTP verification using an authenticator app
    /// </summary>
    /// <returns>
    /// <list type="bullet"><item>
    /// <description>
    /// Success: TOTP verification has been initiated. Returns the CheckTotp state where the user
    /// must submit the current authenticator code.
    /// </description>
    /// </item><item>
    /// <description>
    /// Failure: TOTP is not configured as an available MFA method for this user. Returns the
    /// current state to allow trying a different method.
    /// </description>
    /// </item></list>
    /// </returns>
    /// <exception cref="Exceptions.RequestError">
    /// There was an unexpected error encountered when making the request
    /// </exception>
    public FutResult<CheckTotp, StartUpdate> Totp()
        => DispatchMfa(MfaKind.Totp, permit => new CheckTotp(_client, permit, _mfa));
}

internal record CheckOtpArgs([property: JsonProperty("guess")] string Guess);
internal record CheckOtpTlArgs([property: JsonProperty("check_simple_mfa")] CheckOtpArgs Args);

[Result]
internal partial record CheckOtpRes(
    [property: Ok, JsonProperty("check_simple_mfa")] string OkMsg,
    [property: Error, JsonProperty("bad_check_simple")] string ErrMsg
);

/// <summary>
/// Represents the state where a user must verify their identity using a one-time password (OTP) 
/// sent via SMS or email before proceeding with MFA changes. This verification ensures that only
/// the legitimate account owner can modify MFA settings.
/// </summary>
/// <remarks>
/// The OTP verification process follows these steps:
/// <list type="number">
///   <item><description>
///     System sends OTP to user's registered SMS or email
///   </description></item>
///   <item><description>
///     User submits received code through Guess method
///   </description></item>
///   <item><description>
///     System validates the submitted code:
///     <list type="bullet">
///       <item><description>
///         Valid code: Transitions to Decide state for MFA modifications
///       </description></item>
///       <item><description>
///         Invalid code: Remains in CheckOtp state for retry
///       </description></item>
///     </list>
///   </description></item>
/// </list>
/// 
/// Security considerations:
/// <list type="bullet">
///   <item><description>
///     OTP codes have a limited validity window
///   </description></item>
///   <item><description>
///     Failed attempts are tracked to prevent brute force attacks
///   </description></item>
/// </list>
/// </remarks>
public record CheckOtp : UpdateMfaStateBase
{
    internal CheckOtp(FlowClient client, string? permit, MfaKind[] mfa)
        : base(client, permit, mfa) {}
    
    /// <inheritdoc />
    public override UpdateMfaStateE StateType => UpdateMfaStateE.CheckOtp;

    /// <summary>
    /// Verify the Email or SMS OTP dispatched in <see cref="StartUpdate"/> to continue the 
    /// flow.
    /// </summary>
    /// <param name="guess">What the user believes the current OTP is</param>
    /// <returns>
    /// <list type="bullet"><item>
    /// <description>
    /// Success: The OTP was correct and the user is able to continue the MFA update
    /// process.
    /// </description>
    /// </item><item>
    /// <description>
    /// Failure: The guess was incorrect, the state transitions back to itself, 
    /// allowing the user to try again.
    /// </description>
    /// </item></list>
    /// </returns>
    /// <exception cref="Exceptions.RequestError">
    /// There was an unexpected error encountered when making the request, see 
    /// <see cref="Exceptions.RequestErrorKind"/> for more information
    /// </exception> 
    public FutResult<Decide, CheckOtp> Guess(SimpleOtp guess)
    {
        return FutResult<Decide, CheckOtp>.From(
            _client
                .SendRequest<CheckOtpTlArgs, CheckOtpRes>(
                    new CheckOtpTlArgs(new CheckOtpArgs(guess.ToString())), 
                    _permit
                )
                .ContinueWith(task => {
                    var ret = Resolve.Get<Response<CheckOtpRes>>(task);
                    return ret
                        .UnwrapRet()
                        .ToResult()
                        .MapOrElse(
                            _errMsg => Result<Decide, CheckOtp>
                                .Failure(this with { _permit = ret.Permit }),
                            _okMsg  => Result<Decide, CheckOtp>
                                .Success(new Decide(_client, ret.Permit, _mfa))
                        );
                })
        );
    }
}

internal record CheckTotpTlArgs([property: JsonProperty("check_totp")] CheckOtpArgs Args);

[Result]
internal partial record CheckTotpRes(
    [property: Ok, JsonProperty("check_totp")] string OkMsg,
    [property: Error, JsonProperty("bad_check_totp")] string ErrMsg
);

/// <summary>
/// Represents the state where a user must verify their identity using their authenticator app (TOTP)
/// before proceeding with MFA changes.
/// </summary>
public record CheckTotp : UpdateMfaStateBase
{
    internal CheckTotp(FlowClient client, string? permit, MfaKind[] mfa)
        : base(client, permit, mfa) {}

    /// <inheritdoc />
    public override UpdateMfaStateE StateType => UpdateMfaStateE.CheckTotp;

    /// <summary>
    /// An additional measure to ensure the user is who they say they are, checking 
    /// the TOTP code from their authenticator app.
    /// </summary>
    /// <param name="guess">What the user believes the current TOTP code is</param>
    /// <returns>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// Success: The guess was correct and the user can continue with the MFA update process.
    /// </description>
    /// </item>
    /// <item>
    /// <description>Failure: The guess was incorrect, the user must try again</description>
    /// </item>
    /// </list>
    /// </returns>
    /// <exception cref="Exceptions.RequestError">
    /// There was an unexpected error encountered when making the request, 
    /// see <see cref="Exceptions.RequestErrorKind"/> for more information
    /// </exception> 
    public FutResult<Decide, CheckTotp> Guess(Totp guess)
    {
        return FutResult<Decide, CheckTotp>.From(
            _client
                .SendRequest<CheckTotpTlArgs, CheckTotpRes>(
                    new CheckTotpTlArgs(new CheckOtpArgs(guess.ToString())),
                    _permit
                )
                .ContinueWith(task => {
                    var ret = Resolve.Get<Response<CheckTotpRes>>(task);
                    return ret
                        .UnwrapRet()
                        .ToResult()
                        .MapOrElse(
                            _errMsg => Result<Decide, CheckTotp>
                                .Failure(this with { _permit = ret.Permit }),
                            _okMsg  => Result<Decide, CheckTotp>
                                .Success(new Decide(_client, ret.Permit, _mfa))
                        );
                })
        );
    }
}

/// <summary>
/// Represents the state where the user chooses what MFA changes they want to make after successful
/// verification. They can add / update new methods or remove existing ones.
/// </summary>
public record Decide : UpdateMfaStateBase
{
    internal Decide(FlowClient client, string? permit, MfaKind[] mfa)
        : base(client, permit, mfa) {}

    /// <inheritdoc />
    public override UpdateMfaStateE StateType => UpdateMfaStateE.Decide;

    /// <summary>
    /// Soft transition to MFA removal flow.
    /// </summary>
    public RemoveState Remove() => new(_client, _permit, _mfa);
    
    /// <summary>
    /// Soft transition to MFA update flow.
    /// </summary>
    public UpdateState Update() => new(_client, _permit, _mfa);
}

internal record DecideRemArgs(
    [property: JsonProperty("Remove")] MfaKind Kind
);

internal record DecideRemDArgs(
    [property: JsonProperty("kind")] DecideRemArgs Args
);

internal record DecideRemTlArgs(
    [property: JsonProperty("decide")] DecideRemDArgs Args
);


[Result]
internal partial record InitRemovalRes(
    [property: Error, JsonProperty("invalid_mfa")] string ErrMsg
);

/// <summary>
/// Represents the kinds of failures during MFA kind removal.
/// </summary>
public enum MfaRemoveErrorKind {
    /// <summary>
    /// The user attempted to remove their only MFA kind
    /// </summary>
    IsOnlyMfaKind,
    /// <summary>
    /// The user attempted to delete an MFA kind that they did not have set up.
    /// </summary>
    NotSetUp,
    /// <summary>
    /// The local understanding of the state was out of sync with the IdP, indicating
    /// tampering on your end.
    /// </summary>
    Upstream
}

/// <summary>
/// The error returned during MFA removal when:
/// <list type="bullet">
/// <item><description>The user would have no MFA kinds after removal</description></item>
/// <item><description>The user did not have the requested MFA kind set up</description></item>
/// </list>
/// </summary>
public class CannotRemoveMfa : Exception
{
    /// <summary>
    /// Create a new <see cref="CannotRemoveMfa"/> error.
    /// </summary>
    /// <param name="message">The message to associate with the error.</param>
    /// <param name="kind">The category of error.</param>
    internal CannotRemoveMfa(
        string message, 
        MfaRemoveErrorKind kind = MfaRemoveErrorKind.Upstream
    ) : base(message) 
    {
        ErrorKind = kind;
    }

    internal static Result<T, CannotRemoveMfa> NotSetUp<T>() =>
        Result<T, CannotRemoveMfa>.Failure(
            new CannotRemoveMfa(
                "User attempted to remove an MFA kind they did not have set up",
                MfaRemoveErrorKind.NotSetUp)
        );
    
    internal static Result<T, CannotRemoveMfa> IsOnlyMfaKind<T>() =>
        Result<T, CannotRemoveMfa>.Failure(
            new CannotRemoveMfa(
                "User attempted to remove their only MFA method",
                MfaRemoveErrorKind.IsOnlyMfaKind)
        );
    
    
    /// <summary>
    /// Represents the kinds of failures during MFA kind removal.
    /// </summary>
    public readonly MfaRemoveErrorKind ErrorKind;
}

/// <summary>
/// Represents the soft removal state for an MFA method.
/// </summary>
/// <remarks>
/// This state allows a user to remove an MFA method, provided that they have at least
/// one remaining MFA method configured. If a user attempts to remove their last MFA method,
/// the request will fail.
/// 
/// <b>Process Flow:</b>
/// <list type="number">
///   <item><description>Validate that the user has multiple MFA methods.</description></item>
///   <item><description>Send a removal request for the selected MFA method.</description></item>
///   <item><description>
///     Finalize the flow, ensuring the user's session was not revoked.
///   </description></item>
/// </list>
/// </remarks>
public record RemoveState 
{
    internal RemoveState(FlowClient client, string? permit, MfaKind[] mfa)
    {
        _permit = permit;
        _client = client;
        _mfa    = mfa;
    }
    
    private readonly string? _permit;
    private readonly FlowClient _client;
    private readonly MfaKind[] _mfa;

    private bool Has(MfaKind mfaKind) => Array.Exists(_mfa, kind => kind == mfaKind);

    private FutResult<FinalizeRemoval, CannotRemoveMfa> Remove(MfaKind toRemove)
    {
        Debug.Assert(
            Has(toRemove),
            "Internal `Remove` function precondition violated. `MfaKind` to remove must be set up."
        );
        Debug.Assert(
            _mfa.Length > 1,
            "Internal `Remove` function precondition violated. The user may not remove their only MFA kind"
        );
        return FutResult<FinalizeRemoval, CannotRemoveMfa>.From(
            _client
                .SendRequest<DecideRemTlArgs, InitRemovalRes>(
                    new DecideRemTlArgs(new DecideRemDArgs(new DecideRemArgs(toRemove))),
                    _permit
                )
                .ContinueWith(task => {
                    var ret = Resolve.Get<Response<InitRemovalRes>>(task);
                    
                    if (ret.Ret is InitRemovalRes res) {
                        // init removal returns if there was an error, which, should be guarded
                        // against. The IdP ensures the same things as our runtime checks. At this
                        // point, as modifications to the user during our flow are checked by the
                        // IdP in finalize (not here), the only possible reason for an error is that
                        // the state was modified.
                        return Result<FinalizeRemoval, CannotRemoveMfa>.Failure(
                            new CannotRemoveMfa("MFA State Tampering in Removal")
                        );
                    }
                    else {
                        return Result<FinalizeRemoval, CannotRemoveMfa>
                            .Success(new FinalizeRemoval(_client, ret.Permit, _mfa));
                    }
                })
        );
    }

    private Result<MfaKind, CannotRemoveMfa> CheckMfaKind(MfaKind toRemove)
    {
        if (!Has(toRemove)) {
            return CannotRemoveMfa.NotSetUp<MfaKind>();
        }
        else if (_mfa.Length <= 1) {
            return CannotRemoveMfa.IsOnlyMfaKind<MfaKind>();
        }
        else {
            return Result<MfaKind, CannotRemoveMfa>.Success(toRemove);
        }
    }

    /// <summary>
    /// Initiates the removal of the email MFA method.
    /// </summary>
    /// <remarks>
    /// <b>Fails if:</b>
    /// <list type="bullet">
    ///   <item><description>The email MFA method is not set up.</description></item>
    ///   <item><description>Email is the only configured MFA method.</description></item>
    ///   <item><description>The <c>OldMfa</c> state field was modified.</description></item>
    /// </list>
    /// </remarks>
    public FutResult<FinalizeRemoval, CannotRemoveMfa> Email()
        => CheckMfaKind(MfaKind.Email).BindFut(Remove);
    
    /// <summary>
    /// Initiates the removal of the SMS MFA method.
    /// </summary>
    /// <remarks>
    /// <b>Fails if:</b>
    /// <list type="bullet">
    ///   <item><description>The SMS MFA method is not set up.</description></item>
    ///   <item><description>SMS is the only configured MFA method.</description></item>
    ///   <item><description>The <c>OldMfa</c> state field was modified.</description></item>
    /// </list>
    /// </remarks>
    public FutResult<FinalizeRemoval, CannotRemoveMfa> Sms()
        => CheckMfaKind(MfaKind.Sms).BindFut(Remove);

    /// <summary>
    /// Initiates the removal of the TOTP MFA method.
    /// </summary>
    /// <remarks>
    /// <b>Fails if:</b>
    /// <list type="bullet">
    ///   <item><description>The TOTP MFA method is not set up.</description></item>
    ///   <item><description>TOTP is the only configured MFA method.</description></item>
    ///   <item><description>The <c>OldMfa</c> state field was modified.</description></item>
    /// </list>
    /// </remarks>
    public FutResult<FinalizeRemoval, CannotRemoveMfa> Totp()
        => CheckMfaKind(MfaKind.Totp).BindFut(Remove);
}

internal record FinalizeArgs(
    [property: JsonProperty("token")] Token Token
);
internal record FinalizeRemovalTlArgs(
    [property: JsonProperty("finalize_removal")] FinalizeArgs Args
);

internal record FinalizeRemovalRes(
    [property: JsonProperty("finalize_removal")] 
    Token Token
);

/// <summary>
/// Represents the final confirmation state when removing an MFA method.
/// </summary>
/// <remarks>
/// This state is reached after the user has initiated removal of an MFA method and the system
/// has verified that removing this method will not compromise account security.
/// 
/// <para>Security checks performed in this state:</para>
/// <list type="bullet">
///   <item><description>
///     Verifies the user's session is still valid via token check
///   </description></item>
///   <item><description>
///     Confirms user has at least one other MFA method remaining
///   </description></item>
///   <item><description>
///     Ensures atomic removal to maintain security invariants
///   </description></item>
///   <item><description>
///     Ensures that the original MFA kinds were not modified during the flow
///   </description></item>
/// </list>
/// </remarks>
public record FinalizeRemoval : UpdateMfaStateBase
{
    internal FinalizeRemoval(FlowClient client, string? permit, MfaKind[] mfa)
        : base(client, permit, mfa) {}

    /// <inheritdoc />
    public override UpdateMfaStateE StateType => UpdateMfaStateE.FinalizeRemoval;

    /// <summary>
    /// Commits the removal of the previously selected MFA method.
    /// </summary>
    /// <param name="token">The user's current authentication token</param>
    /// <returns>
    /// A task that resolves to a new authentication token that must be used for 
    /// subsequent requests.
    /// </returns>
    /// <remarks>
    /// <para>Important considerations when calling this method:</para>
    /// <list type="bullet">
    ///   <item><description>
    ///     The returned token must replace the previous token for all future requests
    ///   </description></item>
    ///   <item><description>
    ///     Failing to use the new token will effectively log the user out
    ///   </description></item>
    ///   <item><description>
    ///     The removal operation is permanent and cannot be undone
    ///   </description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="Exceptions.RequestError">
    /// Thrown when:
    /// <list type="bullet">
    ///   <item><description>There is an error communicating with the server</description></item>
    ///   <item><description>The token has been revoked</description></item>
    ///   <item><description>The user's session is no longer valid</description></item>
    /// </list>
    /// </exception>
    public Task<Token> Finalize(Token token) 
    {
        return _client
            .SendRequest<FinalizeRemovalTlArgs, FinalizeRemovalRes>(
                new FinalizeRemovalTlArgs(new FinalizeArgs(token)),
                _permit,
                new TokenDeserializer()
            )
            .ContinueWith(task => {
                var ret = Resolve.Get<Response<FinalizeRemovalRes>>(task);
                return ret
                    .UnwrapRet()
                    .Token;
            });
    }
}

internal abstract class MfaUpdateKind
{
    internal sealed class Totp : MfaUpdateKind
    {
        internal Totp(string displayName) 
        {
            DisplayName = displayName;
        }
        internal string DisplayName { get; }
    }
    internal sealed class Sms : MfaUpdateKind
    {
        internal Sms(string phoneNumber)
        {
            PhoneNumber = phoneNumber;
        }
        internal string PhoneNumber { get; }
    }
    internal sealed class Email : MfaUpdateKind
    {
        internal Email(string emailAddress)
        {
            EmailAddress = emailAddress;
        }
        internal string EmailAddress { get; }
    }
}

internal class DecideUpdateSer : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return typeof(MfaUpdateKind).IsAssignableFrom(objectType);
    }

    private void WriteNonNull(JsonWriter writer, Action<JsonWriter> write)
    {
        writer.WriteStartObject();
        write(writer);
        writer.WriteEndObject();
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is MfaUpdateKind mfaUpdateKind)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("decide");
            writer.WriteStartObject();
            writer.WritePropertyName("kind");
            writer.WriteStartObject();
            writer.WritePropertyName("Update");
            
            switch (mfaUpdateKind)
            {
                case MfaUpdateKind.Totp totp:
                    WriteNonNull(writer, w =>
                    {
                        w.WritePropertyName("Totp"); 
                        w.WriteValue(totp.DisplayName);
                    });
                    break;
                case MfaUpdateKind.Sms sms:
                    WriteNonNull(writer, w =>
                    {
                        w.WritePropertyName("Sms"); 
                        w.WriteValue(sms.PhoneNumber);
                    });
                    break;
                case MfaUpdateKind.Email email:
                    WriteNonNull(writer, w =>
                    {
                        w.WritePropertyName("Email"); 
                        w.WriteValue(email.EmailAddress);
                    });
                    break;
                default:
                    throw new JsonSerializationException("Unreachable path entered");
            };
            
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        else
        {
            throw new JsonSerializationException("Expected MfaUpdateKind type");
        }
    }

    public override object ReadJson(
        JsonReader reader, 
        Type objectType, 
        object? existingValue, 
        JsonSerializer serializer
    )
    {
        throw new NotImplementedException("Deserialization is not supported for SetupMfaKind.");
    }
    
    public override bool CanRead => false;
    public override bool CanWrite => true; 
}

internal record TotpUriRes(
    [property: JsonProperty("totp_uri")] string TotpUri
);

/// <summary>
/// Soft MFA update state
/// </summary>
public record UpdateState 
{
    internal UpdateState(FlowClient client, string? permit, MfaKind[] mfa)
    {
        _permit = permit;
        _client = client;
        _mfa    = mfa;
    }

    private readonly string? _permit;
    private readonly FlowClient _client;
    private readonly MfaKind[] _mfa;
    
    private Task<EnsureOtp> SimpleAction(MfaUpdateKind kind)
    {
        return _client
            .SendRequest<MfaUpdateKind, None>(
                kind,
                _permit,
                new DecideUpdateSer()
            )
            .ContinueWith(task => {
                var ret = Resolve.Get<Response<None>>(task);
                return new EnsureOtp(_client, ret.Permit, _mfa);
            });
    }
    
    /// <summary>
    /// Initiates the update / creation of the SMS MFA method.
    /// </summary>
    public Task<EnsureOtp> Sms(string phoneNumber) => SimpleAction(
        new MfaUpdateKind.Sms(phoneNumber) 
    );
    
    /// <summary>
    /// Initiates the update / creation of the Email MFA method.
    /// </summary>
    public Task<EnsureOtp> Email(string email) => SimpleAction(
        new MfaUpdateKind.Email(email)
    );

    /// <summary>
    /// Initiates the update / creation of the TOTP MFA method.
    /// </summary>
    public Task<EnsureTotp> Totp(string displayName)
    {
        return _client
            .SendRequest<MfaUpdateKind, TotpUriRes>(
                new MfaUpdateKind.Totp(displayName),
                _permit,
                new DecideUpdateSer()
            )
            .ContinueWith(task => {
                var ret = Resolve.Get<Response<TotpUriRes>>(task);
                return new EnsureTotp(_client, ret.Permit, _mfa, ret.UnwrapRet().TotpUri);
            });
    }
}

internal record EnsureOtpTlArgs([property: JsonProperty("ensure_simple_mfa")] CheckOtpArgs Args);

[Result]
internal partial record EnsureOtpRes(
    [property: Ok, JsonProperty("ensure_simple_mfa")] string OkMsg,
    [property: Error, JsonProperty("bad_ensure_simple")] string ErrMsg
);

/// <summary>
/// Represents the state where a user is setting up SMS or email OTP as a new MFA method,
/// or updating an existing method.
/// </summary>
/// <remarks>
/// This state is entered after initiating setup of a new email or SMS-based MFA method.
/// The user must verify they have access to the contact method by entering a one-time code
/// that was sent to them.
/// 
/// <para>The verification process:</para>
/// <list type="number">
///   <item><description>
///     System sends a one-time code to the user's phone number or email
///   </description></item>
///   <item><description>
///     User receives and enters the code via the <see cref="Guess"/> method
///   </description></item>
///   <item><description>
///     If correct, advances to <see cref="FinalizeUpdate"/> state
///   </description></item>
///   <item><description>
///     If incorrect, remains in current state for retry
///   </description></item>
/// </list>
/// 
/// <para>Security considerations:</para>
/// <list type="bullet">
///   <item><description>
///     Codes have a limited validity window
///   </description></item>
///   <item><description>
///     Failed attempts are rate-limited
///   </description></item>
///   <item><description>
///     Verification must succeed before any changes are committed
///   </description></item>
/// </list>
/// </remarks>
public record EnsureOtp : UpdateMfaStateBase
{
    internal EnsureOtp(FlowClient client, string? permit, MfaKind[] mfa)
        : base(client, permit, mfa) {}
    
    /// <inheritdoc />
    public override UpdateMfaStateE StateType => UpdateMfaStateE.EnsureOtpSetup;

    /// <summary>
    /// Attempts to verify the user's access to the new MFA contact method by validating 
    /// a one-time code.
    /// </summary>
    /// <param name="guess">The one-time code entered by the user</param>
    /// <returns>
    /// A result that represents either:
    /// <list type="bullet">
    ///   <item><description>
    ///     Success: Transitions to <see cref="FinalizeUpdate"/> state to complete the MFA setup
    ///   </description></item>
    ///   <item><description>
    ///     Failure: Remains in current state with updated permit for retry
    ///   </description></item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// A successful verification confirms that:
    /// <list type="bullet">
    ///   <item><description>
    ///     The user has access to the contact method
    ///   </description></item>
    ///   <item><description>
    ///     The contact method is valid and can receive codes
    ///   </description></item>
    ///   <item><description>
    ///     The user can successfully retrieve and enter codes
    ///   </description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="Exceptions.RequestError">
    /// Thrown when there is an error communicating with the server
    /// </exception>
    public FutResult<FinalizeUpdate, EnsureOtp> Guess(SimpleOtp guess)
    {
        return FutResult<FinalizeUpdate, EnsureOtp>.From(
            _client
                .SendRequest<EnsureOtpTlArgs, EnsureOtpRes>(
                    new EnsureOtpTlArgs(new CheckOtpArgs(guess.ToString())), 
                    _permit
                )
                .ContinueWith(task => {
                    var ret = Resolve.Get<Response<EnsureOtpRes>>(task);
                    return ret
                        .UnwrapRet()
                        .ToResult()
                        .MapOrElse(
                            _errMsg => Result<FinalizeUpdate, EnsureOtp>
                                .Failure(this with { _permit = ret.Permit }),
                            _okMsg  => Result<FinalizeUpdate, EnsureOtp>
                                .Success(new FinalizeUpdate(_client, ret.Permit, _mfa))
                        );
                })
        );
    }
}

internal record EnsureTotpTlArgs([property: JsonProperty("ensure_totp")] CheckOtpArgs Args);

[Result]
internal partial record EnsureTotpRes(
    [property: Ok, JsonProperty("ensure_totp")] string OkMsg,
    [property: Error, JsonProperty("bad_ensure_totp")] string ErrMsg
);

/// <summary>
/// Base abstract record for generically interfacing with TOTP verification states
/// across both initial attempts (where the TOTP Provisioning URI is provided) and 
/// retries, where this provisioning URI is no longer tracked.
/// </summary>
public abstract record JustEnsureTotpBase<TI> : UpdateMfaStateBase
    where TI : JustEnsureTotpBase<TI>
{
    internal JustEnsureTotpBase(FlowClient client, string? permit, MfaKind[] mfa) 
        : base(client, permit, mfa) 
        {}
    
    /// <inheritdoc />
    public override UpdateMfaStateE StateType => UpdateMfaStateE.EnsureTotpSetup;

    /// <summary>
    /// Verify that the new TOTP secret is properly setup for the user by having them submit the code
    /// from their authenticator app
    /// </summary>
    /// <param name="guess">What the user believes the current TOTP code is</param>
    /// <returns>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// Success: The guess was correct and TOTP now the user can finalize the flow, updating their
    /// MFA kinds with the new TOTP secret.
    /// </description>
    /// </item>
    /// <item>
    /// <description>Failure: The guess was incorrect, the user must try again</description>
    /// </item>
    /// </list>
    /// </returns>
    /// <exception cref="Exceptions.RequestError">
    /// There was an unexpected error encountered when making the request, 
    /// see <see cref="Exceptions.RequestErrorKind"/> for more information
    /// </exception> 
    public FutResult<FinalizeUpdate, TI> Guess(Totp guess)
    {
        return FutResult<FinalizeUpdate, TI>.From(
            _client
                .SendRequest<EnsureTotpTlArgs, EnsureTotpRes>(
                    new EnsureTotpTlArgs(new CheckOtpArgs(guess.ToString())), 
                    _permit
                )
                .ContinueWith(task => {
                    var ret = Resolve.Get<Response<EnsureTotpRes>>(task);
                    return ret
                        .UnwrapRet()
                        .ToResult()
                        .MapOrElse(
                            _errMsg => Result<FinalizeUpdate, TI>
                                .Failure((TI)this with { _permit = ret.Permit }),
                            _okMsg  => Result<FinalizeUpdate, TI>
                                .Success(new FinalizeUpdate(_client, ret.Permit, _mfa))
                        );
                })
        );
    }
}

/// <summary>
/// Represents the state where a user is setting up TOTP as a new MFA method, or wanting
/// to rotate their authenticator app's existing client secret.
/// 
/// The user must configure their authenticator app and verify they can generate valid codes.
/// </summary>
/// <remarks>
/// This state includes the <c>ProvisioningUri</c>, which should be rendered as a QR code for
/// the user for loading into their authenticator app.
/// </remarks>
public record EnsureTotp : JustEnsureTotpBase<EnsureTotp>
{
    internal EnsureTotp(FlowClient client, string? permit, MfaKind[] mfa, string provisioningUri)
        : base(client, permit, mfa) 
    { 
        ProvisioningUri = provisioningUri;
    }

    /// <summary>
    /// The provisioning URI for the user's authenticator app. One should display this in their UI as 
    /// a QR code. It should always be transmitted via a secure channel, but that is a given when 
    /// interacting with this SDK.
    /// </summary>
    public string ProvisioningUri { get; }
}

/// <summary>
/// Represents the state where a user is setting up TOTP as a new MFA method, or wanting
/// to rotate their authenticator app's existing client secret.
/// 
/// The user must configure their authenticator app and verify they can generate valid codes.
/// </summary>
public record JustEnsureTotp : JustEnsureTotpBase<JustEnsureTotp>
{
    internal JustEnsureTotp(FlowClient client, string? permit, MfaKind[] mfa)
        : base(client, permit, mfa) 
        { }
}

internal record FinalizeUpdateTlArgs(
    [property: JsonProperty("finalize_update")] FinalizeArgs Args
);


/// <summary>
/// Represents the result of finalizing an MFA update, containing both a new authentication token
/// and information about the updated MFA method.
/// </summary>
/// <remarks>
/// This type hierarchy provides specific implementations for each MFA method:
/// <list type="bullet">
///   <item><description>TOTP - Contains only the new authentication token</description></item>
///   <item><description>Email - Contains the token and new email address</description></item>
///   <item><description>SMS - Contains the token and new phone number</description></item>
/// </list>
/// 
/// The token must be preserved to maintain the user's authentication state unless
/// logging them out is the desired behavior.
/// </remarks>
[MustUse("You must not ignore the returned `Token` unless the user being logged out is desired behavior")]
public abstract record TokenAndNewMfa
{
    private TokenAndNewMfa(Token token)
    {
        Token = token;
    }

    /// <summary>
    /// Gets the new authentication token that should be used for subsequent requests.
    /// </summary>
    /// <remarks>
    /// This token must be stored and used to replace the previous authentication token
    /// to maintain the user's session.
    /// </remarks>
    public Token Token { get; }


    /// <summary>
    /// Represents a successful TOTP MFA update, containing only the new authentication token.
    /// </summary>
    /// <remarks>
    /// Since TOTP is based on a shared secret that was already configured during the setup phase,
    /// no additional configuration information needs to be stored client-side.
    /// </remarks>
    public sealed record Totp : TokenAndNewMfa
    {
        internal Totp(Token token)
            : base(token)
            { }
    }

    /// <summary>
    /// Represents a successful email-based MFA update, containing both the new authentication
    /// token and the configured email address.
    /// </summary>
    /// <remarks>
    /// The email address is provided to allow the client to display or store the configured
    /// MFA destination for user reference.
    /// </remarks>
    public sealed record Email : TokenAndNewMfa
    {
        internal Email(Token token, string email)
            : base(token)
        {
            EmailAddress = email;
        }
        
        /// <summary>
        /// Gets the email address that was configured for MFA.
        /// </summary>
        public string EmailAddress { get; }
    }

    /// <summary>
    /// Represents a successful SMS-based MFA update, containing both the new authentication
    /// token and the configured phone number.
    /// </summary>
    /// <remarks>
    /// The phone number is provided to allow the client to display or store the configured
    /// MFA destination for user reference.
    /// </remarks>
    public sealed record Sms : TokenAndNewMfa
    {
        internal Sms(Token token, string phoneNumber)
            : base(token)
        {
            PhoneNumber = phoneNumber;
        }
        
        /// <summary>
        /// Gets the phone number that was configured for MFA.
        /// </summary>
        public string PhoneNumber { get; }
    }
}

[JsonConverter(typeof(MfaTypeConverter))]
internal abstract class MfaType
{
    internal sealed class Email : MfaType
    {
        public required string EmailAddress { get; set; }
    }

    internal sealed class Sms : MfaType
    {
        public required string PhoneNumber { get; set; }
    }

    internal sealed class Totp : MfaType
    {
    }
}

internal class MfaTypeConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(MfaType);
    }

    public override object ReadJson(
        JsonReader reader, 
        Type objectType, 
        object? existingValue, 
        JsonSerializer serializer
    )
    {
        JToken token = JToken.Load(reader);

        // Handle the case where mfa is a string (Totp)
        if (token.Type == JTokenType.String)
        {
            if (token.Value<string>() is string mfaType && mfaType == "Totp")
            {
                return new MfaType.Totp();
            }
            throw new JsonSerializationException($"Unknown MFA type: {token.Value<string>()}");
        }

        // Handle the case where mfa is an object with Email or PhoneNumber
        if (token.Type == JTokenType.Object)
        {
            var obj = (JObject)token;
            
            if (obj["Email"] is { } eSlot && eSlot.Value<string>() is string email)
            {
                return new MfaType.Email 
                { 
                    EmailAddress = email
                };
            }
            
            if (obj["Sms"] is { } pSlot && pSlot.Value<string>() is string sms)
            {
                return new MfaType.Sms 
                { 
                    PhoneNumber = sms
                };
            }
        }

        throw new JsonSerializationException("Invalid MFA format");
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        switch (value)
        {
            case MfaType.Totp:
                writer.WriteValue("Totp");
                break;
            case MfaType.Email emailMfa:
                writer.WriteStartObject();
                writer.WritePropertyName("Email");
                writer.WriteValue(emailMfa.EmailAddress);
                writer.WriteEndObject();
                break;
            case MfaType.Sms smsMfa:
                writer.WriteStartObject();
                writer.WritePropertyName("Sms");
                writer.WriteValue(smsMfa.PhoneNumber);
                writer.WriteEndObject();
                break;
            default:
                throw new JsonSerializationException($"Unknown MFA type: {value?.GetType()}");
        }
    }
}

internal record FinalizeUpdateRes(
    [property: JsonProperty("token")] Token Token,
    [property: JsonProperty("mfa")] MfaType Mfa
);
internal record FinalizeUpdateTlRes(
    [property: JsonProperty("finalize_update")] FinalizeUpdateRes Res
);

/// <summary>
/// Represents the final confirmation state when updating MFA settings. The user must review
/// and confirm their new MFA configuration before changes are applied.
/// </summary>
/// <remarks>
/// This state is reached after successfully setting up and verifying a new MFA method. It serves as 
/// the final checkpoint before committing changes to the user's MFA configuration.
/// 
/// <para>Security considerations:</para>
/// <list type="bullet">
///   <item><description>
///     The user's current authentication token must be provided to prevent session hijacking
///   </description></item>
///   <item><description>
///     Changes are only committed if the token is valid and has not been revoked
///   </description></item>
///   <item><description>
///     All updates are atomic and maintain security invariants
///   </description></item>
/// </list>
/// </remarks>
public record FinalizeUpdate : UpdateMfaStateBase
{
    internal FinalizeUpdate(FlowClient client, string? permit, MfaKind[] mfa)
        : base(client, permit, mfa) {}

    /// <inheritdoc />
    public override UpdateMfaStateE StateType => UpdateMfaStateE.FinalizeUpdate;

    /// <summary>
    /// Commits the MFA configuration changes and returns the updated authentication context.
    /// </summary>
    /// <param name="token">The user's current authentication token</param>
    /// <returns>
    /// A task that resolves to a <see cref="TokenAndNewMfa"/> containing:
    /// <list type="bullet">
    ///   <item><description>
    ///     A new authentication token that must be used for subsequent requests
    ///   </description></item>
    ///   <item><description>
    ///     The finalized MFA configuration details (phone number for SMS, email address for email, 
    ///     or just token for TOTP)
    ///   </description></item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// <para>The returned token must be stored and used to replace the previous authentication token
    /// to maintain the user's session. Failing to use the new token will effectively log the user
    /// out.</para>
    /// 
    /// <para>The specific type of <see cref="TokenAndNewMfa"/> returned depends on the MFA method
    /// being finalized:</para>
    /// <list type="bullet">
    ///   <item><description>
    ///     SMS - Returns <see cref="TokenAndNewMfa.Sms"/> with the configured phone number
    ///   </description></item>
    ///   <item><description>
    ///     Email - Returns <see cref="TokenAndNewMfa.Email"/> with the configured email address
    ///   </description></item>
    ///   <item><description>
    ///     TOTP - Returns <see cref="TokenAndNewMfa.Totp"/> with just the new token
    ///   </description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="Exceptions.RequestError">
    /// Thrown when there is an error communicating with the server or if the token has been revoked
    /// </exception>
    public Task<TokenAndNewMfa> Finalize(Token token) 
    {
        return _client
            .SendRequest<FinalizeUpdateTlArgs, FinalizeUpdateTlRes>(
                new FinalizeUpdateTlArgs(new FinalizeArgs(token)),
                _permit,
                new TokenDeserializer()
            )
            .ContinueWith<TokenAndNewMfa>(task => {
                var ret = Resolve.Get<Response<FinalizeUpdateTlRes>>(task);
                var res = ret.UnwrapRet().Res;
                
                return res.Mfa switch {
                    MfaType.Sms sms     => new TokenAndNewMfa.Sms(res.Token, sms.PhoneNumber),
                    MfaType.Email email => new TokenAndNewMfa.Email(res.Token, email.EmailAddress),
                    MfaType.Totp        => new TokenAndNewMfa.Totp(res.Token),
                    _ => throw new Exception("Unreachable path entered")
                };
            });
    }
}
