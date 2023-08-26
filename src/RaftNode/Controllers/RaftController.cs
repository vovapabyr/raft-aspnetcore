using Akka.Actor;
using Akka.Hosting;
using Microsoft.AspNetCore.Mvc;
using RaftCore.Actors;
using RaftCore.Messages;

namespace RaftNode.Controllers;

[ApiController]
[Route("[controller]")]
public class RaftController : ControllerBase
{
    private readonly ActorSystem _actorSystem;
    private IRequiredActor<RaftActor> _raftARefProvider;
    private readonly ILogger<RaftController> _logger;

    public RaftController(IRequiredActor<RaftActor> raftARefProvider, ActorSystem actorSystem, ILogger<RaftController> logger)
    {
        _actorSystem = actorSystem;
        _raftARefProvider = raftARefProvider;
        _logger = logger;
    }

    [HttpPost("command")]
    public async Task<IActionResult> AddNewCommand(string command)
    {
        var inbox = Inbox.Create(_actorSystem);
        _logger.LogInformation($"Trying to add new command: '{ command }'.");
        inbox.Send(_raftARefProvider.ActorRef, new AddNewCommand(command));

        // Fails on timeout = TimeSpan.MaxValue, because of _system.Scheduler.MonotonicClock + timeout (Inbox.cs 556line)
        var response = await inbox.ReceiveAsync(TimeSpan.MaxValue.Add(-TimeSpan.FromDays(1)));
        _logger.LogInformation($"Got response to '{ command }'. Response: '{ response.GetType() }'.");
        if (response is CommandSuccessfullyCommited acknowledgment)
            return Ok(acknowledgment);

        if (response is RedirectToLeader redirectToLeader)
            return BadRequest(redirectToLeader);

        _logger.LogWarning($"Failed to add command '{ command }'. Response: '{ response }'.");
        return BadRequest("Unknown error.");
    }
}