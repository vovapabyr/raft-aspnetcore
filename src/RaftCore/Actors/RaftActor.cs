using Akka.Actor;
using Akka.Event;
using Grpc.Net.ClientFactory;
using RaftCore.Common;
using RaftCore.Services;

namespace RaftCore.Actors;

public class RaftActor : FSM<NodeRole, NodeState>
{
    private const string VoteTimerName = "vote_timer";
    private const string AppendEntriesTimerName = "append_entries_timer";

    private readonly IActorRef _raftMessagingActorRef;
    private readonly ILoggingAdapter _logger = Context.GetLogger();
    private readonly int _voteTimeoutMinValue;
    private readonly int _voteTimeoutMaxValue;
    private readonly int _appendEntriesTimeoutMinValue;
    private readonly int _appendEntriesTimeoutMaxValue;
    private readonly NodeInfo _currentNode;
    private readonly int _majority;

    public RaftActor(IClusterInfoService clusterInfoService, NodeState nodeStateService, GrpcClientFactory grpcClientFactory)
    {
        _raftMessagingActorRef = Context.ActorOf(MessageBroadcastActor.Props(clusterInfoService, grpcClientFactory), "raft-message-broadcast-actor");
        _voteTimeoutMinValue = clusterInfoService.VoteTimeoutMinValue;
        _voteTimeoutMaxValue = clusterInfoService.VoteTimeoutMaxValue;
        _appendEntriesTimeoutMinValue = clusterInfoService.AppendEntriesTimeoutMinValue;
        _appendEntriesTimeoutMaxValue = clusterInfoService.AppendEntriesTimeoutMaxValue;
        _currentNode = clusterInfoService.CurrentNode;
        _majority = (int)Math.Ceiling((clusterInfoService.ClusterNodes.Count + 1) / (double)2);
        _logger.Info("STARTING RAFT ACTOR INITILIZATION.");

        StartWith(NodeRole.Follower, new NodeState());
        SetVoteTimer();

        When(NodeRole.Follower, state => 
        {
            if (state.FsmEvent is StateTimeout && state.StateData is NodeState stateDataTimeout)
            {
                LogInformation($"Vote timer elapsed. Switching to candidate.");
                // Trigger timeout again to start requesting votes.
                return GoTo(NodeRole.Candidate).Using(stateDataTimeout.CopyAsCandidate()).Replying(StateTimeout.Instance);  
            }      

            return null;
        });

        When(NodeRole.Candidate, state => 
        {
            if (state.FsmEvent is StateTimeout && state.StateData is CandidateNodeState stateDataTimeout)
            {
                LogInformation("Starting requesting votes.");
                stateDataTimeout.IncrementTerm();
                stateDataTimeout.Vote(_currentNode.NodeId);
                stateDataTimeout.AddVote(_currentNode.NodeId);
                SetVoteTimer();
                var (lastLogIndedx, lastLogTerm) = stateDataTimeout.GetLastLogInfo();
                _raftMessagingActorRef.Tell(new VoteRequest() { Term = stateDataTimeout.CurrentTerm, CandidateId = _currentNode.NodeId, LastLogIndex = lastLogIndedx, LastLogTerm = lastLogTerm });
                LogInformation("Finished requesting votes.");
                return Stay().Using(stateDataTimeout.CopyAsCandidate());  
            }

            if (state.FsmEvent is VoteResponse voteResponse && state.StateData is CandidateNodeState stateDataVoteResponse)
            {
                LogInformation($"Received vote response from '{ voteResponse.NodeId }'. Term: '{ voteResponse.Term }'. Granted: '{ voteResponse.VoteGranted }'.");
                if (voteResponse.Term > stateDataVoteResponse.CurrentTerm)
                {
                    LogInformation($"Got higher term '{ voteResponse.Term }'. Downgrading to follower.");
                    stateDataVoteResponse.CurrentTerm = voteResponse.Term;
                    stateDataVoteResponse.Vote(null);
                    SetVoteTimer();
                    return GoTo(NodeRole.Follower).Using(stateDataVoteResponse.CopyAsBase());
                }

                if (voteResponse.Term == stateDataVoteResponse.CurrentTerm && voteResponse.VoteGranted)
                {
                    stateDataVoteResponse.AddVote(voteResponse.NodeId);
                    LogInformation($"Vote received from '{ voteResponse.NodeId }'. Total votes collected '{ stateDataVoteResponse.VotesCount }'.");
                    if (stateDataVoteResponse.VotesCount >= _majority)
                    {
                        CancelTimer(VoteTimerName);
                        stateDataVoteResponse.CurrentLeader = _currentNode.NodeId;

                        // Reset ack.
                        // Reset sendLengh.
                        // Replicate log.
                        LogInformation($"Majority votes collected. Becoming leader!");
                        Self.Tell(StateTimeout.Instance);
                        return GoTo(NodeRole.Leader).Using(stateDataVoteResponse.CopyAsBase());
                    }
                }

                return Stay().Using(stateDataVoteResponse.CopyAsCandidate());
            }      

            return null;
        });

        When(NodeRole.Leader, (state) =>
        {
            if (state.FsmEvent is StateTimeout && state.StateData is NodeState stateDataTimeout)
            {
                LogDebug($"Sending append entries request to nodes.");
                SetAppendEntriesTimer();
                _raftMessagingActorRef.Tell(new AppendEntriesRequest() { Term = stateDataTimeout.CurrentTerm, LeaderId = _currentNode.NodeId });             
                return Stay().Using(stateDataTimeout.CopyAsBase());
            }

            if (state.FsmEvent is AppendEntriesResponse appendEntriesResponse && state.StateData is NodeState stateDataAppendResponse)
            {
                LogDebug($"Received append entries response from '{ appendEntriesResponse.NodeId }'. Term: '{ appendEntriesResponse.Term }'. Success: '{ appendEntriesResponse.Success }'.");
                if (appendEntriesResponse.Term > stateDataAppendResponse.CurrentTerm)
                {
                    LogInformation($"Got higher term '{ appendEntriesResponse.Term }'. Downgrading to follower.");
                    stateDataAppendResponse.CurrentTerm = appendEntriesResponse.Term;
                    stateDataAppendResponse.Vote(null);
                    SetVoteTimer();
                    return GoTo(NodeRole.Follower).Using(stateDataAppendResponse.CopyAsBase());
                }

                return Stay().Using(stateDataAppendResponse.CopyAsBase());
            }

            return null;
        });

        WhenUnhandled(state => 
        {
            if (state.FsmEvent is VoteRequest newTermVoteRequest && state.StateData is NodeState newTermStateDataVoteRequest && newTermVoteRequest.Term > newTermStateDataVoteRequest.CurrentTerm)
            {
                LogInformation($"Got higher term '{ newTermVoteRequest.Term }' from '{ newTermVoteRequest.CandidateId }' on vote request. Downgrading to follower.");
                newTermStateDataVoteRequest.CurrentTerm = newTermVoteRequest.Term;
                newTermStateDataVoteRequest.Vote(null);
                Self.Tell(state.FsmEvent);
                return GoTo(NodeRole.Follower).Using(newTermStateDataVoteRequest.CopyAsBase());
            }

            if (state.FsmEvent is AppendEntriesRequest newTermAppendEntriesRequest && state.StateData is NodeState newTermstateDataAppendRequest && newTermAppendEntriesRequest.Term > newTermstateDataAppendRequest.CurrentTerm)
            {
                LogInformation($"Got higher term '{ newTermAppendEntriesRequest.Term }' from leader '{ newTermAppendEntriesRequest.LeaderId }' on append entries request. Downgrading to follower.");
                newTermstateDataAppendRequest.CurrentTerm = newTermAppendEntriesRequest.Term;
                newTermstateDataAppendRequest.Vote(null);
                Self.Tell(state.FsmEvent);
                return GoTo(NodeRole.Follower).Using(newTermstateDataAppendRequest.CopyAsBase());
            }

            if (state.FsmEvent is VoteRequest voteRequest && state.StateData is NodeState stateDataVoteRequest)
            {   
                var (lastLogIndedx, lastLogTerm) = stateDataVoteRequest.GetLastLogInfo();
                var logOk = voteRequest.LastLogTerm > lastLogTerm || (voteRequest.LastLogTerm == lastLogTerm && voteRequest.LastLogIndex >= lastLogIndedx);
                var canVoteForCandidate = stateDataVoteRequest.CanVoteFor(voteRequest.CandidateId);
                LogInformation($"'{ voteRequest.CandidateId }' asks for a vote in the term '{ voteRequest.Term }'. IsLogOk: { logOk }. CanVote: { canVoteForCandidate }.");
                if (voteRequest.Term == stateDataVoteRequest.CurrentTerm && logOk && canVoteForCandidate)
                {   
                    // Only the follower can vote.
                    // Vote for node only if its log is up to date.
                    // Vote for node only in the same term.
                    // Can vote only if it's the same node that follower has already voted in the current term or follower hasn't voted yet.
                    LogInformation($"Voting for candidate '{ voteRequest.CandidateId }' in term '{ voteRequest.Term }'."); 
                    stateDataVoteRequest.Vote(voteRequest.CandidateId);
                    SetVoteTimer();
                    _raftMessagingActorRef.Tell((voteRequest, new VoteResponse() { Term = voteRequest.Term, NodeId = _currentNode.NodeId, VoteGranted = true }));
                    return Stay().Using(stateDataVoteRequest.Copy());
                }
                else
                {
                    // Candidate and leader nodes couldn't grant vote in the current term for other node as they have definitely already voted for itself in the current term.
                    // Decline any request with the term less than current term.
                     LogInformation($"Declining vote request from candidate '{ voteRequest.CandidateId }' in term '{ voteRequest.Term }'.");
                     _raftMessagingActorRef.Tell((voteRequest, new VoteResponse() { Term = voteRequest.Term, NodeId = _currentNode.NodeId,  VoteGranted = false })); 
                    return Stay().Using(stateDataVoteRequest.Copy());
                }
                    
            }
            
            if (state.FsmEvent is AppendEntriesRequest appendEntriesRequest && state.StateData is NodeState stateDataAppendRequest)
            {
                LogDebug($"Got append entries request from leader '{ appendEntriesRequest.LeaderId }' with term '{ appendEntriesRequest.Term }'.");
                SetVoteTimer();
                stateDataAppendRequest.CurrentLeader = appendEntriesRequest.LeaderId;
                _raftMessagingActorRef.Tell((appendEntriesRequest, new AppendEntriesResponse(){ Term = stateDataAppendRequest.CurrentTerm, NodeId = _currentNode.NodeId, Success = true }));
                return GoTo(NodeRole.Follower).Using(stateDataAppendRequest.Copy());
            }

            LogWarning($"Actor couldn't handle the message: '{ state.FsmEvent }'. Type: { state.FsmEvent.GetType() }.");
            return Stay().Using(state.StateData.Copy());
        });

        Initialize();
        _logger.Info("RAFT ACTOR INITILIZATION FINISHED.");
    }

    protected override void PreStart()
    {
        _logger.Info("STARTING RAFT ACTOR!!!");
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

    private void SetVoteTimer() => SetTimer(VoteTimerName, StateTimeout.Instance, CalculateNextVoteTimeout(), repeat: false);

    private void SetAppendEntriesTimer() => SetTimer(AppendEntriesTimerName, StateTimeout.Instance, CalculateNextAppendEntriesTimeout(), repeat: false);

    private void LogDebug(string? message = null) => _logger.Debug(FormaLogMessage(message));

    private void LogInformation(string? message = null) => _logger.Info(FormaLogMessage(message));

    private void LogWarning(string? message = null) => _logger.Warning(FormaLogMessage(message));

    private string FormaLogMessage(string? message = null)
    {
        return message == null ? $"{{ ROLE: '{ StateName }'. { StateData } }}" : $"{{ ROLE: '{ StateName }'. { StateData } }} - MESSAGE: { message }";
    }
}