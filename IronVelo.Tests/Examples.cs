using IronVelo.Types;

namespace IronVelo.Tests;

public class Examples
{
    // we don't need to actually run these, Integration is for that, we leverage a CbC paradigm so if it builds it most
    // likely works. This is all just to sanity check our examples and ensure we are not being misleading.
    public readonly VeloSdk Sdk;
    public const string Username = "username";
    public readonly Password Pass;
    public readonly Totp Guess;
    public readonly Token? Token;

    public Examples(VeloSdk sdk)
    {
        Sdk = sdk;
        Pass = Password.From("APassword123!").Unwrap();
        Guess = Totp.From("12345678").Unwrap();
        Token = null;
    }

    private async void ReadMeSignup()
    {
        Token token = await Sdk.Signup()
            // request the username
            .Start(Username).MapErr(e => e.ToString())
            // set the password
            .MapFut(s => s.Password(Pass))
            // select TOTP as the MFA kind (options: Totp, Sms, Email)
            .MapFut(s => s.Totp())
            // have the user guess, failure results in a retry state
            .BindFut(s => s.Guess(Guess).MapErr(retry => retry.Serialize()))
            // the user only wants TOTP, so they finish the process
            .MapFut(s => s.Finish())
            // If any of the states failed, throw
            .Unwrap();
    }

    private async void ReadMeLogin()
    {
        Token token = await Sdk.Login()
            // Attempt to initiate the flow with username and password
            .Start(Username, Pass).MapErr(err => err.ToString())
            // Select the MfaKind the user would like to use (available kinds are accessible from the state)
            .BindFut(s => s.LeftV!.Totp().MapErr(selectAgain => selectAgain.Serialize(/* user did not set TOTP up */)))
            .BindFut(s => s.Guess(Guess).MapErr(selectAgain => selectAgain.Serialize(/* wrong! */)))
            // If any of the states failed, throw
            .Unwrap();
    }

    private async void ReadMeDelete()
    {
        var _ = await Sdk.DeleteUser()
            // Request deletion, providing the user's token and asking them to confirm their username.
            .Delete(Token!, Username).MapErr(err => err.Reason.ToString())
            // For added assurance that this is a legitimate request, confirm they know their password
            .BindFut(s => s.CheckPassword(Pass).MapErr(err => err.Reason.ToString()))
            // Finally, schedule the account's deletion
            .MapFut(s => s.Confirm())
            // If any of the states failed, throw
            .Unwrap();
    }

    private async void ReadMeMigrateLogin()
    {
        Token token = await Sdk.MigrateLogin() 
            // Initiate the migrating login flow
            .Start(Username, Pass)
            // If there was an error, it could have been due to WrongFlow, the LoginError will tell you this.
            .MapErr(err => err.ToString())
            // Now, the user is prompted to select an MFA kind they want to set up, select TOTP
            // This provides the provisioning Uri to use in an authenticator app
            .MapFut(s => s.Totp()) 
            // Confirm that TOTP was successfully setup by having the user verify the code, error means retry
            .BindFut(s => s.Guess(Guess).MapErr(err => err.Serialize()))
            // Either setup another MFA kind or finish the login. The user must use the default flow going forward.
            .MapFut(s => s.Finish())
            // If any of the states failed, throw
            .Unwrap();
    }
}