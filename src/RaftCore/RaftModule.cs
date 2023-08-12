using System.Dynamic;
using RaftCore.Commands;
using RaftCore.Common;
using RaftCore.Services;

namespace RaftCore;
public class RaftModule
{
    // TODO Check against docs.
    #region Raft persisted

    private int _currentTerm = 0;

    private string? _votedFor = null;

    private IList<ICommand> _log = new List<ICommand>();

    private long _commitLength = 0;

    #endregion

    // TODO Check against docs.
    #region Raft not-persisted

    private NodeRole _currentRole = NodeRole.Follower;

    private string? _currentLeader = null;

    private HashSet<string> _votesReceived = new HashSet<string>();  

    #endregion

    private readonly NodeInfo _currentNode;

    private readonly Dictionary<string, NodeInfo> _nodes;

    private readonly Dictionary<NodeRole, INodeRoleBehaviourService> _behavioursServices;

    private INodeRoleBehaviourService _currentBehavior; 

    public RaftModule(IClusterInfoService clusterDescriptionService, IEnumerable<INodeRoleBehaviourService> behavioursServices)
    {
        _currentNode = clusterDescriptionService.CurrentNode ?? throw new ArgumentNullException(nameof(clusterDescriptionService.CurrentNode));

        if (clusterDescriptionService.ClusterNodes == null) 
            throw new ArgumentNullException(nameof(clusterDescriptionService.ClusterNodes));
        // Throw on number of nodes?

        if (behavioursServices == null) 
            throw new ArgumentNullException(nameof(behavioursServices));

        if (behavioursServices.Count() != 3)
            throw new NotSupportedException($"Invalid number of raft roles: { behavioursServices?.Count() }.");

        _nodes = clusterDescriptionService.ClusterNodes.ToDictionary(n => n.IPAddress.ToString(), n => n);
        _behavioursServices = behavioursServices.ToDictionary(b => b.NodeRole, b => b);

        SwitchToBehaviour(_currentRole); 
    }

    public void SwitchToBehaviour(NodeRole nodeRole) 
    {
        _currentBehavior = _behavioursServices[nodeRole];
        _currentBehavior.Select(); 
    }
}
