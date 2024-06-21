using static IronVelo.Example.Models.SignupResHelpers;
using Microsoft.AspNetCore.Mvc;
using IronVelo.Example.Models;
using IronVelo.Flows.Signup;
using IronVelo.Flows;
using IronVelo.Types;
using MustUse;

namespace IronVelo.Example.Controllers;

[ApiController]
[Route("auth/signup/[controller]")]
[Produces("application/json")]
public class SignupController : ControllerBase
{
    private readonly VeloSdk _idpService;
    private readonly ILogger<SignupController> _logger;

    public SignupController(VeloSdk idpService, ILogger<SignupController> logger)
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
    private IActionResult ResErrState<TS>(TS err) where TS : IState<SignupState> => Ok(ErrState(err));
    
    /// <summary>
    /// Successful Response, State Transition
    /// </summary>
    private IActionResult ResOkState<TS>(TS ok) where TS : IState<SignupState> => Ok(Success(ok));

    /// <summary>
    /// Successful Response, Token Issuance
    /// </summary>
    private IActionResult ResOkToken(Token token) => Ok(Token(token));
    
    /// <summary>
    /// Initiates signup process with a username.
    /// </summary>
    /// <param name="username">Username for signup</param>
    /// <returns>Action result indicating success or failure</returns>
    [ProducesResponseType(typeof(SignupResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SignupResponse), StatusCodes.Status400BadRequest)]
    [HttpGet("hello")]
    public Task<IActionResult> SignupHello([FromQuery] string username)
    {
        return _idpService.Signup()
            .Start(username)
            .InspectErr(_ => _logger.LogError("Username already exists! {Username}", username))
            .MapOrElse(ResErr, ResOkState);
    }
    
    /// <summary>
    /// Handles the signup process based on the state.
    /// </summary>
    /// <param name="request">Signup request object</param>
    /// <returns>Action result indicating success or failure</returns>
    [ProducesResponseType(typeof(SignupResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SignupResponse), StatusCodes.Status400BadRequest)]
    [HttpPost]
    public Task<IActionResult> Signup([FromBody] SignupRequest request)
    {
        return request.State.State switch
        {
            SignupStateE.Password => PasswordHandler(request),
            SignupStateE.SetupFirstMfa => SetupFirstMfaHandler(request),
            SignupStateE.VerifyOtpSetup => VerifyOtpSetupHandler(request),
            SignupStateE.VerifyTotpSetup => VerifyTotpSetupHandler(request),
            SignupStateE.SetupMfaOrFinalize => SetupMfaOrFinalizeHandler(request),
            _ => throw new Exception("Impossibility! SignupStateE is exhaustive!")
        };
    }
    
    /// <summary>
    /// Handles password setting during signup.
    /// </summary>
    /// <param name="request">Signup request object</param>
    /// <returns>Action result indicating success or failure</returns>
    [MustUse]
    private Task<IActionResult> PasswordHandler(SignupRequest request)
    {
        if (request.Secret is not { } rawPassword) return Task.FromResult(ResErr("Needs password!"));

        var resumed = _idpService.Signup().Resume().Password(request.State);

        return Password.From(rawPassword)
            .MapFut(resumed.Password)
            .MapOrElse(ResErr, ResOkState);
    }
    
    /// <summary>
    /// Handles the setup of the first MFA method during signup.
    /// </summary>
    /// <param name="request">Signup request object</param>
    /// <returns>Action result indicating success or failure</returns>
    [MustUse]
    private Task<IActionResult> SetupFirstMfaHandler(SignupRequest request)
    {
        if (request.MfaKind is not { } mfaKind) return Task.FromResult(ResErr("Needs MFA kind"));

        var resumed = _idpService.Signup().Resume().SetupFirstMfa(request.State);
        
        return MfaParser.From(mfaKind)
            .MapFut(kind => SetupMfaRouter(kind, request.Contact, resumed))
            .MapOrElse(ResErr, res => res);
    }
    
    /// <summary>
    /// Routes MFA setup based on the kind of MFA.
    /// </summary>
    /// <param name="mfaKind">Kind of MFA</param>
    /// <param name="contact">Contact information</param>
    /// <param name="state">MFA state object</param>
    /// <returns>Action result indicating success or failure</returns>
    [MustUse]
    private async Task<IActionResult> SetupMfaRouter(
        MfaKind mfaKind,
        string? contact,
        SetupMfaBase<VerifyTotpSetup, VerifyMfaSetup, SignupState> state
    )
    {
        if (mfaKind is MfaKind.Email or MfaKind.Sms && contact is null)
        {
            return ResErr("Cannot setup Email or SMS without contact info!");
        }

        return mfaKind switch
        {
            MfaKind.Email => ResOkState(await state.Email(contact!)),
            MfaKind.Sms => ResOkState(await state.Sms(contact!)),
            MfaKind.Totp => ResOkState(await state.Totp()),
            _ => throw new Exception("Impossibility! MFA Kinds are exhaustive!")
        };
    }
    
    /// <summary>
    /// Handles OTP verification during MFA setup.
    /// </summary>
    /// <param name="request">Signup request object</param>
    /// <returns>Action result indicating success or failure</returns>
    [MustUse]
    private Task<IActionResult> VerifyOtpSetupHandler(SignupRequest request)
    {
        if (request.Secret is not {} rawOtp) return Task.FromResult(ResErr("Needs OTP guess as Secret!"));
        var resume = _idpService.Signup().Resume().VerifyOtpSetup(request.State);
        
        return SimpleOtp.From(rawOtp)
            .MapErr(ResErr)
            .BindFut(otp => resume.Guess(otp).MapErr(ResErrState))
            .MapOrElse(err => err, ResOkState);
    }
    
    /// <summary>
    /// Handles TOTP verification during MFA setup.
    /// </summary>
    /// <param name="request">Signup request object</param>
    /// <returns>Action result indicating success or failure</returns>
    [MustUse]
    private Task<IActionResult> VerifyTotpSetupHandler(SignupRequest request)
    {
        if (request.Secret is not {} rawTotp) return Task.FromResult(ResErr("Needs TOTP guess as Secret!"));
        var resumed = _idpService.Signup().Resume().VerifyTotpSetup(request.State);

        return Totp.From(rawTotp)
            .MapErr(ResErr)
            .BindFut(totp => resumed.Guess(totp).MapErr(ResErrState))
            .MapOrElse(err => err, ResOkState);
    }
    
    /// <summary>
    /// Handles MFA setup or finalization of the signup process.
    /// </summary>
    /// <param name="request">Signup request object</param>
    /// <returns>Action result indicating success or failure</returns>
    [MustUse]
    private async Task<IActionResult> SetupMfaOrFinalizeHandler(SignupRequest request)
    {
        var resumed = _idpService.Signup().Resume().SetupMfaOrFinalize(request.State);
        
        // if no MFA kind was provided, finalize the signup process
        if (request.MfaKind is {} rawMfaKind)
        {
            return await MfaParser.From(rawMfaKind)
                .MapErr(ResErr)
                .MapFut(mfaKind => SetupMfaRouter(mfaKind, request.Contact, resumed))
                .Collapse();
        }

        return ResOkToken(await resumed.Finish());
    }
}