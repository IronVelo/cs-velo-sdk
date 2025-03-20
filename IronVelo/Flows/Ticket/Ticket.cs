using IronVelo.Types;
using IronVelo.Utils;
using IronVelo.Exceptions;
using MustUse;
using Newtonsoft.Json;
using ResultAble;

namespace IronVelo.Flows.Ticket;

/// <summary>
/// Defines the kinds of tickets that can be issued for account recovery.
/// </summary>
public enum TicketKind
{
    /// <summary>
    /// Allows resetting either password OR MFA, but not both.
    /// Requires the user to authenticate with the method they still have access to.
    /// </summary>
    Mutual,
    
    /// <summary>
    /// Allows resetting all forms of authentication.
    /// Highest security risk and requires elevated permissions to issue.
    /// </summary>
    Full
}

/// <summary>
/// Defines the operations that can be performed with a ticket.
/// </summary>
public enum TicketOperation
{
    /// <summary>
    /// Reset the user's password.
    /// </summary>
    ResetPassword,
    
    /// <summary>
    /// Reset the user's MFA methods.
    /// </summary>
    ResetMfa,
    
    /// <summary>
    /// Reset both the user's password and MFA methods (only available with Full tickets).
    /// </summary>
    ResetAll
}

/// <summary>
/// Error returned when a ticket verification fails.
/// </summary>
public enum TicketVerificationError
{
    /// <summary>
    /// The ticket is invalid (malformed or tampered with).
    /// </summary>
    InvalidTicket,
    
    /// <summary>
    /// The ticket does not grant permission for the requested operation.
    /// </summary>
    InvalidOp
}

// Issue Ticket Flow

internal record IssueTicketArgs(
    [property: JsonProperty("token")] string Token,
    [property: JsonProperty("username")] string TargetUsername,
    [property: JsonProperty("kind")] TicketKind Kind,
    [property: JsonProperty("reason")] string Reason
);

internal record IssueTicketTlArgs([property: JsonProperty("issue_ticket")] IssueTicketArgs Args);

[Result]
internal partial record IssueTicketRes(
    [property: Ok, JsonProperty("issue", NullValueHandling = NullValueHandling.Ignore)]
    TicketIssuanceResult Success,
    [property: Error, JsonProperty("issue_error", NullValueHandling = NullValueHandling.Ignore)]
    string Error
);

/// <summary>
/// Represents the result of ticket issuance.
/// </summary>
/// <param name="ExpiresAt">The expiration time of the ticket in Unix timestamp format.</param>
/// <param name="Ticket">The ticket granting access to perform some account recovery.</param>
public record TicketIssuanceResult(
    [property: JsonProperty("ticket")] Types.Ticket Ticket,
    [property: JsonProperty("exp")] long ExpiresAt
);

/// <summary>
/// Error returned when ticket issuance fails.
/// </summary>
public enum TicketIssuanceError
{
    /// <summary>
    /// The super user lacks the required permissions to issue this kind of ticket.
    /// </summary>
    InsufficientPermissions,
    
    /// <summary>
    /// The target username does not exist.
    /// </summary>
    UserNotFound,
}

// Redeem Ticket Flow

internal record RedeemTicketArgs(
    [property: JsonProperty("op")] TicketOperation Operation
);

internal record RedeemTicketTlArgs([property: JsonProperty("redeem")] RedeemTicketArgs Args);

internal record ProceedArgs;
internal record ProceedTlArgs([property: JsonProperty("proceed")] ProceedArgs Args);

[Result]
internal partial record RedeemTicketRes(
    [property: Ok, JsonProperty("verify_ticket", NullValueHandling = NullValueHandling.Ignore)]
    string Success,
    [property: Error, JsonProperty("redeem_error", NullValueHandling = NullValueHandling.Ignore)]
    TicketVerificationError Error
);

/// <summary>
/// The ingress state for account recovery using a ticket. The main method for this state is
/// <see cref="Issue"/> for super users and <see cref="Redeem"/> for end users.
/// </summary>
public class HelloTicket
{
    internal HelloTicket(FlowClient client) { _client = client; }
    private readonly FlowClient _client;

    /// <summary>
    /// Issue a ticket to allow a user to recover their account.
    /// </summary>
    /// <param name="token">The token of the super user issuing the ticket.</param>
    /// <param name="targetUsername">The username of the account to be recovered.</param>
    /// <param name="kind">The kind of ticket to issue (Mutual or Full).</param>
    /// <param name="reason">The reason for issuing the ticket (required for audit purposes).</param>
    /// <returns>
    /// <list type="bullet">
    /// <item>
    ///     <term>Success:</term>
    ///     <description>
    ///         The ticket was successfully issued. Contains the ticket ID and expiration time.
    ///     </description>
    /// </item>
    /// <item>
    ///     <term>Failure:</term>
    ///     <description>
    ///         The ticket could not be issued. Contains the reason for the failure.
    ///     </description>
    /// </item>
    /// </list>
    /// </returns>
    /// <exception cref="Exceptions.RequestError">
    /// The token was either expired or revoked, or there was a network error.
    /// </exception>
    public FutResult<TicketIssuanceResult, TicketIssuanceError> Issue(
        Token token, 
        string targetUsername, 
        TicketKind kind, 
        string reason)
    {
        return FutResult<TicketIssuanceResult, TicketIssuanceError>
            .From(_client.SendRequest<IssueTicketTlArgs, IssueTicketRes>(
                new IssueTicketTlArgs(
                    new IssueTicketArgs(token.Encode(), targetUsername, kind, reason))
            ).ContinueWith(task => {
                var res = Resolve.Get(task);
                return res.UnwrapRet().ToResult().MapOrElse(
                    error => Result<TicketIssuanceResult, TicketIssuanceError>.Failure(
                        Enum.Parse<TicketIssuanceError>(error)),
                    success => Result<TicketIssuanceResult, TicketIssuanceError>.Success(success)
                );
            }));
    }

    /// <summary>
    /// Redeem a ticket to begin the account recovery process.
    /// </summary>
    /// <param name="operation">The operation to perform with the ticket.</param>
    /// <param name="ticket">The ticket, granted by an administrator, to perform the action.</param>
    /// <returns>
    /// <list type="bullet">
    /// <item>
    ///     <term>Success:</term>
    ///     <description>
    ///         The ticket was validated and the recovery process can begin.
    ///     </description>
    /// </item>
    /// <item>
    ///     <term>Failure:</term>
    ///     <description>
    ///         The ticket validation failed. Contains the reason for the failure.
    ///     </description>
    /// </item>
    /// </list>
    /// </returns>
    /// <exception cref="Exceptions.RequestError">
    /// There was a network error or the ticket was malformed.
    /// </exception>
    public FutResult<VerifiedTicket, TicketVerificationError> Redeem(
        Types.Ticket ticket, 
        TicketOperation operation)
    {
        return FutResult<VerifiedTicket, TicketVerificationError>
            .From(_client.SendRequest<RedeemTicketTlArgs, RedeemTicketRes>(
                new RedeemTicketTlArgs(
                    new RedeemTicketArgs(operation)),
                // The ticket is our permit
                ticket.Encode()
            ).ContinueWith(task => {
                var res = Resolve.Get(task);
                return res.UnwrapRet().ToResult().MapOrElse(
                    error => Result<VerifiedTicket, TicketVerificationError>.Failure(error),
                    success => Result<VerifiedTicket, TicketVerificationError>.Success(
                        new VerifiedTicket(_client, res.Permit, operation))
                );
            }));
    }

    /// <summary>
    /// For multipart flows, resume a state from the serializable representation.
    /// </summary>
    public ResumeTicketState Resume() => new(_client);
}

/// <summary>
/// Resume the ticket redemption flow from a serialized state, used in multistep flows to avoid the need for
/// tracking state yourself.
/// </summary>
/// <remarks>
/// <b>How do I know which method to invoke?:</b><br/>
/// In order to properly use <see cref="HelloTicket.Resume"/> you should have invoked <see cref="IState{TF}.Serialize"/>
/// in order to provide the state to the client. When the client continues the flow, they should return this serialized
/// representation of the state to your server.
/// <br/><br/>
/// The serialized states all include a <c>State</c> property, which indicates which method to call.
/// <br/><br/>
/// <b>Mapping:</b>
/// <list type="bullet">
/// <item>
///     <description><c>State.VerifiedTicket => ResumeTicketState.VerifiedTicket(state)</c></description>
/// </item>
/// <item>
///     <description><c>State.ResetPassword => ResumeTicketState.ResetPassword(state)</c></description>
/// </item>
/// <item>
///     <description><c>State.SetupMfa => ResumeTicketState.SetupMfa(state)</c></description>
/// </item>
/// <item>
///     <description><c>State.CompleteRecovery => ResumeTicketState.CompleteRecovery(state)</c></description>
/// </item>
/// </list>
/// </remarks>
public record ResumeTicketState
{
    internal ResumeTicketState(FlowClient client) { _client = client; }
    private readonly FlowClient _client;

    /// <summary>
    /// Resumes the ticket redemption flow from the verification state.
    /// </summary>
    /// <param name="state">The current state of the ticket flow.</param>
    /// <returns>A new <see cref="VerifiedTicket"/> instance to handle the verification step.</returns>
    public VerifiedTicket VerifiedTicket(TicketState state) => new(
        _client, 
        state.Permit, 
        state.OperationType
    );

    /// <summary>
    /// Resumes the ticket redemption flow from the password reset state.
    /// </summary>
    /// <param name="state">The current state of the ticket flow.</param>
    /// <returns>A new <see cref="ResetPassword"/> instance to handle the password reset step.</returns>
    public ResetPassword ResetPassword(TicketState state) => new(
        _client,
        state.Permit,
        state.OperationType
    );

    /// <summary>
    /// Resumes the ticket redemption flow from the MFA setup state.
    /// </summary>
    /// <param name="state">The current state of the ticket flow.</param>
    /// <returns>A new <see cref="SetupMfa"/> instance to handle the MFA setup step.</returns>
    public SetupMfa SetupMfa(TicketState state) => new(
        _client,
        state.Permit,
        state.OperationType
    );

    /// <summary>
    /// Resumes the ticket redemption flow from the recovery completion state.
    /// </summary>
    /// <param name="state">The current state of the ticket flow.</param>
    /// <returns>A new <see cref="CompleteRecovery"/> instance to handle the completion step.</returns>
    public CompleteRecovery CompleteRecovery(TicketState state) => new(
        _client,
        state.Permit,
        state.OperationType
    );
    
    /// <summary>
    /// Resumes the ticket redemption flow from the TOTP verification state.
    /// </summary>
    /// <param name="state">The current state of the ticket flow.</param>
    /// <returns>A new <see cref="JustVerifyTotp"/> instance to handle TOTP verification.</returns>
    public JustVerifyTotp VerifyTotpSetup(TicketState state) => new(
        _client,
        state.Permit,
        state.OperationType
    );

    /// <summary>
    /// Resumes the ticket redemption flow from the MFA verification state for SMS/Email.
    /// </summary>
    /// <param name="state">The current state of the ticket flow.</param>
    /// <param name="mfaKind">The type of MFA being verified (SMS or Email)</param>
    /// <returns>A new <see cref="VerifyMfaSetup"/> instance to handle OTP verification.</returns>
    public VerifyMfaSetup VerifyMfaSetup(TicketState state, MfaKind mfaKind) => new(
        _client,
        state.Permit,
        mfaKind,
        state.OperationType
    );
}


[Result]
internal partial record ProceedRes(
    [property: Ok, JsonProperty("next_step")] string NextStep
);

/// <summary>
/// Represents the next state in the recovery process after verification.
/// </summary>
public abstract record NextRecoveryState
{
    private NextRecoveryState() { }

    /// <summary>
    /// The user needs to reset their password.
    /// </summary>
    /// <param name="State">The state for resetting the password.</param>
    public sealed record ResetPasswordState(ResetPassword State) : NextRecoveryState;

    /// <summary>
    /// The user needs to set up new MFA methods.
    /// </summary>
    /// <param name="State">The state for setting up MFA.</param>
    public sealed record SetupMfaState(SetupMfa State) : NextRecoveryState;
}

/// <summary>
/// Verification state for a ticket redemption. Confirms the ticket is valid and
/// transitions to the appropriate recovery state.
/// </summary>
public class VerifiedTicket : IState<TicketState>
{
    internal VerifiedTicket(
        FlowClient client, 
        string? permit, 
        TicketOperation? operation)
    {
        _client = client;
        _permit = permit;
        _operation = operation;
    }

    private readonly FlowClient _client;
    private readonly string? _permit;
    private readonly TicketOperation? _operation;

    /// <inheritdoc />
    public TicketState GetState() => new(
        TicketStateE.VerifiedTicket,
        _permit!,
        _operation
    );

    /// <inheritdoc />
    public string Serialize() => JsonConvert.SerializeObject(GetState());

    /// <summary>
    /// Proceed to the next state in the recovery flow based on the ticket operation.
    /// </summary>
    /// <returns>
    /// The next state in the recovery flow, either ResetPassword, SetupMfa, or both.
    /// </returns>
    /// <exception cref="Exceptions.RequestError">
    /// There was a network error or the permit expired.
    /// </exception>
    public Task<NextRecoveryState> Proceed()
    {
        return _client
            .SendRequest<ProceedTlArgs, ProceedRes>(
                new ProceedTlArgs(new ProceedArgs()),
                _permit
            )
            .ContinueWith<NextRecoveryState>(task => {
                var res = Resolve.Get<Response<ProceedRes>>(task);
                
                return _operation switch
                {
                    TicketOperation.ResetPassword => 
                        new NextRecoveryState.ResetPasswordState(
                            new ResetPassword(_client, res.Permit, _operation)
                        ),
                        TicketOperation.ResetMfa => 
                        new NextRecoveryState.SetupMfaState(
                            new SetupMfa(_client, res.Permit, _operation)
                        ),
                        TicketOperation.ResetAll => 
                        new NextRecoveryState.ResetPasswordState(
                            new ResetPassword(_client, res.Permit, _operation)
                        ),
                        _ => throw new ArgumentOutOfRangeException(
                            nameof(_operation), 
                            _operation, "Unexpected ticket operation type"
                        )
                };
            });
    }
}

internal record ResetPasswordArgs(
    [property: JsonProperty("password")] string Password
);

internal record ResetPasswordTlArgs(
    [property: JsonProperty("reset_password")] ResetPasswordArgs Args
);

[Result]
internal partial record ResetPasswordRes(
    [property: Ok, JsonProperty("password_reset")] bool Success,
    [property: Error, JsonProperty("invalid_password")] PasswordInvalid Error
);

/// <summary>
/// State for resetting a user's password during account recovery with a ticket.
/// </summary>
public class ResetPassword : IState<TicketState>
{
    internal ResetPassword(
        FlowClient client, 
        string? permit, 
        TicketOperation? operation)
    {
        _client = client;
        _permit = permit;
        _operation = operation;
    }

    private readonly FlowClient _client;
    private readonly string? _permit;
    private readonly TicketOperation? _operation;

    /// <inheritdoc />
    public TicketState GetState() => new(
        TicketStateE.ResetPassword,
        _permit!,
        _operation
    );

    /// <inheritdoc />
    public string Serialize() => JsonConvert.SerializeObject(GetState());

    /// <summary>
    /// Reset the user's password.
    /// </summary>
    /// <param name="password">The new password to set for the user's account.</param>
    /// <returns>
    /// <list type="bullet">
    /// <item>
    ///     <term>Success:</term>
    ///     <description>
    ///         The password was successfully reset. Returns the next state in the recovery flow.
    ///     </description>
    /// </item>
    /// <item>
    ///     <term>Failure:</term>
    ///     <description>
    ///         The password reset failed. Contains the validation errors.
    ///     </description>
    /// </item>
    /// </list>
    /// </returns>
    /// <exception cref="Exceptions.RequestError">
    /// There was a network error or the permit expired.
    /// </exception>
    public FutResult<object, PasswordInvalid> Reset(Password password)
    {
        return FutResult<object, PasswordInvalid>
            .From(_client.SendRequest<ResetPasswordTlArgs, ResetPasswordRes>(
                new ResetPasswordTlArgs(new ResetPasswordArgs(password.ToString())),
                _permit
            ).ContinueWith(task => {
                var res = Resolve.Get(task);
                return res.UnwrapRet().ToResult().MapOrElse(
                    error => Result<object, PasswordInvalid>.Failure(error),
                    _ => Result<object, PasswordInvalid>.Success(
                        _operation == TicketOperation.ResetAll
                            ? new SetupMfa(_client, res.Permit, _operation)
                            : new CompleteRecovery(_client, res.Permit, _operation)
                    )
                );
            }));
    }
}

internal abstract class MfaSetupKind
{
    internal sealed class Totp : MfaSetupKind
    {
        internal Totp(string displayName) 
        {
            DisplayName = displayName;
        }
        internal string DisplayName { get; }
    }
    internal sealed class Sms : MfaSetupKind
    {
        internal Sms(string phoneNumber)
        {
            PhoneNumber = phoneNumber;
        }
        internal string PhoneNumber { get; }
    }
    internal sealed class Email : MfaSetupKind
    {
        internal Email(string emailAddress)
        {
            EmailAddress = emailAddress;
        }
        internal string EmailAddress { get; }
    }
}

internal class MfaSetupSerializer : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return typeof(MfaSetupKind).IsAssignableFrom(objectType);
    }

    private void WriteNonNull(JsonWriter writer, Action<JsonWriter> write)
    {
        writer.WriteStartObject();
        write(writer);
        writer.WriteEndObject();
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is MfaSetupKind mfaKind)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("setup_mfa");
            writer.WriteStartObject();
            writer.WritePropertyName("kind");
            
            switch (mfaKind)
            {
                case MfaSetupKind.Totp totp:
                    WriteNonNull(writer, w =>
                    {
                        w.WritePropertyName("Totp"); 
                        w.WriteValue(totp.DisplayName);
                    });
                    break;
                case MfaSetupKind.Sms sms:
                    WriteNonNull(writer, w =>
                    {
                        w.WritePropertyName("Sms"); 
                        w.WriteValue(sms.PhoneNumber);
                    });
                    break;
                case MfaSetupKind.Email email:
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
        }
        else
        {
            throw new JsonSerializationException("Expected MfaSetupKind type");
        }
    }

    public override object ReadJson(
        JsonReader reader, 
        Type objectType, 
        object? existingValue, 
        JsonSerializer serializer
    )
    {
        throw new NotImplementedException("Deserialization is not supported for MfaSetupKind.");
    }
    
    public override bool CanRead => false;
    public override bool CanWrite => true; 
}

internal record SetupTotpRes([property: JsonProperty("provisioning_uri")] string ProvisioningUri);

/// <summary>
/// State for setting up new MFA methods during account recovery with a ticket.
/// </summary>
public class SetupMfa : IState<TicketState>
{
    internal SetupMfa(
        FlowClient client, 
        string? permit, 
        TicketOperation? operation)
    {
        _client = client;
        _permit = permit;
        _operation = operation;
    }

    private readonly FlowClient _client;
    private readonly string? _permit;
    private readonly TicketOperation? _operation;

    /// <inheritdoc />
    public TicketState GetState() => new(
        TicketStateE.SetupMfa,
        _permit!,
        _operation
    );

    /// <inheritdoc />
    public string Serialize() => JsonConvert.SerializeObject(GetState());

    /// <summary>
    /// Set up TOTP (for authenticator apps) as an available MFA method for the user.
    /// </summary>
    /// <param name="displayName">A display name for the authenticator app.</param>
    /// <returns>
    /// A task that resolves to a VerifyTotpSetup state with a provisioning URI for the authenticator app.
    /// </returns>
    /// <exception cref="Exceptions.RequestError">
    /// There was an unexpected error encountered when making the request.
    /// </exception>
    public Task<VerifyTotpSetup> Totp(string displayName = "Default")
    {
        return _client
            .SendRequest<MfaSetupKind, SetupTotpRes>(
                new MfaSetupKind.Totp(displayName),
                _permit,
                new MfaSetupSerializer()
            )
            .ContinueWith(task => {
                var res = Resolve.Get(task);
                return new VerifyTotpSetup(
                    _client,
                    res.Permit, 
                    res.Ret.ProvisioningUri, 
                    _operation
                );
            });
    }

    /// <summary>
    /// Set up SMS OTPs as an available MFA method for the user.
    /// </summary>
    /// <param name="phoneNumber">The user's phone number.</param>
    /// <returns>
    /// A task that resolves to a VerifyMfaSetup state for the SMS verification.
    /// </returns>
    /// <exception cref="Exceptions.RequestError">
    /// There was an unexpected error encountered when making the request.
    /// </exception>
    public Task<VerifyMfaSetup> Sms(string phoneNumber)
    {
        return _client
            .SendRequest<MfaSetupKind, Infallible>(
                new MfaSetupKind.Sms(phoneNumber),
                _permit,
                new MfaSetupSerializer()
            )
            .ContinueWith(task => {
                var res = Resolve.Get(task);
                return new VerifyMfaSetup(
                    _client, 
                    res.Permit, 
                    MfaKind.Sms, 
                    _operation
                );
            });
    }

    /// <summary>
    /// Set up email OTPs as an available MFA method for the user.
    /// </summary>
    /// <param name="emailAddress">The user's email address.</param>
    /// <returns>
    /// A task that resolves to a VerifyMfaSetup state for the email verification.
    /// </returns>
    /// <exception cref="Exceptions.RequestError">
    /// There was an unexpected error encountered when making the request.
    /// </exception>
    public Task<VerifyMfaSetup> Email(string emailAddress)
    {
        return _client
            .SendRequest<MfaSetupKind, Infallible>(
                new MfaSetupKind.Email(emailAddress),
                _permit,
                new MfaSetupSerializer()
            )
            .ContinueWith(task => {
                var res = Resolve.Get(task);
                return new VerifyMfaSetup(
                    _client, 
                    res.Permit, 
                    MfaKind.Email, 
                    _operation
                );
            });
    }
}

internal record VerifyMfaSetupArgs([property: JsonProperty("guess")] string Guess);
internal record VerifyMfaSetupTlArgs(
    [property: JsonProperty("verify_mfa_setup")] VerifyMfaSetupArgs Args
);

[Result]
internal partial record VerifyMfaSetupRes(
    [property: Ok, JsonProperty("mfa_verified")] bool Success,
    [property: Error, JsonProperty("retry_mfa")] bool Retry
);

/// <summary>
/// State for verifying that the MFA method is properly set up during account recovery.
/// </summary>
public class VerifyMfaSetup : IState<TicketState>
{
    internal VerifyMfaSetup(
        FlowClient client,
        string? permit,
        MfaKind kind,
        TicketOperation? operation)
    {
        _client = client;
        _permit = permit;
        Kind = kind;
        _operation = operation;
    }

    private readonly FlowClient _client;
    private readonly string? _permit;
    private readonly TicketOperation? _operation;

    /// <summary>
    /// The kind of MFA being verified.
    /// </summary>
    public MfaKind Kind { get; }

    /// <inheritdoc />
    public TicketState GetState() => new(
        TicketStateE.SetupMfa,
        _permit!,
        _operation
    );

    /// <inheritdoc />
    public string Serialize() => JsonConvert.SerializeObject(GetState());

    /// <summary>
    /// Verify that the MFA method is controlled/accessible by the user.
    /// </summary>
    /// <param name="guess">What the user believes the OTP is.</param>
    /// <returns>
    /// <list type="bullet">
    /// <item>
    ///     <description>Success: The OTP was correct and now the MFA method is set up.</description>
    /// </item>
    /// <item>
    ///     <description>Failure: The guess was incorrect, the user must try again.</description>
    /// </item>
    /// </list>
    /// </returns>
    /// <exception cref="Exceptions.RequestError">
    /// There was an unexpected error encountered when making the request.
    /// </exception>
    public FutResult<CompleteRecovery, VerifyMfaSetup> Guess(SimpleOtp guess)
    {
        return FutResult<CompleteRecovery, VerifyMfaSetup>
            .From(
                _client.SendRequest<VerifyMfaSetupTlArgs, VerifyMfaSetupRes>(
                    new VerifyMfaSetupTlArgs(new VerifyMfaSetupArgs(guess.ToString())),
                    _permit
                )
                .ContinueWith(task => {
                    var res = Resolve.Get<Response<VerifyMfaSetupRes>>(task);
                    return res.UnwrapRet().ToResult().MapOrElse(
                        _ => Result<CompleteRecovery, VerifyMfaSetup>.Failure(
                            new VerifyMfaSetup(_client, res.Permit, Kind, _operation)
                        ),
                        _ => Result<CompleteRecovery, VerifyMfaSetup>.Success(
                            new CompleteRecovery(_client, res.Permit, _operation)
                        )
                    );
                }));
    }
}

internal record VerifyTotpTlArgs([property: JsonProperty("verify_totp")] VerifyMfaSetupArgs Args);

[Result]
internal partial record VerifyTotpRes(
    [property: Ok, JsonProperty("verify_totp")] string OkMsg,
    [property: Error, JsonProperty("bad_verify_totp")] string ErrMsg
);

/// <summary>
/// Base abstract record for generically interfacing with TOTP verification states
/// across both initial attempts (where the TOTP Provisioning URI is provided) and 
/// retries, where this provisioning URI is no longer tracked.
/// </summary>
public abstract record JustVerifyTotpBase<TI> : IState<TicketState>
    where TI : JustVerifyTotpBase<TI>
{
    internal JustVerifyTotpBase(
        FlowClient client,
        string? permit,
        TicketOperation? operation)
    {
        _client = client;
        _permit = permit;
        _operation = operation;
    }

    private readonly FlowClient _client;
    private string? _permit;
    private readonly TicketOperation? _operation;

    /// <summary>
    /// The kind of MFA being verified.
    /// </summary>
    public MfaKind Kind { get; }

    /// <inheritdoc />
    public TicketState GetState() => new(
        TicketStateE.SetupMfa,
        _permit!,
        _operation
    );

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
    public FutResult<CompleteRecovery, TI> Guess(Totp guess)
    {
        return FutResult<CompleteRecovery, TI>.From(
            _client
                .SendRequest<VerifyTotpTlArgs, VerifyTotpRes>(
                    new VerifyTotpTlArgs(new VerifyMfaSetupArgs(guess.ToString())), 
                    _permit
                )
                .ContinueWith(task => {
                    var ret = Resolve.Get<Response<VerifyTotpRes>>(task);
                    return ret
                        .UnwrapRet()
                        .ToResult()
                        .MapOrElse(
                            _errMsg => Result<CompleteRecovery, TI>
                                .Failure((TI)this with { _permit = ret.Permit }),
                            _okMsg  => Result<CompleteRecovery, TI>
                                .Success(new CompleteRecovery(_client, ret.Permit, _operation))
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
public record VerifyTotpSetup : JustVerifyTotpBase<VerifyTotpSetup>
{
    internal VerifyTotpSetup(
        FlowClient client, 
        string? permit, 
        string provisioningUri,
        TicketOperation? operation
    )
        : base(client, permit, operation) 
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
public record JustVerifyTotp : JustVerifyTotpBase<JustVerifyTotp>
{
    internal JustVerifyTotp(FlowClient client, string? permit, TicketOperation? operation)
        : base(client, permit, operation) 
        { }
}

internal record CompleteArgs([property: JsonProperty("token")] Token Token);
internal record CompleteTlArgs([property: JsonProperty("complete")] CompleteArgs args);
internal record CompleteRecoveryRes([property: JsonProperty("token")] Token Token);

/// <summary>
/// The final state of the account recovery flow, issuing a new token for the recovered account.
/// </summary>
public class CompleteRecovery : IState<TicketState>
{
    internal CompleteRecovery(
        FlowClient client, 
        string? permit, 
        TicketOperation? operation)
    {
        _client = client;
        _permit = permit;
        _operation = operation;
    }

    private readonly FlowClient _client;
    private readonly string? _permit;
    private readonly TicketOperation? _operation;

    /// <inheritdoc />
    public TicketState GetState() => new(
        TicketStateE.CompleteRecovery,
        _permit!,
        _operation
    );

    /// <inheritdoc />
    public string Serialize() => JsonConvert.SerializeObject(GetState());

    /// <summary>
    /// Complete the account recovery process and obtain a new authentication token.
    /// </summary>
    /// <returns>A new authentication token for the recovered account.</returns>
    /// <exception cref="Exceptions.RequestError">
    /// There was a network error or the permit expired.
    /// </exception>
    public Task<Token> Complete(Token token)
    {
        return _client
            .SendRequest<CompleteTlArgs, CompleteRecoveryRes>(
                new CompleteTlArgs(new CompleteArgs(token)),
                _permit
            )
            .ContinueWith(task => {
                var res = Resolve.Get(task);
                return res.Ret.Token;
            });
    }
}
