using IronVelo.Example.Models;
using IronVelo.Flows;
using IronVelo.Flows.Ticket;
using IronVelo.Types;
using Microsoft.AspNetCore.Mvc;
using MustUse;

namespace IronVelo.Example.Controllers;

[ApiController]
[Route("auth/ticket")]
[Produces("application/json")]
public class TicketController : ControllerBase
{
    private readonly VeloSdk _idpService;
    private readonly ILogger<TicketController> _logger;

    public TicketController(VeloSdk idpService, ILogger<TicketController> logger)
    {
        _idpService = idpService;
        _logger = logger;
    }

    /// <summary>
    /// Helper class for creating consistent ticket responses
    /// </summary>
    private static class ResponseBuilder
    {
        /// <summary>
        /// Creates an error response with an error message
        /// </summary>
        public static TicketResponse Error<TE>(TE err) 
            => new(
                State: null, 
                ErrState: null, 
                ErrMsg: err?.ToString() ?? "Error!", 
                Token: null, 
                ProvisioningUri: null, 
                IssuanceInfo: null
            );

        /// <summary>
        /// Creates an error response with an error state
        /// </summary>
        public static TicketResponse ErrorState<TS>(TS err) where TS : IState<TicketState> 
            => new(
                State: null, 
                ErrState: err.GetState(), 
                ErrMsg: null, 
                Token: null, 
                ProvisioningUri: null, 
                IssuanceInfo: null
            );

        /// <summary>
        /// Creates a success response with a state
        /// </summary>
        public static TicketResponse State<TS>(TS state) where TS : IState<TicketState> 
            => new(
                State: state.GetState(), 
                ErrState: null, 
                ErrMsg: null, 
                Token: null, 
                ProvisioningUri: null, 
                IssuanceInfo: null
            );

        /// <summary>
        /// Creates a success response with a token
        /// </summary>
        public static TicketResponse Token(Token token) 
            => new(
                State: null, 
                ErrState: null, 
                ErrMsg: null, 
                Token: token.Encode(), 
                ProvisioningUri: null, 
                IssuanceInfo: null
            );

        /// <summary>
        /// Creates a success response with ticket issuance information
        /// </summary>
        public static TicketResponse IssuedTicket(TicketIssuanceResult result) 
            => new(
                State: null, 
                ErrState: null, 
                ErrMsg: null, 
                Token: null, 
                ProvisioningUri: null, 
                IssuanceInfo: new TicketIssuanceInfo(result.Ticket.Encode(), result.ExpiresAt)
            );

        /// <summary>
        /// Creates a success response with TOTP provisioning URI
        /// </summary>
        public static TicketResponse ProvisioningUri(VerifyTotpSetup state) 
            => new(
                State: state.GetState(), 
                ErrState: null, 
                ErrMsg: null, 
                Token: null, 
                ProvisioningUri: state.ProvisioningUri, 
                IssuanceInfo: null
            );
    }

    /// <summary>
    /// Standard Error Response
    /// </summary>
    private IActionResult ResErr<TE>(TE err) => BadRequest(ResponseBuilder.Error(err));

    /// <summary>
    /// Error Response, State Transition
    /// </summary>
    private IActionResult ResErrState<TS>(TS err) where TS : IState<TicketState> => 
        Ok(ResponseBuilder.ErrorState(err));
    
    /// <summary>
    /// Successful Response, State Transition
    /// </summary>
    private IActionResult ResOkState<TS>(TS ok) where TS : IState<TicketState> => 
        Ok(ResponseBuilder.State(ok));

    /// <summary>
    /// Successful Response, Token Issuance
    /// </summary>
    private IActionResult ResOkToken(Token token) => 
        Ok(ResponseBuilder.Token(token));

    [ProducesResponseType(typeof(TicketResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TicketResponse), StatusCodes.Status400BadRequest)]
    [HttpPost("issue")]
    public Task<IActionResult> IssueTicket([FromBody] IssueTicketRequest request)
    {
        return Token.TryDecode(request.Token)
            .InspectErr(e => _logger.LogError("Invalid token provided by admin: {Error}", e))
            .MapErr(e => ResErr(e))
            .BindFut(token => _idpService.Ticket()
                .Issue(token, request.TargetUsername, request.Kind, request.Reason)
                .InspectErr(err => _logger.LogError(
                    "Ticket issuance failed for user {Username}: {Error}",
                    request.TargetUsername, err)
                )
                .MapErr(ResErr)
            )
            .MapOrElse(ResErr, result => Ok(ResponseBuilder.IssuedTicket(result)));
    }

    [ProducesResponseType(typeof(TicketResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TicketResponse), StatusCodes.Status400BadRequest)]
    [HttpPost("redeem")]
    public Task<IActionResult> RedeemTicket([FromBody] RedeemTicketRequest request)
    {
        return Types.Ticket.TryDecode(request.Ticket)
            .InspectErr(e => _logger.LogError("Invalid ticket format: {Error}", e))
            .MapErr(e => ResErr(e))
            .BindFut(ticket => _idpService.Ticket()
                .Redeem(ticket, request.Operation)
                .InspectErr(err => _logger.LogError(
                    "Ticket redemption failed: {Error}", err)
                )
                .MapErr(ResErr)
            )
            .MapOrElse(ResErr, ResOkState);
    }

    [ProducesResponseType(typeof(TicketResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(TicketResponse), StatusCodes.Status400BadRequest)]
    [HttpPost]
    public Task<IActionResult> ProcessTicket([FromBody] TicketRequest request)
    {
        return request.State.State switch
        {
            TicketStateE.VerifiedTicket => HandleVerifiedTicket(request),
            TicketStateE.ResetPassword => HandleResetPassword(request),
            TicketStateE.SetupMfa => HandleSetupMfa(request),
            TicketStateE.CompleteRecovery => HandleCompleteRecovery(request),
            _ => throw new Exception("Impossibility! TicketStateE is exhaustive!")
        };
    }

    [MustUse]
    private Task<IActionResult> HandleVerifiedTicket(TicketRequest request)
    {
        var verifiedTicket = _idpService.Ticket().Resume().VerifiedTicket(request.State);
        
        return verifiedTicket.Proceed()
            .ContinueWith(task =>
            {
                var nextState = Utils.Resolve.Get(task);
                return nextState switch
                {
                    NextRecoveryState.ResetPasswordState resetPasswordState => 
                        (IActionResult)ResOkState(resetPasswordState.State),
                    NextRecoveryState.SetupMfaState setupMfaState => 
                        ResOkState(setupMfaState.State),
                    _ => throw new Exception("Unknown next recovery state")
                };
            });
    }

    [MustUse]
    private Task<IActionResult> HandleResetPassword(TicketRequest request)
    {
        if (request.Password is not { } rawPassword)
        {
            return Task.FromResult(ResErr("You must provide a password"));
        }

        var resetPassword = _idpService.Ticket().Resume().ResetPassword(request.State);

        return Password.From(rawPassword)
            .MapErr(e => ResErr(e))
            .BindFut(pass => resetPassword.Reset(pass)
                .MapErr(ResErr))
            .MapOrElse(
                err => err,
                nextState => nextState switch
                {
                    SetupMfa setupMfa => ResOkState(setupMfa),
                    CompleteRecovery completeRecovery => ResOkState(completeRecovery),
                    _ => throw new Exception("Unknown next state after password reset")
                }
            );
    }

    [MustUse]
    private Task<IActionResult> HandleSetupMfa(TicketRequest request)
    {
        var setupMfa = _idpService.Ticket().Resume().SetupMfa(request.State);

        // Handle MFA setup based on requested MFA kind and verification
        if (request.MfaSetup is null)
        {
            return Task.FromResult(ResErr("MFA setup information is required"));
        }

        if (request.MfaSetup.MfaKind is not { } mfaKind)
        {
            return Task.FromResult(ResErr("MFA kind is required"));
        }

        // Check if verification is needed
        if (request.MfaSetup.IsVerification)
        {
            return HandleMfaVerification(mfaKind, request.MfaSetup, request.State);
        }

        // Handle initial MFA setup
        return HandleMfaSetup(mfaKind, setupMfa, request.MfaSetup.Contact);
    }

    [MustUse]
    private Task<IActionResult> HandleMfaSetup(string mfaKind, SetupMfa setupMfa, string? contact)
    {
        return MfaParser.From(mfaKind)
            .MapErr(ResErr)
            .MapFut(kind => 
            {
                return kind switch
                {
                    MfaKind.Email when contact is not null => 
                        setupMfa.Email(contact)
                            .ContinueWith(
                                task => (IActionResult)ResOkState(Utils.Resolve.Get(task))
                            ),
                    MfaKind.Sms when contact is not null => 
                        setupMfa.Sms(contact)
                            .ContinueWith(
                                task => (IActionResult)ResOkState(Utils.Resolve.Get(task))
                            ),
                    MfaKind.Totp => 
                        setupMfa.Totp()
                             .ContinueWith(task => (IActionResult)Ok(
                                 ResponseBuilder.ProvisioningUri(Utils.Resolve.Get(task))
                             )),
                    _ => Task.FromResult(
                        ResErr($"Invalid MFA configuration. Contact info is required for Email/SMS")
                    )
                };
            })
            .MapOrElse(err => err, result => result);
    }

    [MustUse]
    private Task<IActionResult> HandleMfaVerification(
        string mfaKind, 
        MfaSetupInfo mfaSetup, 
        TicketState state)
    {
        if (mfaSetup.Otp is null)
        {
            return Task.FromResult(ResErr("OTP is required for verification"));
        }

        return MfaParser.From(mfaKind)
            .MapErr(ResErr)
            .MapFut(kind =>
            {
                if (kind == MfaKind.Totp)
                {
                    // Handle TOTP verification using JustVerifyTotp
                    var verifyTotp = _idpService.Ticket().Resume().VerifyTotpSetup(state);
                    return Totp.From(mfaSetup.Otp)
                        .MapErr(ResErr)
                        .BindFut(totp => verifyTotp.Guess(totp).MapErr(ResErrState))
                        .MapOrElse(result => result, ResOkState);
                }
                else
                {
                    // Handle Email/SMS verification
                    var verifyMfa = _idpService.Ticket().Resume().VerifyMfaSetup(state, kind);
                    return SimpleOtp.From(mfaSetup.Otp)
                        .MapErr(ResErr)
                        .BindFut(otp => verifyMfa.Guess(otp).MapErr(ResErrState))
                        .MapOrElse(result => result, ResOkState);
                }
            })
            .MapOrElse(err => err, result => result);
    }

    [MustUse]
    private Task<IActionResult> HandleCompleteRecovery(TicketRequest request)
    {
        if (request.Token is not { } rawToken)
        {
            return Task.FromResult(ResErr("Token is required to complete recovery"));
        }

        return Token.TryDecode(rawToken)
            .MapErr(ResErr)
            .MapFut(token => 
            {
                var completeRecovery = _idpService.Ticket().Resume().CompleteRecovery(request.State);
                return completeRecovery.Complete(token).ContinueWith(task => 
                {
                    var newToken = Utils.Resolve.Get(task);
                    return (IActionResult)ResOkToken(newToken);
                });
            })
            .MapOrElse(err => err, result => result);
    }
}
