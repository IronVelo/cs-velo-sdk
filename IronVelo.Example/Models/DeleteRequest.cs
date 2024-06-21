using IronVelo.Flows;

namespace IronVelo.Example.Models;

public record DeleteRequest(
    string? Password,
    DeleteState State
);

public record DeleteIngressRequest(
    string Token,
    string Username
);