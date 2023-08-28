using RaftCore.Common;
using RaftCore.Messages;
using RaftCore.States;

namespace RaftCore.Actors;

public partial class RaftActor
{
    public State<NodeRole, NodeState> FollowerBehaviour(Event<NodeState> state)
    {
        if (state.FsmEvent is VoteTimeout && state.StateData is NodeState stateDataTimeout)
        {
            LogInformation($"Vote timer elapsed. Switching to candidate.");
            // Trigger timeout again to start requesting votes.
            return GoTo(NodeRole.Candidate).Using(stateDataTimeout.CopyAsCandidate()).Replying(VoteTimeout.Instance);  
        }      

        return null;
    }
}

