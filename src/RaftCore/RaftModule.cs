using System.Dynamic;
using Microsoft.Extensions.Logging;
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

    private NodeInfo _currentNode;

    private Dictionary<string, NodeInfo> _nodes;

    private readonly Dictionary<NodeRole, INodeRoleBehaviourService> _behavioursServices;

    private INodeRoleBehaviourService _currentBehavior; 
    
    private readonly ILogger<RaftModule> _logger;

    private readonly IClusterInfoService _clusterDescriptionService;

    public RaftModule(IClusterInfoService clusterDescriptionService, IEnumerable<INodeRoleBehaviourService> behavioursServices, ILogger<RaftModule> logger)
    {
        _logger = logger;
        _clusterDescriptionService = clusterDescriptionService;

        if (behavioursServices == null) 
            throw new ArgumentNullException(nameof(behavioursServices));

        if (behavioursServices.Count() != 3)
            throw new NotSupportedException($"Invalid number of raft roles: { behavioursServices?.Count() }.");
        
        _behavioursServices = behavioursServices.ToDictionary(b => b.NodeRole, b => b);
        foreach (var behaviour in behavioursServices) 
            behaviour.BehaviourChanged += SwitchToBehaviour;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("STARTING RAFT MODULE.");
        _currentNode = _clusterDescriptionService.CurrentNode ?? throw new ArgumentNullException(nameof(_clusterDescriptionService.CurrentNode));

        if (_clusterDescriptionService.ClusterNodes == null) 
            throw new ArgumentNullException(nameof(_clusterDescriptionService.ClusterNodes));
        // Throw on number of nodes?
        _nodes = _clusterDescriptionService.ClusterNodes.ToDictionary(n => n.IPAddress.ToString(), n => n);

        SwitchToBehaviour(_currentRole);

        return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    public void SwitchToBehaviour(NodeRole nodeRole) 
    {
        _logger.LogInformation($"NODE: { _currentNode }. Switching node to '{ nodeRole }'.");
        _currentBehavior = _behavioursServices[nodeRole];
        _currentBehavior.Select();        
    }
}
