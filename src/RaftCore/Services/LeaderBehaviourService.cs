using RaftCore.Common;

namespace RaftCore.Services;

public class LeaderBehaviourService : INodeRoleBehaviourService
{
    public NodeRole NodeRole => NodeRole.Leader;

    public void Select()
    {
        throw new NotImplementedException();
    }

    public Task Vote()
    {
        throw new NotSupportedException();
    }
}