using IronVelo.Flows;
using IronVelo.Flows.Ticket;
using IronVelo.Types;

namespace IronVelo.Example.Models;

public record TicketResponse(
    TicketState? State,
    TicketState? ErrState,
    string? ErrMsg,
    string? Token,
    string? ProvisioningUri,
    TicketIssuanceInfo? IssuanceInfo
);

public record TicketIssuanceInfo(
    string Ticket,
    long ExpiresAt
);

public static class TicketResHelpers
{
    public static TicketResponse FErr<TE>(TE err) 
        => new(null, null, err?.ToString() ?? "Error!", null, null, null);
    
    public static TicketResponse ErrState<TS>(TS errState) where TS : IState<TicketState> =>
        new(null, errState.GetState(), null, null, null, null);
    
    public static TicketResponse Success<TS>(TS state) where TS : IState<TicketState> 
        => new(state.GetState(), null, null, null, null, null);
    
    public static TicketResponse Token(Token token) => 
        new(null, null, null, token.Encode(), null, null);
    
    public static TicketResponse PUri(VerifyTotpSetup state) => 
        new(state.GetState(), null, null, null, state.ProvisioningUri, null);
    
    public static TicketResponse TicketIssued(TicketIssuanceResult result) => 
        new(null, null, null, null, null, new TicketIssuanceInfo(result.Ticket.Encode(), result.ExpiresAt));
}
