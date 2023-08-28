using System.Collections.Immutable;
using Akka.Actor;
using Akka.Event;
using Grpc.Net.ClientFactory;
using RaftCore.Common;
using RaftCore.Messages;
using RaftCore.Services;
using RaftCore.States;

namespace RaftCore.Actors;

public partial class RaftActor : FSM<NodeRole, NodeState>
{
    private const string VoteTimerName = "vote_timer";
    private const string AppendEntriesTimerName = "append_entries_timer";

    private readonly IActorRef _raftMessagingActorRef;
    private readonly ILoggingAdapter _logger = Context.GetLogger();
    private readonly int _voteTimeoutMinValue;
    private readonly int _voteTimeoutMaxValue;
    private readonly int _appendEntriesTimeoutMinValue;
    private readonly int _appendEntriesTimeoutMaxValue;
    private readonly Common.NodeInfo _currentNode;
    private readonly List<Common.NodeInfo> _clusterNodes;
    private readonly int _majority;

    public RaftActor(IClusterInfoService clusterInfoService, GrpcClientFactory grpcClientFactory)
    {
        _raftMessagingActorRef = Context.ActorOf(MessageBroadcastActor.Props(clusterInfoService, grpcClientFactory), "raft-message-broadcast-actor");
        _voteTimeoutMinValue = clusterInfoService.VoteTimeoutMinValue;
        _voteTimeoutMaxValue = clusterInfoService.VoteTimeoutMaxValue;
        _appendEntriesTimeoutMinValue = clusterInfoService.AppendEntriesTimeoutMinValue;
        _appendEntriesTimeoutMaxValue = clusterInfoService.AppendEntriesTimeoutMaxValue;
        _currentNode = clusterInfoService.CurrentNode;
        _clusterNodes = clusterInfoService.ClusterNodes;
        _majority = (int)Math.Ceiling((clusterInfoService.ClusterNodes.Count + 1) / (double)2);
        _logger.Info("STARTING RAFT ACTOR INITILIZATION.");

        StartWith(NodeRole.Follower, new NodeState());
        SetVoteTimer();

        When(NodeRole.Follower, FollowerBehaviour);
        When(NodeRole.Candidate, CandidateBehaviour);
        When(NodeRole.Leader, LeaderBehaviour);
        WhenUnhandled(CommonBehaviour);

        Initialize();
        LogInformation("RAFT ACTOR INITILIZATION FINISHED.");
    }

    protected override void PreStart()
    {
        LogInformation("STARTING RAFT ACTOR!!!");
        base.PreStart();
    }

    private TimeSpan CalculateNextVoteTimeout()
    {
        var random = new Random();
        var next = random.Next(_voteTimeoutMinValue, _voteTimeoutMaxValue);
        LogDebug($"Calculated next vote timeout: {next}.");
        return TimeSpan.FromMilliseconds(next);
    }

    private TimeSpan CalculateNextAppendEntriesTimeout()
    {
        var random = new Random();
        var next = random.Next(_appendEntriesTimeoutMinValue, _appendEntriesTimeoutMaxValue);
        LogDebug($"Calculated next append entries timeout: {next}.");
        return TimeSpan.FromMilliseconds(next);
    }

    private void SetVoteTimer() => SetTimer(VoteTimerName, VoteTimeout.Instance, CalculateNextVoteTimeout(), repeat: false);

    private void SetAppendEntriesTimer() => SetTimer(AppendEntriesTimerName, AppendEntriesTimeout.Instance, CalculateNextAppendEntriesTimeout(), repeat: false);

    private void LogDebug(string? message = null) => _logger.Debug(FormaLogMessage(message));

    private void LogInformation(string? message = null) => _logger.Info(FormaLogMessage(message));

    private void LogWarning(string? message = null) => _logger.Warning(FormaLogMessage(message));

    private string FormaLogMessage(string? message = null)
    {
        return message == null ? $"{{ ROLE: '{ StateName }'. { StateData } }}" : $"{{ ROLE: '{ StateName }'. { StateData } }} - MESSAGE: { message }";
    }
}