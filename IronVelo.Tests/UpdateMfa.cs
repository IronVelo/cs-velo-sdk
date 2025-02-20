using System.Web;
using IronVelo.Flows;
using IronVelo.Types;
using Newtonsoft.Json;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace IronVelo.Tests.UpdateMfa;

public class UpdateMfa 
{
    public UpdateMfa(ITestOutputHelper output)
    {
        _output = output;
        _sdk = Helpers.GetSdk();
        _totp = null;
        var totp = new TotpGen(new OtpNet.Totp(
            _totp_secret, 
            mode: OtpNet.OtpHashMode.Sha256,
            totpSize: 8,
            step: 30
        ));
        _totp = totp;
    }
    
    private const string Username = "dummy_bill";
    private const string Pass = "Password1234!";
    private static readonly byte[] _totp_secret = new byte[32] {
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 
        18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32
    };
    private readonly ITestOutputHelper _output;
    private readonly VeloSdk _sdk;
    private TotpGen? _totp;
    private Token? _token;

    private async Task LoginBill() 
    {
        var res = await _sdk
            .Login()
            .Start(Username, Password.From(Pass).Unwrap())
            .MapErr(err => $"Failed to initiate login flow: {err}")
            .BindFut(state => state
                .Totp()
                .MapErr(
                    err => $"User did not have TOTP enabled (due to test before it removing it: {err}"
                )
            )
            .BindFut(state => state
                .Guess(_totp.Gen())
                .MapErr(err => $"TOTP guess was incorrect: {err}")
            );
        _token = res.Unwrap();
    }
    
    [Fact]
    public async Task SmokeRemove() 
    {
        await LoginBill();
        
        var hello = await _sdk
            .UpdateMfa()
            .Start(_token!);
        
        var totp = await hello.State
            .Totp()
            .MapFut(state => state.Guess(_totp.Gen()).ExpectWith(err => $"TOTP Guess Err {err}"))
            .ExpectWith(err => $"Check TOTP Err {err}");
        
        var our_token = await totp
            .Remove()
            // We will avoid removing the TOTP. This is a dummy user I injected into
            // my test enviornment.
            .Email()
            .MapFut(state => state.Finalize(hello.Token))
            .ExpectWith(err => $"Remove TOTP Err {err}");
        
        _token = our_token;
    }

    [Fact]
    public async Task UpdateTotpMfa() 
    {
        await LoginBill();
        
        var hello = await _sdk
            .UpdateMfa()
            .Start(_token!);
        
        var totp = await hello.State
            .Totp()
            .MapFut(state => state.Guess(_totp.Gen()).ExpectWith(err => $"TOTP Guess Err {err}"))
            .ExpectWith(err => $"Check TOTP Err {err}");

        var updating = await totp
            .Update()
            .Totp("Auth App DisplayName");

        var totp_gen = new TotpGen(updating.ProvisioningUri);

        var our_token = await updating
            .Guess(totp_gen.Gen())
            .MapErr(err => $"Incorrect TOTP guess: {err}")
            .MapFut(state => state.Finalize(hello.Token))
            .Unwrap();
        
        _totp = totp_gen;
        _token = our_token.Token;
    }
}
