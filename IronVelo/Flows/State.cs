namespace IronVelo.Flows;

/// <summary>
/// Denotes the state to transition to in <see cref="Flows.Login.ResumeLoginState"/>.
/// </summary>
public enum LoginStateE
{
    /// <summary>
    /// On resume, invoke <see cref="Flows.Login.ResumeLoginState.InitMfa"/>.
    /// </summary>
    InitMfa,
    /// <summary>
    /// On resume, invoke <see cref="Flows.Login.ResumeLoginState.RetryInitMfa"/>. 
    /// </summary>
    RetryInitMfa,
    /// <summary>
    /// On resume, invoke <see cref="Flows.Login.ResumeLoginState.VerifyOtp"/>.  
    /// </summary>
    VerifyOtp,
    /// <summary>
    /// On resume, invoke <see cref="Flows.Login.ResumeLoginState.VerifyTotp"/>.
    /// </summary>
    VerifyTotp
}

/// <summary>
/// Serializable representation of a login state, enabling multistep flows without needing to track state yourself.
/// </summary>
/// <param name="State">Indicates the state to <see cref="Login.HelloLogin.Resume"/> for continuing the process.</param>
/// <param name="Permit">Secure representation of the future state, used by the IdP and SDK. One should ignore this.</param>
/// <param name="AvailableMfa">The MFA kinds the user has previously set up.</param>
public record LoginState(
    LoginStateE State,
    string Permit,
    MfaKind[] AvailableMfa
);

/// <summary>
/// Denotes the state to transition to in <see cref="Flows.Signup.ResumeSignupState"/>. 
/// </summary>
public enum SignupStateE
{
    /// <summary>
    /// On resume, invoke <see cref="Flows.Signup.ResumeSignupState.Password"/>.
    /// </summary>
    Password,
    /// <summary>
    /// On resume, invoke <see cref="Flows.Signup.ResumeSignupState.SetupFirstMfa"/>.
    /// </summary>
    SetupFirstMfa,
    /// <summary>
    /// On resume, invoke <see cref="Flows.Signup.ResumeSignupState.SetupMfaOrFinalize"/>.
    /// </summary>
    SetupMfaOrFinalize,
    /// <summary>
    /// On resume, invoke <see cref="Flows.Signup.ResumeSignupState.VerifyOtpSetup"/>.
    /// </summary>
    VerifyOtpSetup,
    /// <summary>
    /// On resume, invoke <see cref="Flows.Signup.ResumeSignupState.VerifyTotpSetup"/>.
    /// </summary>
    VerifyTotpSetup,
}

/// <summary>
/// Serializable representation of a signup state, enabling multistep flows without needing to track 
/// state yourself.
/// </summary>
/// <param name="State">Indicates the state to <see cref="Signup.HelloSignup.Resume"/> for continuing 
/// the process.</param>
/// <param name="Permit">Secure representation of the future state, used by the IdP and SDK. One should 
/// ignore this.</param>
/// <param name="AlreadySetup">The MFA kinds the user has already setup in this signup flow.</param>
/// <param name="Current">
/// If the previous state was the initiation of MFA verification. This will be non-null if the <c>State</c> property is
/// either <c>VerifyOtpSetup</c> or <c>VerifyTotpSetup</c>.
/// </param>
public record SignupState(
    SignupStateE State,
    string Permit,
    List<MfaKind> AlreadySetup,
    MfaKind? Current
);

/// <summary>
/// Denotes the state to transition to in <see cref="Flows.Delete.ResumeDeleteState"/>. 
/// </summary>
public enum DeleteStateE
{
    /// <summary>
    /// On resume, invoke <see cref="Flows.Delete.ResumeDeleteState.ConfirmPassword"/>.
    /// </summary>
    ConfirmPassword,
    /// <summary>
    /// On resume, invoke <see cref="Flows.Delete.ResumeDeleteState.ConfirmDeletion"/>.
    /// </summary>
    ConfirmDeletion
}

/// <summary>
/// Serializable representation of an account deletion state, enabling multistep flows without 
/// needing to track state yourself.
/// </summary>
/// <param name="State">Indicates the state to <see cref="Delete.AskDelete.Resume"/> for continuing the 
/// process.</param>
/// <param name="Permit">Secure representation of the future state, used by the IdP and SDK. One should 
/// ignore this.</param>
/// <param name="Token">
/// The token associated with the user's login state. When providing on 
/// <see cref="Delete.AskDelete.Resume"/> this must be valid for use. Otherwise, the state will throw 
/// an error.
/// </param>
public record DeleteState
(
    DeleteStateE State,
    string Permit,
    string Token
);

/// <summary>
/// Denotes the state to transition to in <see cref="Flows.MigrateLogin.ResumeLoginState"/>. 
/// </summary>
public enum MigrateLoginStateE
{
    /// <summary>
    /// On resume, invoke <see cref="Flows.MigrateLogin.ResumeLoginState.SetupFirstMfa"/>.
    /// </summary>
    SetupFirstMfa,
    /// <summary>
    /// On resume, invoke <see cref="Flows.MigrateLogin.ResumeLoginState.NewMfaOrLogin"/>.
    /// </summary>
    NewMfaOrLogin,
    /// <summary>
    /// On resume, invoke <see cref="Flows.MigrateLogin.ResumeLoginState.VerifyOtpSetup"/>.
    /// </summary>
    VerifyOtpSetup,
    /// <summary>
    /// On resume, invoke <see cref="Flows.MigrateLogin.ResumeLoginState.VerifyTotpSetup"/>.
    /// </summary>
    VerifyTotpSetup
}

/// <summary>
/// Serializable representation of a migrating login state, enabling multistep flows without needing 
/// to track state yourself.
/// </summary>
/// <param name="State">Indicates the state to <see cref="MigrateLogin.HelloLogin.Resume"/> for 
/// continuing the process.</param>
/// <param name="Permit">Secure representation of the future state, used by the IdP and SDK. One 
/// should ignore this.</param>
/// <param name="AlreadySetup">The MFA kinds the user has already setup in this migrating login 
/// flow.</param>
/// <param name="Current">
/// If the previous state was the initiation of MFA verification. This will be non-null if the 
/// <c>State</c> property is either <c>VerifyOtpSetup</c> or <c>VerifyTotpSetup</c>.
/// </param>
public record MigrateLoginState(
    MigrateLoginStateE State,
    string Permit,
    List<MfaKind> AlreadySetup,
    MfaKind? Current
);

/// <summary>
/// Represents a state enumeration for the MFA update flow, tracking the current stage
/// of the update process.
///
/// Denotes the state to transition to in <see cref="Flows.MigrateLogin.ResumeLoginState"/>. 
/// </summary>
/// <remarks>
/// The states follow this progression:
/// <list type="number">
///   <item><description>StartUpdate - Initial state when beginning the update</description></item>
///   <item><description>CheckOtp/CheckTotp - Verification of existing MFA</description></item>
///   <item><description>Decide - Choosing what changes to make</description></item>
///   <item><description>EnsureOtpSetup/EnsureTotpSetup - Setting up new MFA method</description></item>
///   <item><description>FinalizeUpdate/FinalizeRemoval - Confirming changes</description></item>
/// </list>
/// </remarks>
public enum UpdateMfaStateE
{
    /// <summary>
    /// On resume, invoke <see cref="Flows.UpdateMfa.ResumeUpdateState.InitMfaCheck"/>.
    /// </summary>
    StartUpdate,
    /// <summary>
    /// On resume, invoke <see cref="Flows.UpdateMfa.ResumeUpdateState.CheckOtp"/>.
    /// </summary>
    CheckOtp,
    /// <summary>
    /// On resume, invoke <see cref="Flows.UpdateMfa.ResumeUpdateState.CheckTotp"/>.
    /// </summary>
    CheckTotp,
    /// <summary>
    /// On resume, invoke <see cref="Flows.UpdateMfa.ResumeUpdateState.DecideUpdate"/>.
    /// </summary>
    Decide,
    /// <summary>
    /// On resume, invoke <see cref="Flows.UpdateMfa.ResumeUpdateState.FinalizeRemoval"/>.
    /// </summary>
    FinalizeRemoval,
    /// <summary>
    /// On resume, invoke <see cref="Flows.UpdateMfa.ResumeUpdateState.EnsureOtp"/>.
    /// </summary>
    EnsureOtpSetup,
    /// <summary>
    /// On resume, invoke <see cref="Flows.UpdateMfa.ResumeUpdateState.EnsureTotp"/>.
    /// </summary>
    EnsureTotpSetup,
    /// <summary>
    /// On resume, invoke <see cref="Flows.UpdateMfa.ResumeUpdateState.FinalizeUpdate"/>.
    /// </summary>
    FinalizeUpdate
}

/// <summary>
/// Represents the current progress in the MFA update flow. This is used when resuming
/// a previously started update process.
/// </summary>
/// <remarks>
/// The state contains:
/// <list type="bullet">
///   <item><description>Current stage of the MFA update process</description></item>
///   <item><description>Authentication permit for the flow</description></item>
///   <item><description>List of configured MFA methods</description></item>
/// </list>
/// </remarks>
public record UpdateMfaState(
    UpdateMfaStateE State,
    string Permit,
    MfaKind[] OldMfa
);


/// <summary>
/// Denotes the state to transition to in <see cref="Ticket.ResumeTicketState"/>.
/// </summary>
public enum TicketStateE
{
    /// <summary>
    /// On resume, invoke <see cref="Ticket.ResumeTicketState.VerifiedTicket"/>.
    /// </summary>
    VerifiedTicket,
    
    /// <summary>
    /// On resume, invoke <see cref="Ticket.ResumeTicketState.ResetPassword"/>.
    /// </summary>
    ResetPassword,
    
    /// <summary>
    /// On resume, invoke <see cref="Ticket.ResumeTicketState.SetupMfa"/>.
    /// </summary>
    SetupMfa,
    
    /// <summary>
    /// On resume, invoke <see cref="Ticket.ResumeTicketState.CompleteRecovery"/>.
    /// </summary>
    CompleteRecovery
}

/// <summary>
/// Serializable representation of a ticket state, enabling multistep flows without needing to 
/// track state yourself.
/// </summary>
/// <param name="State">Indicates the state to <see cref="Ticket.HelloTicket.Resume"/> for 
/// continuing the process.</param>
/// <param name="Permit">Secure representation of the future state, used by the IdP and SDK.</param>
/// <param name="OperationType">The type of operation the ticket is being used for.</param>
public record TicketState(
    TicketStateE State,
    string Permit,
    Ticket.TicketOperation? OperationType
);
