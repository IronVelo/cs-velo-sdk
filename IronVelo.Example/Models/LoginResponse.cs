using IronVelo.Flows;
using IronVelo.Types;

namespace IronVelo.Example.Models;

public record LoginResponse(
    LoginState? State,
    LoginState? ErrState,
    string? ErrMsg,
    string? Token
);

public static class LoginResHelpers
{
    public static LoginResponse FErr<TE>(TE err) 
        => new(null, null, err != null ? err.ToString() : "Error!", null);
    public static LoginResponse ErrState<TS>(TS errState) where TS : IState<LoginState> =>
        new(null, errState.GetState(), null, null);
    public static LoginResponse Success<TS>(TS state) where TS : IState<LoginState> 
        => new(state.GetState(), null, null, null);
    public static LoginResponse Token(Token token) => new(null, null, null, token.Encode());
}