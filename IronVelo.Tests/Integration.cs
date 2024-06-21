using System.Web;
using IronVelo.Flows;
using IronVelo.Types;
using Newtonsoft.Json;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace IronVelo.Tests;
public class TotpGen
{
    private OtpNet.Totp TotpInner { get; }
    public TotpGen(string provisioningUri)
    {
        var uri = new Uri(provisioningUri);
        var query = HttpUtility.ParseQueryString(uri.Query);
		
        var secretBase32 = query["secret"];
        var digits = int.Parse(query["digits"] ?? "8"); 
        var period = int.Parse(query["period"] ?? "30");
		
        var secretBytes = OtpNet.Base32Encoding.ToBytes(secretBase32);
        var totp = new OtpNet.Totp(secretBytes, mode: OtpNet.OtpHashMode.Sha256, totpSize: digits, step: period);

        TotpInner = totp;
    }

    public Totp Gen() => Totp.From(TotpInner.ComputeTotp()).Unwrap();
}

public class TestEnv : IDisposable
{
    private readonly Integration _test;

    public TestEnv(Integration test)
    {
        _test = test;
        
        _test.GetBob();
    }

    #pragma warning disable CA1816
    public void Dispose()
    #pragma warning restore CA1816
    {
        _test.DeleteBob();
    }
}


public class Integration
{
    public Integration(ITestOutputHelper output)
    {
        _output = output;
        var httpClientHandler = new HttpClientHandler();
        httpClientHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

        _sdk = new VeloSdk("127.0.0.1", 3069, new HttpClient(httpClientHandler));
        _totp = null;
    }
    private readonly ITestOutputHelper _output;

    private const string Username = "bob123";
    private const string BobsPassword = "Password1234!";
    private readonly VeloSdk _sdk;
    private TotpGen? _totp;
    private Token? _token;
    
    internal void GetBob()
    {
        var totp = _sdk.Signup().Start(username: Username)
            .MapFut(p => p.Password(Password.From(BobsPassword).Unwrap()))
            .MapFut(m => m.Totp())
            .Unwrap()
            .Result;

        _totp = new TotpGen(totp.ProvisioningUri);

        var guessRes = totp.Guess(_totp.Gen()).Result;

        if (guessRes.IsErr())
        {
            guessRes = guessRes.UnwrapErr().Guess(_totp.Gen()).Result;
        }

        _token = guessRes.Unwrap().Finish().Result;
    }

    internal void DeleteBob()
    {
        if (_token == null)
        {
            throw new NullException("Token cannot be null for deletion");
        }

        var _ =_sdk.DeleteUser().Delete(_token, Username).Unwrap().Result
            .CheckPassword(Password.From(BobsPassword).Unwrap()).Unwrap().Result
            .Confirm().Result;
    }

    private void LoginBob()
    {
        if (_totp == null)
        {
            throw new NullException("Cannot login to bob if no access to totp");
        }

        var totp = _sdk.Login().Start(Username, Password.From(BobsPassword).Unwrap()).Unwrap().Result
            .Totp().Unwrap().Result
            .Guess(_totp.Gen())
            .Result;

        if (totp.IsErr())
        {
            totp = totp.UnwrapErr().Totp().Unwrap().Result.Guess(_totp.Gen()).Result;
        }

        _token = totp.Unwrap();
    }

    [Fact]
    public void LoginBobTest()
    {
        using var _ = new TestEnv(this);
        LoginBob();
    }

    [Fact]
    public async void MigrateBob()
    {
        var totp = await _sdk.MigrateLogin()
            .Start("bob_with_no_mfa", Password.From(BobsPassword).Unwrap())
            .InspectErr(err => _output.WriteLine($"Error! {err}"))
            .MapFut(m => m.Totp())
            .ExpectWith(e => $"Did you forget to re init bob_with_no_mfa? Error Kind: {e}");

        var totpGen = new TotpGen(totp.ProvisioningUri);

        var final = await totp.Guess(totpGen.Gen()).Unwrap();

        var token = await final.Finish(); 
        var tokenRes = await _sdk.CheckToken(token);

        tokenRes.Unwrap();
    }
    
    /* Storing state on the server is no fun, and the way that the DFA is modeled above really does not feel nice.
       Thankfully, there is a serialization interface for states. Allowing for the server to remain mostly stateless.
       
       Below can be thought of as usage examples, and how I personally iterated on the API, making tweaks to what I 
       felt was annoying to use.
     */

    private async Task<StateOrToken> RunOneLogin(string state, string? otpGuess)
    {
        var de = JsonConvert.DeserializeObject<LoginState>(state);

        if (de == null)
        {
            throw new NullException("Failed to deserialize state");
        }

        var resume = _sdk.Login().Resume();

        return de.State switch
        {
            LoginStateE.InitMfa => await resume.InitMfa(de)
                .Totp()
                .MapOrElse(StateOrToken.SerLogin, StateOrToken.SerLogin),
            LoginStateE.RetryInitMfa => await resume.RetryInitMfa(de)
                .Totp()
                .MapOrElse(StateOrToken.SerLogin, StateOrToken.SerLogin),
            LoginStateE.VerifyOtp => await resume.VerifyOtp(de)
                .Guess(SimpleOtp.From(otpGuess!).Unwrap())
                .MapOrElse(StateOrToken.SerLogin, StateOrToken.T),
            LoginStateE.VerifyTotp => await resume.VerifyTotp(de)
                .Guess(Totp.From(otpGuess!).Unwrap())
                .MapOrElse(StateOrToken.SerLogin, StateOrToken.T),
            _ => throw new ArgumentOutOfRangeException(nameof(de.State), $"Unexpected state: {de.State}")
        };
    }

    private StateOrToken RunOneSignup(string state, string? input)
    {
        var de = JsonConvert.DeserializeObject<SignupState>(state);

        if (de == null)
        {
            throw new NullException("Failed to deserialize state");
        }

        var resume = _sdk.Signup().Resume();

        if (de.State == SignupStateE.SetupFirstMfa)
        {
            var r = resume.SetupFirstMfa(de).Totp().Result;
            
            // we need the provisioning uri. In actual use, you would return this to the user and display as a QR code 
            // for them to scan.
            _totp = new TotpGen(r.ProvisioningUri);
            
            return StateOrToken.SerSignup(r);
        }

        return de.State switch
        {
            SignupStateE.Password => StateOrToken.S(resume
                .Password(de).Password(Password.From(input!).Unwrap()).Result
                .Serialize()),
            SignupStateE.VerifyOtpSetup => resume.VerifyOtpSetup(de)
                .Guess(SimpleOtp.From(input!).Unwrap())
                .MapOrElse(StateOrToken.SerSignup, StateOrToken.SerSignup).Result,
            SignupStateE.VerifyTotpSetup => resume.VerifyTotpSetup(de)
                .Guess(Totp.From(input!).Unwrap())
                .MapOrElse(StateOrToken.SerSignup, StateOrToken.SerSignup).Result,
            SignupStateE.SetupMfaOrFinalize => StateOrToken.T(resume.SetupMfaOrFinalize(de).Finish().Result),
            _ => throw new ArgumentOutOfRangeException(nameof(de.State), $"Unexpected state: {de.State}")
        };
    }

    private StateOrToken RunOneMigrateLogin(string state, string? input)
    {
        var de = JsonConvert.DeserializeObject<MigrateLoginState>(state);

        if (de == null)
        {
            throw new NullException("Failed to deserialize state");
        }

        var resume = _sdk.MigrateLogin().Resume();

        if (de.State == MigrateLoginStateE.SetupFirstMfa)
        {
            var r = resume.SetupFirstMfa(de).Totp().Result;
            
            // we need the provisioning uri. In actual use, you would return this to the user and display as a QR code 
            // for them to scan.
            _totp = new TotpGen(r.ProvisioningUri);
            
            return StateOrToken.SerMLogin(r); 
        }

        return de.State switch
        {
            MigrateLoginStateE.VerifyOtpSetup => resume.VerifyOtpSetup(de).Guess(SimpleOtp.From(input!).Unwrap())
                .MapOrElse(StateOrToken.SerMLogin, StateOrToken.SerMLogin).Result,
            MigrateLoginStateE.VerifyTotpSetup => resume.VerifyTotpSetup(de).Guess(Totp.From(input!).Unwrap())
                .MapOrElse(StateOrToken.SerMLogin, StateOrToken.SerMLogin).Result,
            MigrateLoginStateE.NewMfaOrLogin => StateOrToken.T(resume.NewMfaOrLogin(de).Finish().Result),
            _ => throw new ArgumentOutOfRangeException(nameof(de.State), $"Unexpected state: {de.State}")
        };
    }

    private Token MultiPartSignupBob()
    {
        // we start our signup process, Bob knows what he wants his username to be
        var state = _sdk.Signup().Start(Username).Unwrap().Result.Serialize();
        
        // we returned the serialized state to the user, they just responded and were a good person not making any 
        // modifications (not that it matters, haha the IdP is not trash and our usage can never cause a vulnerability)
        
        // bob told us what he wants his password to be
        state = RunOneSignup(state, BobsPassword).State!;
        
        // we already knew bob wanted TOTP as he knows his security to some degree (his password is pretty bad tho)
        state = RunOneSignup(state, null).State!;
        
        // bob set up his authenticator app correctly, so he provided us the current TOTP
        state = RunOneSignup(state, _totp!.Gen().ToString()).State!;
        
        // bob is quite security oriented, he knows the issues with sim swapping, and he understands our IdP is more 
        // secure than whatever his email provider uses, so he finalizes the login getting his token.
        return RunOneSignup(state, null).Token!;
    }

    [Fact]
    public void MultiPartSignupBobTest()
    {
        var token = MultiPartSignupBob();
        
        // bob realized he didn't like anything about how he configured his account, so he deletes it
        var _ = _sdk.DeleteUser()
            .Delete(token, Username).Unwrap().Result
            .CheckPassword(Password.From(BobsPassword).Unwrap()).Unwrap().Result
            .Confirm().Result;
    }

    private Token MultiPartLoginBob()
    {
        // bob has returned and he would like to log in
        var state = _sdk.Login().Start(Username, Password.From(BobsPassword).Unwrap())
            .Unwrap()
            .Result
            .Serialize();
        
        // Bob only likes TOTP, and it also is all that he has previously set up. He communicates to the IdP that he 
        // would like to use TOTP.
        state = RunOneLogin(state, null).Result.State!;
        
        // Bob uses his authenticator app to provide the TOTP and gets his token
        return RunOneLogin(state, _totp!.Gen().ToString()).Result.Token!;
    }

    private Token MultiPartMigrateLoginBob()
    {
        var state = _sdk.MigrateLogin()
            .Start("bob2_with_no_mfa", Password.From(BobsPassword).Unwrap())
            .ExpectWith(e => $"Did you forget to re init bob_with_no_mfa? Error Kind: {e}")
            .Result
            .Serialize();

        // bob sets up TOTP
        state = RunOneMigrateLogin(state, null).State!;
        // bob verifies TOTP
        state = RunOneMigrateLogin(state, _totp!.Gen().ToString()).State!;
        // bob finishes his login
        return RunOneMigrateLogin(state, null).Token!;
    }

    [Fact]
    public void MultiPartLoginBobTest()
    {
        MultiPartSignupBob();

        var token = MultiPartLoginBob();
        
        // bob decides to delete his account
        var _ = _sdk.DeleteUser()
            .Delete(token, Username).Unwrap().Result
            .CheckPassword(Password.From(BobsPassword).Unwrap()).Unwrap().Result
            .Confirm().Result; 
    }

    [Fact]
    public async void MultiPartMigrateLoginBobTest()
    {
        var token = MultiPartMigrateLoginBob();
        token = await _sdk.CheckToken(token).Map(x => x.CurrentToken).Unwrap();
        token = await _sdk.CheckToken(token).Map(x => x.CurrentToken).Unwrap();
        
        var _ = await _sdk.CheckToken(token).Unwrap();
    }
}

public record StateOrToken(string? State, Token? Token)
{
    public readonly string? State = State;
    public readonly Token? Token = Token;

    public static StateOrToken S(string state) => new(state, null);
    public static StateOrToken SerLogin<TS>(TS v) where TS : IState<LoginState> => S(v.Serialize());
    public static StateOrToken SerSignup<TS>(TS v) where TS : IState<SignupState> => S(v.Serialize());
    public static StateOrToken SerMLogin<TS>(TS v) where TS : IState<MigrateLoginState> => S(v.Serialize());
    public static StateOrToken T(Token token) => new(null, token);
}