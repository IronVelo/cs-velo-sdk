using IronVelo.Flows;
using IronVelo.Flows.Ticket;

namespace IronVelo.Example.Models;

public record TicketRequest(
    string? Password,
    string? Token,
    MfaSetupInfo? MfaSetup,
    TicketState State
);

public record IssueTicketRequest(
    string Token,
    string TargetUsername,
    TicketKind Kind,
    string Reason
);

public record RedeemTicketRequest(
    string Ticket,
    TicketOperation Operation
);

public record MfaSetupInfo(
    string MfaKind,
    string? Contact,
    string? Otp,
    bool IsVerification
);
