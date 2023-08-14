using RaftCore.Common;

namespace RaftCore.Services;

public class LeaderBehaviourService : INodeRoleBehaviourService
{
    public NodeRole NodeRole => NodeRole.Leader;

    public event Action<NodeRole> BehaviourChanged;

    public void Select()
    {
        throw new NotImplementedException();
    }
}