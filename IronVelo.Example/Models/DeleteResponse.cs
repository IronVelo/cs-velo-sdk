using IronVelo.Flows;
using IronVelo.Flows.Delete;

namespace IronVelo.Example.Models;

public record DeleteResponse(
    DeleteErrResponse? Error,
    DeleteState? State,
    bool DeleteScheduled 
)
{
    public static DeleteResponse FromErr<TE>(DeleteError<TE> err) where TE: notnull
        => new(
            new DeleteErrResponse(err.NewToken.Encode(), err.ToString() ?? "Error!"),
            null,
            false
        );

    public static DeleteResponse Err<TE>(string token, TE err) where TE: notnull
        => new(
            new DeleteErrResponse(token, err.ToString() ?? "Error!"),
            null,
            false
        );

    public static DeleteResponse Ok<TS>(TS state) where TS : IState<DeleteState>
        => new(null, state.GetState(), false);

    public static DeleteResponse Finish() => new(null, null, true);
}

public record DeleteErrResponse(
    string Token,
    string Msg
);