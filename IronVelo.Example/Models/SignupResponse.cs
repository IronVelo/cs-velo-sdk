using IronVelo.Flows;
using IronVelo.Types;

namespace IronVelo.Example.Models;

public record SignupResponse(
    SignupState? State,
    SignupState? ErrState,
    string? ErrMsg,
    string? Secret
);

public static class SignupResHelpers
{
    public static SignupResponse FErr<TE>(TE err) 
        => new(null, null, err != null ? err.ToString() : "Error!", null);
    public static SignupResponse ErrState<TS>(TS errState) where TS : IState<SignupState> =>
        new(null, errState.GetState(), null, null);
    public static SignupResponse Success<TS>(TS state) where TS : IState<SignupState> 
        => new(state.GetState(), null, null, null);
    public static SignupResponse Token(Token token) => new(null, null, null, token.Encode());
    public static SignupResponse PUri(Flows.Signup.VerifyTotpSetup state) => new(
        state.GetState(), null, null, state.ProvisioningUri
    );
}