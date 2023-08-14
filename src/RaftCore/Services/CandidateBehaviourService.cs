using System.Timers;
using Microsoft.Extensions.Logging;
using RaftCore.Common;

namespace RaftCore.Services;

public class CandidateBehaviourService : INodeRoleBehaviourService
{
    int _voteTimeoutMinValue { get; }

    int _voteTimeoutMaxValue { get; }

    private readonly ILogger<CandidateBehaviourService> _logger;

    public CandidateBehaviourService(IClusterInfoService clusterInfoService, ILogger<CandidateBehaviourService> logger)
    {
        _voteTimeoutMinValue = clusterInfoService.VoteTimeoutMinValue;
        _voteTimeoutMaxValue = clusterInfoService.VoteTimeoutMaxValue;
        _logger = logger;
    }

    public NodeRole NodeRole => NodeRole.Candidate;

    public event Action<NodeRole> BehaviourChanged;

    public void Select()
    {
        var random = new Random();
        var randomVoteTimeout = random.Next(_voteTimeoutMinValue, _voteTimeoutMaxValue);
        var voteTimer = new System.Timers.Timer(randomVoteTimeout);
        _logger.LogInformation($"Starting vote timer with the value: '{ randomVoteTimeout }'.");
        voteTimer.AutoReset = true;
        voteTimer.Enabled = true;
        
        voteTimer.Elapsed += SendVoteRequests;
    }

    public void SendVoteRequests(object? sender, ElapsedEventArgs e)
    {
        _logger.LogInformation("Sending Vote request!");
    }
}