using RaftCore.Common;

namespace RaftCore.Services;

public class CandidateBehaviourService : INodeRoleBehaviourService
{
    public NodeRole NodeRole => NodeRole.Candidate;

    public void Select()
    {
        throw new NotImplementedException();
    }

    public Task Vote()
    {
        throw new NotSupportedException();
    }
}