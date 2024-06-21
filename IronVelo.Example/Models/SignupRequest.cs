using IronVelo.Flows;

namespace IronVelo.Example.Models;

public record SignupRequest(
    string? Contact,
    string? Secret,
    string? MfaKind,
    SignupState State
);