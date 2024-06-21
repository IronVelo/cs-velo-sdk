using IronVelo.Flows;

namespace IronVelo.Example.Models;

public record LoginRequest(
    string? OtpGuess,
    string? MfaKind,
    LoginState State
);