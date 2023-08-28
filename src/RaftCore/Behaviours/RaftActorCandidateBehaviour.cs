using Akka.Actor;
using RaftCore.Common;
using RaftCore.Messages;
using RaftCore.States;

namespace RaftCore.Actors;

public partial class RaftActor
{
    public State<NodeRole, NodeState> CandidateBehaviour(Event<NodeState> state)
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
            return Stay().Using(stateDataTimeout.Copy());  
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

            return Stay().Using(stateDataVoteResponse.Copy());
        }      

        return null;
    }
}

