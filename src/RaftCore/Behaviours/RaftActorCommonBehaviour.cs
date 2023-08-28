using System.Collections.Immutable;
using System.Transactions;
using Akka.Actor;
using RaftCore.Common;
using RaftCore.Messages;
using RaftCore.States;

namespace RaftCore.Actors;

public partial class RaftActor
{
    public State<NodeRole, NodeState> CommonBehaviour(Event<NodeState> state)
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
            return Stay().Using(stateDataGenNodeInfo.Copy()).Replying(new Messages.NodeInfo(_currentNode.NodeId, StateName, stateDataGenNodeInfo.CurrentTerm, stateDataGenNodeInfo.CommitLength, stateDataGenNodeInfo.CopyLog()));
        }

        LogWarning($"Actor couldn't handle the message: '{ state.FsmEvent }'. Type: { state.FsmEvent.GetType() }.");
        return Stay().Using(state.StateData.Copy());
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
}

