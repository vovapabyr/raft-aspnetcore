using RaftCore.Common;

namespace RaftCore.Services;

public interface INodeRoleBehaviourService
{
    NodeRole NodeRole{ get; }

    void Select();

    event Action<NodeRole> BehaviourChanged;
}