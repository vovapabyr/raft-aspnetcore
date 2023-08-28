using System.Transactions;
using Akka.Actor;
using RaftCore.Common;
using RaftCore.Messages;
using RaftCore.States;

namespace RaftCore.Actors;

public partial class RaftActor
{
    public State<NodeRole, NodeState> LeaderBehaviour(Event<NodeState> state)
    {
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
                    CommitLogEntries(stateDataAppendResponse);
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
    }

    private void CommitLogEntries(LeaderNodeState leaderNodeState)
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
    }
}

