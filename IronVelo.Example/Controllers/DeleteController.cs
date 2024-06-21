using IronVelo.Example.Models;
using IronVelo.Flows;
using IronVelo.Flows.Delete;
using IronVelo.Types;
using Microsoft.AspNetCore.Mvc;
using MustUse;

namespace IronVelo.Example.Controllers;

[ApiController]
[Route("auth/delete/[controller]")]
[Produces("application/json")]
public class DeleteController : ControllerBase
{
    private readonly VeloSdk _idpService;
    private readonly ILogger<DeleteController> _logger;

    public DeleteController(VeloSdk idpService, ILogger<DeleteController> logger)
    {
        _idpService = idpService;
        _logger = logger;
    }

    private IActionResult ResFromErr<TE>(DeleteError<TE> delError) where TE: notnull
        => BadRequest(DeleteResponse.FromErr(delError));

    private IActionResult ResErr<TE>(string token, TE err) where TE : notnull
        => BadRequest(DeleteResponse.Err(token, err));

    private IActionResult ResOkState<TS>(TS state) where TS : IState<DeleteState>
        => Ok(DeleteResponse.Ok(state));

    [ProducesResponseType(typeof(DeleteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(DeleteResponse), StatusCodes.Status400BadRequest)]
    [HttpPost("ask")]
    public Task<IActionResult> AskDelete([FromBody] DeleteIngressRequest request)
    {
        return Token.TryDecode(request.Token)
            .InspectErr(e => _logger.LogError("Invalid token provided, {Username}: {Error}", request.Username, e))
            .MapErr(e => ResErr(request.Token, e))
            .BindFut(token => _idpService.DeleteUser().Delete(token, request.Username).MapErr(ResFromErr))
            .MapOrElse(e => e, ResOkState);
    }
    
    [ProducesResponseType(typeof(DeleteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(DeleteResponse), StatusCodes.Status400BadRequest)]
    [HttpPost] 
    public Task<IActionResult> Delete([FromBody] DeleteRequest request)
    {
        return request.State.State switch
        {
            DeleteStateE.ConfirmPassword => ConfirmPasswordHandler(request),
            DeleteStateE.ConfirmDeletion => ConfirmDeletionHandler(request),
            _ => throw new Exception("Impossibility! DeleteStateE is exhaustive!")
        };
    }

    [MustUse]
    public Task<IActionResult> ConfirmPasswordHandler(DeleteRequest request)
    {
        if (request.Password is not { } rawPassword)
        {
            return Task.FromResult(ResErr(request.State.Token, "You must provide a password to confirm it!"));
        }
        
        var resumed = _idpService.DeleteUser().Resume().ConfirmPassword(request.State);

        return Password.From(rawPassword)
            .MapErr(e => ResErr(request.State.Token, e))
            .BindFut(pass => resumed.CheckPassword(pass).MapErr(ResFromErr))
            .MapOrElse(err => err, ResOkState);
    }

    [MustUse]
    public Task<IActionResult> ConfirmDeletionHandler(DeleteRequest request)
    {
        return _idpService.DeleteUser().Resume().ConfirmDeletion(request.State).Confirm()
            .ContinueWith(task => {
                Utils.Resolve.Get(task);
                return (IActionResult)Ok(DeleteResponse.Finish());
            });
    }
}