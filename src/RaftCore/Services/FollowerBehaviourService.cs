using RaftCore.Common;

namespace RaftCore.Services;

public class FollowerBehaviourService : INodeRoleBehaviourService
{
    public NodeRole NodeRole => NodeRole.Follower;

    public void Select()
    {
        throw new NotImplementedException();
    }

    public Task Vote()
    {
        throw new NotSupportedException();
    }
}