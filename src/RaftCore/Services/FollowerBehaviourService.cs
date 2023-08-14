using System.Timers;
using Microsoft.Extensions.Logging;
using RaftCore.Common;

namespace RaftCore.Services;

public class FollowerBehaviourService : INodeRoleBehaviourService
{   
    int _voteTimeoutMinValue { get; }

    int _voteTimeoutMaxValue { get; }

    private readonly ILogger<FollowerBehaviourService> _logger;

    public FollowerBehaviourService(IClusterInfoService clusterInfoService, ILogger<FollowerBehaviourService> logger)
    {
        _voteTimeoutMinValue = clusterInfoService.VoteTimeoutMinValue;
        _voteTimeoutMaxValue = clusterInfoService.VoteTimeoutMaxValue;
        _logger = logger;
    }

    public NodeRole NodeRole => NodeRole.Follower;

    public event Action<NodeRole> BehaviourChanged;

    public void Select()
    {
        var random = new Random();
        var randomVoteTimeout = random.Next(_voteTimeoutMinValue, _voteTimeoutMaxValue);
        var voteTimer = new System.Timers.Timer(randomVoteTimeout);
        _logger.LogInformation($"Starting vote timer with the value: '{ randomVoteTimeout }'.");
        voteTimer.Enabled = true;
        voteTimer.AutoReset = false;

        voteTimer.Elapsed += SwitchToCandidate;
    }

    public void SwitchToCandidate(object? sender, ElapsedEventArgs e)
    {
        _logger.LogInformation($"Request to switch node to '{ NodeRole.Candidate }'.");
        BehaviourChanged(NodeRole.Candidate);
    }
}