using static IronVelo.Example.Models.LoginResHelpers;
using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using IronVelo.Example.Models;
using IronVelo.Flows;
using IronVelo.Flows.Login;
using IronVelo.Types;
using MustUse;

namespace IronVelo.Example.Controllers;

[ApiController]
[Route("auth/login/[controller]")]
[Produces("application/json")]
public class LoginController : ControllerBase
{
    private readonly VeloSdk _idpService;
    private readonly ILogger<LoginController> _logger;

    public LoginController(VeloSdk idpService, ILogger<LoginController> logger)
    {
        _idpService = idpService;
        _logger = logger;
    }

    /// <summary>
    /// Standard Error Response
    /// </summary>
    private IActionResult ResErr<TE>(TE err) => BadRequest(FErr(err));

    /// <summary>
    /// Error Response, State Transition
    /// </summary>
    private IActionResult ResErrState<TS>(TS err) where TS : IState<LoginState> => Ok(ErrState(err));
    
    /// <summary>
    /// Successful Response, State Transition
    /// </summary>
    private IActionResult ResOkState<TS>(TS ok) where TS : IState<LoginState> => Ok(Success(ok));

    /// <summary>
    /// Successful Response, Token Issuance
    /// </summary>
    private IActionResult ResOkToken(Token token) => Ok(Token(token));
    
    /// <summary>
    /// Handles login initiation with username and password.
    /// </summary>
    /// <param name="username">Username for login</param>
    /// <param name="password">Password for login</param>
    /// <returns>Action result indicating success or failure</returns>
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status400BadRequest)]
    [HttpGet("hello")]
    public Task<IActionResult> LoginHello([FromQuery] string username, [FromQuery] string password)
    {
        // Example with detailed logging via `Inspect`. Though, the IdP itself has detailed telemetry.
        return Password.From(password)
            .Inspect(_ => _logger.LogInformation("Password processed for user {Username}.", username))
            .InspectErr(err => _logger.LogError("Password processing failed for user {Username}: {Error}", username, err))
            .MapErr(ResErr) // convert the error type into an IActionResult
            .BindFut(pass => _idpService.Login()
                .Start(username, pass)
                .Inspect(_ => _logger.LogInformation("Login started for user {Username}.", username))
                .InspectErr(err => _logger
                    .LogError("Login start failed for user {Username}: {Error}", username, err)
                )
                .MapErr(ResErr)
            )
            .MapOrElse(ResErr, ResOkState); // Turn the FutResult into a Task<IActionResult>
    }
    
    /// <summary>
    /// Handles login requests and routes based on the login state.
    /// </summary>
    /// <param name="request">Login request object</param>
    /// <returns>Action result indicating success or failure</returns>
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status400BadRequest)]
    [HttpPost]
    public Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        return request.State.State switch
        {
            LoginStateE.InitMfa => HandleInitMfa(request),
            LoginStateE.RetryInitMfa => HandleRetryInitMfa(request),
            LoginStateE.VerifyOtp => HandleVerifyOtp(request),
            LoginStateE.VerifyTotp => HandleVerifyTotp(request),
            _ => throw new Exception("Impossibility! LoginStateE is exhaustive!")
        };
    }
    
    /// <summary>
    /// Handles the initial MFA state.
    /// </summary>
    /// <param name="request">Login request object</param>
    /// <returns>Action result indicating success or failure</returns>
    [MustUse]
    private Task<IActionResult> HandleInitMfa(LoginRequest request)
    {
        var mfaInitResume = _idpService.Login().Resume().InitMfa(request.State);
        return HandleMfaInit(mfaInitResume, request);
    }
    
    /// <summary>
    /// Handles the retry MFA state.
    /// </summary>
    /// <param name="request">Login request object</param>
    /// <returns>Action result indicating success or failure</returns>
    [MustUse]
    private Task<IActionResult> HandleRetryInitMfa(LoginRequest request)
    {
        var mfaRetryResume = _idpService.Login().Resume().RetryInitMfa(request.State);
        return HandleMfaInit(mfaRetryResume, request);
    }
    
    /// <summary>
    /// Handles MFA initialization.
    /// </summary>
    /// <typeparam name="TF">Type parameter for the MFA state</typeparam>
    /// <param name="state">MFA state object</param>
    /// <param name="request">Login request object</param>
    /// <returns>Action result indicating success or failure</returns>
    [MustUse]
    private Task<IActionResult> HandleMfaInit<TF>(MfaBase<TF> state, LoginRequest request) where TF: IState<LoginState>
    {
        if (request.MfaKind is not { } mfaKind) return Task.FromResult(ResErr("Needs MFA kind"));
    
        return MfaParser.From(mfaKind)
            .MapErr(ResErr)
            .MapFut(selected => MfaInitRouter(selected, state))
            .Collapse();  
    }
    
    /// <summary>
    /// Routes MFA initialization based on the kind of MFA the user would like to use.
    /// </summary>
    /// <typeparam name="TF">Type parameter for the MFA state</typeparam>
    /// <param name="mfaKind">Kind of MFA</param>
    /// <param name="state">MFA state object</param>
    /// <returns>Action result indicating success or failure</returns>
    [MustUse]
    private Task<IActionResult> MfaInitRouter<TF>(MfaKind mfaKind, MfaBase<TF> state) where TF: IState<LoginState>
    {
        return mfaKind switch
        {
            MfaKind.Email => state.Email().MapOrElse(ResErrState, ResOkState),
            MfaKind.Sms => state.Sms().MapOrElse(ResErrState, ResOkState),
            MfaKind.Totp => state.Totp().MapOrElse(ResErrState, ResOkState),
            _ => throw new Exception("Impossibility! MFA Kinds are exhaustive!")
        };
    }
    
    /// <summary>
    /// Handles OTP verification state for email and SMS.
    /// </summary>
    /// <param name="request">Login request object</param>
    /// <returns>Action result indicating success or failure</returns> 
    [MustUse]
    private Task<IActionResult> HandleVerifyOtp(LoginRequest request)
    {
        if (request.OtpGuess is not { } rawOtp) return Task.FromResult(ResErr("Needs OTP guess"));

        var resumed = _idpService.Login().Resume().VerifyOtp(request.State);
        
        return SimpleOtp.From(rawOtp)
            .MapErr(ResErr)
            .BindFut(otp => resumed.Guess(otp).MapErr(ResErrState))
            .MapOrElse(failure => failure, ResOkToken);
    }
    
    /// <summary>
    /// Handles TOTP verification state (authenticator apps).
    /// </summary>
    /// <param name="request">Login request object</param>
    /// <returns>Action result indicating success or failure</returns>
    [MustUse]
    private Task<IActionResult> HandleVerifyTotp(LoginRequest request)
    {
        if (request.OtpGuess is not { } rawTotp) return Task.FromResult(ResErr("Needs TOTP guess"));

        var resumed = _idpService.Login().Resume().VerifyTotp(request.State);
        
        return Totp.From(rawTotp)
            .MapErr(ResErr)
            .BindFut(totp => resumed.Guess(totp).MapErr(ResErrState))
            .MapOrElse(failure => failure, ResOkToken);
    }
}