using IronVelo.Types;
using IronVelo.Utils;
using MustUse;
using Newtonsoft.Json;
using ResultAble;

namespace IronVelo.Flows.Delete;

internal record AskDeleteArgs(
    [property: JsonProperty("token")] string Token,
    [property: JsonProperty("username")] string Username
);

internal record AskDeleteTlArgs([property: JsonProperty("ask_delete")] AskDeleteArgs Args);

/// <summary>
/// The user failed one of the challenges / confirmations in the deletion flow. A new <see cref="Token"/> is returned
/// to prevent the user from being logged out as well as the reason for the failure.
/// </summary>
/// <param name="NewToken">The new token, representing the user being logged in.</param>
/// <param name="Reason">The reason for the error.</param>
/// <typeparam name="TK">The kind of error.</typeparam>
[MustUse("You must not ignore the NewToken property, as otherwise the user would be logged out.")]
public record DeleteError<TK>(
    Token NewToken,
    TK Reason
);

/// <summary>
/// The provided username did not match the username associated with the provided <see cref="Token"/>.
/// </summary>
public record IncorrectUsername
{
    internal static Result<T, DeleteError<IncorrectUsername>> AsErr<T>(Token token)
    {
        return Result<T, DeleteError<IncorrectUsername>>.Failure(new DeleteError<IncorrectUsername>(
            token,
            new IncorrectUsername()
        ));
    }
}

[Result]
internal partial record AskDeleteRes(
    [property: Error, JsonProperty("invalid_username", NullValueHandling = NullValueHandling.Ignore)]
    string FailToken,
    [property: Ok, JsonProperty("ask_delete", NullValueHandling = NullValueHandling.Ignore)]
    string SuccessToken
);

/// <summary>
/// The ingress state for scheduling the deletion of a user's account. The main method for this state is
/// <see cref="Delete"/>.
/// </summary>
public class AskDelete
{
    internal AskDelete(FlowClient client) { _client = client; }
    private readonly FlowClient _client;

    /// <summary>
    /// Request for the scheduled deletion of a user's account.
    /// </summary>
    /// <param name="token">The token associated with the user's login state</param>
    /// <param name="username">The username associated with the account being deleted</param>
    /// <returns>
    /// <list type="bullet">
    /// <item>
    ///     <term>Success:</term>
    ///     <description>
    ///         Both the token was valid and not revoked, and the username matched that which is associated with the
    ///         token. To continue with the deletion process, the user must now provide the password associated with
    ///         their account.
    ///     </description>
    /// </item>
    /// <item>
    ///     <term>Failure:</term>
    ///     <description>
    ///     The username did not match that which was associated with the token. This does not log the user out, but
    ///     does rotate the token. The new token is stored in the <see cref="DeleteError{TK}.NewToken"/> property.
    ///     </description>
    /// </item>
    /// </list>
    /// </returns>
    /// <exception cref="Exceptions.RequestError">
    /// The token was either expired or revoked, or there was a network error.
    /// </exception>
    public FutResult<ConfirmPassword, DeleteError<IncorrectUsername>> Delete(Token token, string username)
    {
        return FutResult<ConfirmPassword, DeleteError<IncorrectUsername>>
            .From(_client.SendRequest<AskDeleteTlArgs, AskDeleteRes>(
                new AskDeleteTlArgs(new AskDeleteArgs(token.Encode(), username))
            ).ContinueWith(task => {
                var res = Resolve.Get(task);
                return res.UnwrapRet().ToResult().MapOrElse(
                    failToken => IncorrectUsername.AsErr<ConfirmPassword>(new Token(failToken)),
                    successToken => Result<ConfirmPassword, DeleteError<IncorrectUsername>>.Success(new ConfirmPassword(
                        _client, res.Permit, successToken
                    ))
                );
            }));
    }

    /// <summary>
    /// For multipart flows, resume a state from the serializable representation.
    /// </summary>
    public ResumeDeleteState Resume() => new(_client);
}

/// <summary>
/// Resume the account deletion flow from a <see cref="DeleteState"/>, used in multistep flows to avoid the need for
/// tracking state yourself.
/// </summary>
/// <remarks>
/// <b>How do I know which method to invoke?:</b><br/>
/// In order to properly use <see cref="AskDelete.Resume"/> you should have invoked <see cref="IState{TF}.Serialize"/>
/// in order to provide the state to the client. When the client continues the flow, they should return this serialized
/// representation of the state to your server.
/// <br/><br/>
/// The serialized states all include a <c>State</c> property, which indicates which method to call.
/// <br/><br/>
/// <b>Mapping:</b>
/// <list type="bullet">
/// <item>
///     <description><c>State.ConfirmPassword => ResumeDeleteState.ConfirmPassword(state)</c></description>
/// </item>
/// <item>
///     <description><c>State.ConfirmDeletion => ResumeDeleteState.ConfirmDeletion(state)</c></description>
/// </item>
/// </list>
/// <para>
/// <b>Security:</b><br/>
/// There are no security concerns here, but if you would like to catch errors / tampering early, you can sign the
/// serialized representation using <c>HMAC</c>. To clarify, this is not necessary, the IdP will detect any tampering
/// itself.
/// </para>
/// </remarks>
public record ResumeDeleteState
{
    internal ResumeDeleteState(FlowClient client) { _client = client; }
    private readonly FlowClient _client;
    
    /// <summary>
    /// Resumes the account deletion flow from the confirm password state.
    /// </summary>
    /// <param name="state">The current state of the account deletion flow.</param>
    /// <returns>A new <see cref="ConfirmPassword"/> instance to handle the password confirmation step.</returns>
    public ConfirmPassword ConfirmPassword(DeleteState state) => new(_client, state.Permit, state.Token);

    /// <summary>
    /// Resumes the account deletion flow from the confirm deletion state.
    /// </summary>
    /// <param name="state">The current state of the account deletion flow.</param>
    /// <returns>A new <see cref="ConfirmDeletion"/> instance to handle the deletion confirmation step.</returns>
    public ConfirmDeletion ConfirmDeletion(DeleteState state) => new(_client, state.Permit, state.Token);
}

internal record CheckPasswordArgs(
    [property: JsonProperty("token")] string Token,   
    [property: JsonProperty("password")] string Password
);

internal record CheckPasswordTlArgs([property: JsonProperty("password")] CheckPasswordArgs Args);

/// <summary>
/// The password was incorrect, the user must restart the flow if they want to continue with the deletion process.
/// </summary>
public record IncorrectPassword 
{
    internal static Result<T, DeleteError<IncorrectPassword>> AsErr<T>(Token token)
    {
        return Result<T, DeleteError<IncorrectPassword>>.Failure(new DeleteError<IncorrectPassword>(
            token,
            new IncorrectPassword()
        ));
    }
}

[Result]
internal partial record CheckPasswordRes(
    [property: Error, JsonProperty("invalid_password", NullValueHandling = NullValueHandling.Ignore)]
    string FailToken,
    [property: Ok, JsonProperty("password", NullValueHandling = NullValueHandling.Ignore)]
    string SuccessToken
);

/// <summary>
/// The password confirmation state in the account deletion flow, with the main method being
/// <see cref="ConfirmPassword.CheckPassword"/>.
/// </summary>
public class ConfirmPassword : IState<DeleteState>
{
    internal ConfirmPassword(FlowClient client, string? permit, string token)
    {
        _client = client;
        _permit = permit;
        _token = token;
    }
    private readonly FlowClient _client;
    private readonly string? _permit;
    private readonly string _token;

    /// <inheritdoc />
    public DeleteState GetState() => new(DeleteStateE.ConfirmPassword, _permit!, _token);
    /// <inheritdoc />
    public string Serialize() => JsonConvert.SerializeObject(GetState());

    /// <summary>
    /// After successfully requesting scheduled account deletion, the user must provide their password as an extra
    /// security measure.
    /// </summary>
    /// <param name="password">The claimed password.</param>
    /// <returns>
    /// <list type="bullet">
    /// <item>
    ///     <term>Success:</term>
    ///     <description>
    ///         The password was correct and the <see cref="ConfirmDeletion"/> state is returned. The user now can
    ///         finalize the flow with a final confirmation scheduling their account for deletion.
    ///     </description>
    /// </item>
    /// <item>
    ///     <term>Failure:</term>
    ///     <description>
    ///     The password was incorrect, in the account deletion flow this is unacceptable. If the user wishes to proceed
    ///     with the scheduled deletion they must restart at <see cref="AskDelete"/>. If you don't want the user to
    ///     have to go through the process of restarting the flow you can track their username and return to this state
    ///     behind the scenes.
    ///     <br/><br/>
    ///     Like <see cref="AskDelete"/>, under the failure a new <see cref="Token"/> is returned, preventing the user
    ///     from being logged out.
    ///     </description>
    /// </item>
    /// </list>
    /// </returns>
    /// <exception cref="Exceptions.RequestError">A network error took place or the permit expired.</exception>
    public FutResult<ConfirmDeletion, DeleteError<IncorrectPassword>> CheckPassword(Password password)
    {
        return FutResult<ConfirmDeletion, DeleteError<IncorrectPassword>>
            .From(_client.SendRequest<CheckPasswordTlArgs, CheckPasswordRes>(
                new CheckPasswordTlArgs(new CheckPasswordArgs(_token, password.ToString())),
                _permit
            ).ContinueWith(task => {
                var res = Resolve.Get(task);
                return res.UnwrapRet().ToResult().MapOrElse(
                    failToken => IncorrectPassword.AsErr<ConfirmDeletion>(new Token(failToken)),
                    successToken => Result<ConfirmDeletion, DeleteError<IncorrectPassword>>.Success(new ConfirmDeletion(
                        _client, res.Permit, successToken
                    ))
                );
            }));
    }
}

internal record ConfirmDeletionArgs([property: JsonProperty("token")] string Token);
internal record ConfirmDeleteTlArgs([property: JsonProperty("confirm")] ConfirmDeletionArgs Args);

/// <summary>
/// Terminal state of the account deletion flow, scheduling deletion of the user's account. The main method of
/// <c>ConfirmDeletion</c> being <see cref="ConfirmDeletion.Confirm"/>.
/// </summary>
public class ConfirmDeletion : IState<DeleteState>
{
    internal ConfirmDeletion(FlowClient client, string? permit, string token)
    {
        _client = client;
        _permit = permit;
        _token = token;
    }
    private readonly FlowClient _client;
    private readonly string? _permit;
    private readonly string _token;

    /// <inheritdoc/>
    public DeleteState GetState() => new(DeleteStateE.ConfirmDeletion, _permit!, _token);
    /// <inheritdoc/>
    public string Serialize() => JsonConvert.SerializeObject(GetState());
    
    /// <summary>
    /// Confirm the deletion of the user's account, scheduling it for deletion. 
    /// </summary>
    /// <returns>
    /// Nothing relevant. The user is now logged out.
    /// </returns>
    /// <exception cref="Exceptions.RequestError">A network error took place or the permit expired.</exception>
    public Task<Response<None>> Confirm()
    {
        return _client.SendRequest<ConfirmDeleteTlArgs, None>(
            new ConfirmDeleteTlArgs(new ConfirmDeletionArgs(_token)), _permit
        );
    }
}
