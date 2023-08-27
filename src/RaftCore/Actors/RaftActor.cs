using System.Collections.Immutable;
using Akka.Actor;
using Akka.Event;
using Grpc.Net.ClientFactory;
using RaftCore.Common;
using RaftCore.Messages;
using RaftCore.Services;
using RaftCore.States;

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

        When(NodeRole.Follower, state => 
        {
            if (state.FsmEvent is VoteTimeout && state.StateData is NodeState stateDataTimeout)
            {
                LogInformation($"Vote timer elapsed. Switching to candidate.");
                // Trigger timeout again to start requesting votes.
                return GoTo(NodeRole.Candidate).Using(stateDataTimeout.CopyAsCandidate()).Replying(VoteTimeout.Instance);  
            }      

            return null;
        });

        When(NodeRole.Candidate, state => 
        {
            if (state.FsmEvent is VoteTimeout && state.StateData is CandidateNodeState stateDataTimeout)
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
                        Self.Tell(AppendEntriesTimeout.Instance);
                        return GoTo(NodeRole.Leader).Using(stateDataVoteResponse.CopyAsLeader(_clusterNodes.Select(n => n.NodeId).ToList()));
                    }
                }

                return Stay().Using(stateDataVoteResponse.CopyAsCandidate());
            }      

            return null;
        });

        When(NodeRole.Leader, (state) =>
        {
            var commitLogEntries = (LeaderNodeState leaderNodeState) => 
            {
                LogInformation("Trying to commit new log entries.");
                foreach (var committedEntry in leaderNodeState.TryCommitLogEntries())
                {
                    LogInformation($"New commited entry: '{ committedEntry }'.");
                    if (leaderNodeState.TryGetPendingResponseSender(committedEntry.Id, out var actorRef))
                    {
                        LogInformation($"Sending entry '{ committedEntry }' replication acknowledgment back to client '{ actorRef.Path }'.");
                        actorRef.Tell(new CommandSuccessfullyCommited(committedEntry.Id, committedEntry.Command, committedEntry.Term));
                        leaderNodeState.TryRemovePendingResponseSender(committedEntry.Id);
                    }
                }
            };

            if (state.FsmEvent is AppendEntriesTimeout && state.StateData is LeaderNodeState stateDataTimeout)
            {
                LogDebug($"Sending append entries request to nodes.");
                SetAppendEntriesTimer();
                _raftMessagingActorRef.Tell(new BroadcastAppendEntries(stateDataTimeout, _currentNode.NodeId));             
                return Stay().Using(stateDataTimeout.Copy());
            }

            if (state.FsmEvent is AppendEntriesResponse appendEntriesResponse && state.StateData is LeaderNodeState stateDataAppendResponse)
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

                if (appendEntriesResponse.Term == stateDataAppendResponse.CurrentTerm)
                {
                    var responseNodeId = appendEntriesResponse.NodeId;
                    var responseMatchIndex = appendEntriesResponse.MatchIndex;
                    var nodeMatchInedx = stateDataAppendResponse.GetNodeMatchIndex(responseNodeId);
                    if (appendEntriesResponse.Success && responseMatchIndex >= nodeMatchInedx)
                    {
                        LogDebug($"Updaing node '{ responseNodeId }' nextIndex and matchIndex values to '{ responseMatchIndex }' for leader '{ _currentNode.NodeId }'.");
                        stateDataAppendResponse.SetNodeNextIndex(responseNodeId, responseMatchIndex);
                        stateDataAppendResponse.SetNodeMatchIndex(responseNodeId, responseMatchIndex);
                        commitLogEntries(stateDataAppendResponse);
                    }
                    else if (stateDataAppendResponse.GetNodeNextInfo(responseNodeId) is var (prevLogIndex, _, _) && prevLogIndex > 0)
                    {
                        var decrementedPrevLogIndex = prevLogIndex - 1;
                        LogDebug($"Follower node '{ responseNodeId }' log is not up to date with leader '{ _currentNode.NodeId }'. Decremented prevLogIndex: '{ decrementedPrevLogIndex }'.");
                        stateDataAppendResponse.SetNodeNextIndex(responseNodeId, decrementedPrevLogIndex);
                        _raftMessagingActorRef.Tell(new AppendEntries(stateDataAppendResponse, _currentNode.NodeId, responseNodeId)); 
                    }
                }

                return Stay().Using(stateDataAppendResponse.Copy());
            }

            if (state.FsmEvent is AddNewCommand addNewCommandRequest && state.StateData is LeaderNodeState stateDataAddNewCommand)
            {
                LogInformation($"Client '{ Sender.Path }' adds new command. Current log entry count: '{ stateDataAddNewCommand.LogCount }'.");
                var newLogEntryId = Guid.NewGuid().ToString();
                stateDataAddNewCommand.AddPendingResponse(newLogEntryId, Sender);
                var newLogEntry = new LogEntry() { Id = newLogEntryId, Command = addNewCommandRequest.Command, Term = stateDataAddNewCommand.CurrentTerm };
                stateDataAddNewCommand.AddLog(newLogEntry);
                _raftMessagingActorRef.Tell(new BroadcastAppendEntries(stateDataAddNewCommand, _currentNode.NodeId));
                SetAppendEntriesTimer();
                return Stay().Using(stateDataAddNewCommand.Copy());  
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
                SetVoteTimer();
                Self.Tell(state.FsmEvent);
                return GoTo(NodeRole.Follower).Using(newTermStateDataVoteRequest.CopyAsBase());
            }

            if (state.FsmEvent is AppendEntriesRequest newTermAppendEntriesRequest && state.StateData is NodeState newTermstateDataAppendRequest && newTermAppendEntriesRequest.Term > newTermstateDataAppendRequest.CurrentTerm)
            {
                LogInformation($"Got higher term '{ newTermAppendEntriesRequest.Term }' from leader '{ newTermAppendEntriesRequest.LeaderId }' on append entries request. Downgrading to follower.");
                newTermstateDataAppendRequest.CurrentTerm = newTermAppendEntriesRequest.Term;
                newTermstateDataAppendRequest.Vote(null);
                SetVoteTimer();
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
                }
                else
                {
                    // Candidate and leader nodes couldn't grant vote in the current term for other node as they have definitely already voted for itself in the current term.
                    // Decline any request with the term less than current term.
                     LogInformation($"Declining vote request from candidate '{ voteRequest.CandidateId }' in term '{ voteRequest.Term }'.");
                     _raftMessagingActorRef.Tell((voteRequest, new VoteResponse() { Term = voteRequest.Term, NodeId = _currentNode.NodeId,  VoteGranted = false })); 
                }

                return Stay().Using(stateDataVoteRequest.Copy()); 
            }
            
            if (state.FsmEvent is AppendEntriesRequest appendEntriesRequest && state.StateData is NodeState stateDataAppendRequest)
            {
                LogDebug($"Got append entries request from leader '{ appendEntriesRequest.LeaderId }' with term '{ appendEntriesRequest.Term }'.");

                var logOk = (stateDataAppendRequest.LogCount >= appendEntriesRequest.PrevLogIndex) && 
                    (appendEntriesRequest.PrevLogIndex == 0 || stateDataAppendRequest.GetLogEntry(appendEntriesRequest.PrevLogIndex - 1).Term == appendEntriesRequest.PrevLogTerm);
                
                if (appendEntriesRequest.Term == stateDataAppendRequest.CurrentTerm && logOk)
                {
                    // Only the follower or candidate can succesfully respond to append entries.
                    // Always convert to follower.
                    LogDebug($"Approve append entries request from leader '{ appendEntriesRequest.LeaderId }' with term '{ appendEntriesRequest.Term }'.");
                    stateDataAppendRequest.CurrentLeader = appendEntriesRequest.LeaderId;
                    AppendEntries(appendEntriesRequest, stateDataAppendRequest);
                    _raftMessagingActorRef.Tell((appendEntriesRequest, new AppendEntriesResponse(){ Term = stateDataAppendRequest.CurrentTerm, NodeId = _currentNode.NodeId, MatchIndex = appendEntriesRequest.PrevLogIndex + appendEntriesRequest.Entries.Count, Success = true }));
                    SetVoteTimer();
                    return GoTo(NodeRole.Follower).Using(stateDataAppendRequest.Copy());
                }
                else
                {
                    // Decline any request with the term less than current term.
                    LogDebug($"Declining append entries request from leader '{ appendEntriesRequest.LeaderId }' with term '{ appendEntriesRequest.Term }'.");
                    _raftMessagingActorRef.Tell((appendEntriesRequest, new AppendEntriesResponse(){ Term = stateDataAppendRequest.CurrentTerm, NodeId = _currentNode.NodeId, MatchIndex = 0, Success = false }));
                    return Stay().Using(stateDataAppendRequest.Copy());
                }                               
            }

            if (state.FsmEvent is AddNewCommand addNewCommandRequest && state.StateData is NodeState stateDataAddNewCommand)
            {
                LogWarning($"Node is not a leader. Redirecting to leader '{ stateDataAddNewCommand.CurrentLeader }'.");
                return Stay().Using(stateDataAddNewCommand.Copy()).Replying(new RedirectToLeader(stateDataAddNewCommand.CurrentTerm, stateDataAddNewCommand.CurrentLeader));  
            }

            if (state.FsmEvent is GetNodeInfo getNodeInfoRequest && state.StateData is NodeState stateDataGenNodeInfo)
            {
                LogInformation($"Client '{ Sender.Path }' requests node info.");
                return Stay().Using(stateDataGenNodeInfo.Copy()).Replying(new Messages.NodeInfo(StateName, stateDataGenNodeInfo.CurrentTerm, stateDataGenNodeInfo.CommitLength, stateDataGenNodeInfo.CopyLog()));
            }

            LogWarning($"Actor couldn't handle the message: '{ state.FsmEvent }'. Type: { state.FsmEvent.GetType() }.");
            return Stay().Using(state.StateData.Copy());
        });

        Initialize();
        LogInformation("RAFT ACTOR INITILIZATION FINISHED.");
    }

    private void AppendEntries(AppendEntriesRequest appendEntriesRequest, NodeState nodeState)
    {
        var newEntries = appendEntriesRequest.Entries.ToImmutableList();
        var prevLogIndex = appendEntriesRequest.PrevLogIndex;
        // Replace overlaping entries in log with entries from request.
        LogInformation($"Trying to copy new  '{ newEntries.Count }' log entries with prevLogIndex '{ prevLogIndex }'. Current log count: '{ nodeState.LogCount }'."); 
        if (newEntries.Count > 0 && nodeState.LogCount > prevLogIndex)
        {
            var lastLogIndex = Math.Min(nodeState.LogCount, prevLogIndex + newEntries.Count) - 1;
            if (nodeState.GetLogEntry(lastLogIndex).Term != newEntries[lastLogIndex - prevLogIndex].Term)
            {
                LogInformation($"There is overlapping entries between existing log and new entries from request. Cropping log to prevLogIndex '{ prevLogIndex }'.");
                nodeState.CropLogEntry(prevLogIndex);
            }
        }
        LogInformation($"Adding '{ prevLogIndex + newEntries.Count - nodeState.LogCount }' to node log.");
        // Append new entries if any.
        if (prevLogIndex + newEntries.Count > nodeState.LogCount)
        {
            for (var i = nodeState.LogCount - prevLogIndex; i < newEntries.Count; i++)
            {
                LogInformation($"Appending new entry: { newEntries[i] }.");
                nodeState.AddLog(newEntries[i]);
            }
        }
        if (appendEntriesRequest.LeaderCommit > nodeState.CommitLength)
        {
            LogInformation($"APPLY COMMANDS FROM '{ nodeState.CommitLength }' TO '{ appendEntriesRequest.LeaderCommit }' TO THE SATE");
            // APPLY TO STATE ALL COMMANDS FROM nodeState.CommitLength TO appendEntriesRequest.LeaderCommit.
            nodeState.CommitLength = Math.Min(appendEntriesRequest.LeaderCommit, nodeState.LogCount);
        }
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