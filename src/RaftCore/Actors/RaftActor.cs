using Akka.Actor;
using Akka.Event;
using Grpc.Net.ClientFactory;
using RaftCore.Common;
using RaftCore.Services;

namespace RaftCore.Actors;

public class RaftActor : FSM<NodeRole, NodeStateService>
{
    private const string VoteTimerName = "vote_timer";
    private const string AppendEntriesTimerName = "append_entries_timer";

    private readonly ILoggingAdapter _logger = Context.GetLogger();
    private readonly int _voteTimeoutMinValue;
    private readonly int _voteTimeoutMaxValue;
    private readonly int _appendEntriesTimeoutMinValue;
    private readonly int _appendEntriesTimeoutMaxValue;
    private readonly NodeInfo _currentNode;
    private readonly List<NodeInfo> _clusterNodes;
    private readonly GrpcClientFactory _grpcClientFactory;
    private readonly int _majority;

    public RaftActor(IClusterInfoService clusterInfoService, NodeStateService nodeStateService, GrpcClientFactory grpcClientFactory)
    {
        clusterInfoService.ResolveNodesDnsAsync().Wait();
        _voteTimeoutMinValue = clusterInfoService.VoteTimeoutMinValue;
        _voteTimeoutMaxValue = clusterInfoService.VoteTimeoutMaxValue;
        _appendEntriesTimeoutMinValue = clusterInfoService.AppendEntriesTimeoutMinValue;
        _appendEntriesTimeoutMaxValue = clusterInfoService.AppendEntriesTimeoutMaxValue;
        _currentNode = clusterInfoService.CurrentNode;
        _clusterNodes = clusterInfoService.ClusterNodes;
        _grpcClientFactory = grpcClientFactory;
        _majority = (int)Math.Ceiling((clusterInfoService.ClusterNodes.Count + 1) / (double)2);

        StartWith(NodeRole.Follower, nodeStateService);
        SetVoteTimer();

        When(NodeRole.Follower, state => 
        {
            if (state.FsmEvent is StateTimeout)
            {
                LogInformation($"Vote timer elapsed. Switching to candidate.");
                // Trigger timeout again to start requesting votes.
                return GoTo(NodeRole.Candidate).Replying(StateTimeout.Instance);  
            }      

            return null;
        });

        When(NodeRole.Candidate, state => 
        {
            if (state.FsmEvent is StateTimeout)
            {
                LogInformation("Starting requesting votes.");
                StateData.IncrementTerm();
                StateData.Vote(_currentNode.NodeId);
                StateData.AddVote(_currentNode.NodeId);
                SetVoteTimer();
                var (lastLogIndedx, lastLogTerm) = StateData.GetLastLogInfo();
                foreach (var nodeClient in GetNodesClients())
                {
                    LogInformation($"Requesting vote from '{ nodeClient.Item1 }'.");
                    nodeClient.Item2.SendVoteRequest(new VoteRequest() { Term = StateData.CurrentTerm, CandidateId = _currentNode.NodeId, LastLogIndex = lastLogIndedx, LastLogTerm = lastLogTerm });
                }
                LogInformation("Finished requesting votes.");
                return Stay();  
            }

            if (state.FsmEvent is VoteResponse voteResponse)
            {
                LogInformation($"Received vote response from '{ voteResponse.NodeId }'. Term: '{ voteResponse.Term }'. Granted: '{ voteResponse.VoteGranted }'.");
                if (voteResponse.Term > StateData.CurrentTerm)
                {
                    LogInformation($"Got higher term '{ voteResponse.Term }'. Downgrading to follower.");
                    StateData.CurrentTerm = voteResponse.Term;
                    StateData.Vote(null);
                    SetVoteTimer();
                    return GoTo(NodeRole.Follower);
                }

                if (voteResponse.Term == StateData.CurrentTerm && voteResponse.VoteGranted)
                {
                    StateData.AddVote(voteResponse.NodeId);
                    LogInformation($"Vote received from '{ voteResponse.NodeId }'. Total votes collected '{ StateData.VotesCount }'.");
                    if (StateData.VotesCount >= _majority)
                    {
                        CancelTimer(VoteTimerName);
                        StateData.CurrentLeader = _currentNode.NodeId;

                        // Reset ack.
                        // Reset sendLengh.
                        // Replicate log.
                        LogInformation($"Majority votes collected. Becoming leader!");
                        Self.Tell(StateTimeout.Instance);
                        return GoTo(NodeRole.Leader);
                    }
                }

                return Stay();
            }      

            return null;
        });

        When(NodeRole.Leader, (state) =>
        {
            if (state.FsmEvent is StateTimeout)
            {
                LogDebug($"Sending append entries request to nodes.");
                SetAppendEntriesTimer();
                foreach (var nodeClient in GetNodesClients())
                {
                    LogDebug($"Sending append entries request to '{ nodeClient.Item1 }'.");
                    nodeClient.Item2.SendAppendEntriesRequest(new AppendEntriesRequest() { Term = StateData.CurrentTerm, LeaderId = _currentNode.NodeId });
                }                
                return Stay();
            }

            if (state.FsmEvent is AppendEntriesResponse appendEntriesResponse)
            {
                LogDebug($"Received append entries response from '{ appendEntriesResponse.NodeId }'. Term: '{ appendEntriesResponse.Term }'. Success: '{ appendEntriesResponse.Success }'.");
                if (appendEntriesResponse.Term > StateData.CurrentTerm)
                {
                    LogInformation($"Got higher term '{ appendEntriesResponse.Term }'. Downgrading to follower.");
                    StateData.CurrentTerm = appendEntriesResponse.Term;
                    StateData.Vote(null);
                    SetVoteTimer();
                    return GoTo(NodeRole.Follower);
                }

                return Stay();
            }

            return null;
        });

        WhenUnhandled(state => 
        {
            if (state.FsmEvent is VoteRequest newTermVoteRequest && newTermVoteRequest.Term > StateData.CurrentTerm)
            {
                LogInformation($"Got higher term '{ newTermVoteRequest.Term }' from '{ newTermVoteRequest.CandidateId }' on vote request. Downgrading to follower.");
                StateData.CurrentTerm = newTermVoteRequest.Term;
                Self.Tell(state.FsmEvent);
                return GoTo(NodeRole.Follower);
            }

            if (state.FsmEvent is AppendEntriesRequest newTermAppendEntriesRequest && newTermAppendEntriesRequest.Term > StateData.CurrentTerm)
            {
                LogInformation($"Got higher term '{ newTermAppendEntriesRequest.Term }' from leader '{ newTermAppendEntriesRequest.LeaderId }' on append entries request. Downgrading to follower.");
                StateData.CurrentTerm = newTermAppendEntriesRequest.Term;
                // ???
                StateData.Vote(null);
                StateData.ClearVotes();
                Self.Tell(state.FsmEvent);
                return GoTo(NodeRole.Follower);
            }

            if (state.FsmEvent is VoteRequest voteRequest)
            {   
                var (lastLogIndedx, lastLogTerm) = StateData.GetLastLogInfo();
                var logOk = voteRequest.LastLogTerm > lastLogTerm || (voteRequest.LastLogTerm == lastLogTerm && voteRequest.LastLogIndex >= lastLogIndedx);
                var canVoteForCandidate = StateData.CanVoteFor(voteRequest.CandidateId);
                LogInformation($"'{ voteRequest.CandidateId }' asks for a vote in the term '{ voteRequest.Term }'. IsLogOk: { logOk }. CanVote: { canVoteForCandidate }.");

                var node = _clusterNodes.FirstOrDefault(n => n.NodeId == voteRequest.CandidateId);
                var client = grpcClientFactory.CreateClient<RaftMessagingService.RaftMessagingServiceClient>(node.HostName);
                if (voteRequest.Term == StateData.CurrentTerm && logOk && canVoteForCandidate)
                {   
                    // Only the follower can vote.
                    // Vote for node only if its log is up to date.
                    // Vote for node only in the same term.
                    // Can vote only if it's the same node that follower has already voted in the current term or follower hasn't voted yet.
                    LogInformation($"Voting for candidate '{ voteRequest.CandidateId }' in term '{ voteRequest.Term }'."); 
                    StateData.Vote(voteRequest.CandidateId);
                    SetVoteTimer();
                    client.SendVoteResponse(new VoteResponse() { Term = voteRequest.Term, NodeId = _currentNode.NodeId,  VoteGranted = true });
                    return Stay();
                }
                else
                {
                    // Candidate and leader nodes couldn't grant vote in the current term for other node as they have definitely already voted for itself in the current term.
                    // Decline any request with the term less than current term.
                     LogInformation($"Declining vote request from candidate '{ voteRequest.CandidateId }' in term '{ voteRequest.Term }'."); 
                    client.SendVoteResponse(new VoteResponse() { Term = voteRequest.Term, NodeId = _currentNode.NodeId, VoteGranted = false }); 
                    return Stay();
                }
                    
            }
            
            if (state.FsmEvent is AppendEntriesRequest appendEntriesRequest)
            {
                LogDebug($"Got append entries request from leader '{ appendEntriesRequest.LeaderId }' with term '{ appendEntriesRequest.Term }'.");
                SetVoteTimer();
                StateData.CurrentLeader = appendEntriesRequest.LeaderId;
                var node = _clusterNodes.FirstOrDefault(n => n.NodeId == appendEntriesRequest.LeaderId);
                var client = grpcClientFactory.CreateClient<RaftMessagingService.RaftMessagingServiceClient>(node.HostName);
                client.SendAppendEntriesResponse(new AppendEntriesResponse(){ Term = StateData.CurrentTerm, NodeId = _currentNode.NodeId, Success = true });
                return GoTo(NodeRole.Follower);
            }

            LogWarning($"Actor couldn't handle the message: '{ state.FsmEvent }'. Type: { state.FsmEvent.GetType() }.");
            return Stay();
        });

        Initialize();
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

    private void SetVoteTimer() => SetTimer(VoteTimerName, StateTimeout.Instance, CalculateNextVoteTimeout(), repeat: false);

    private void SetAppendEntriesTimer() => SetTimer(AppendEntriesTimerName, StateTimeout.Instance, CalculateNextAppendEntriesTimeout(), repeat: false);

    private IEnumerable<(string, RaftMessagingService.RaftMessagingServiceClient)> GetNodesClients()
    {
        foreach (var node in _clusterNodes)
        {
            yield return (node.HostName, _grpcClientFactory.CreateClient<RaftMessagingService.RaftMessagingServiceClient>(node.HostName));
        }
    }

    private void LogDebug(string? message = null) => _logger.Debug(FormaLogMessage(message));

    private void LogInformation(string? message = null) => _logger.Info(FormaLogMessage(message));

    private void LogWarning(string? message = null) => _logger.Warning(FormaLogMessage(message));

    private string FormaLogMessage(string? message = null)
    {
        return message == null ? $"{{ ROLE: '{ StateName }'. { StateData } }}" : $"{{ ROLE: '{ StateName }'. { StateData } }} - MESSAGE: { message }";
    }
}