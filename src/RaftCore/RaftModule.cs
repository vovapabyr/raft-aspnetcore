using System.Dynamic;
using Microsoft.Extensions.Logging;
using RaftCore.Common;
using RaftCore.Services;

namespace RaftCore;
public class RaftModule
{ 
    private NodeRole _currentRole = NodeRole.Follower;

    private INodeRoleBehaviourService _currentBehavior; 

    private NodeInfo _currentNode;

    private Dictionary<string, NodeInfo> _nodes;

    private readonly Dictionary<NodeRole, INodeRoleBehaviourService> _behavioursServices;
    
    private readonly ILogger<RaftModule> _logger;

    private readonly IClusterInfoService _clusterDescriptionService;

    private readonly NodeState _nodeStateService;

    public RaftModule(IClusterInfoService clusterDescriptionService, NodeState nodeStateService, IEnumerable<INodeRoleBehaviourService> behavioursServices, ILogger<RaftModule> logger)
    {
        _logger = logger;
        _clusterDescriptionService = clusterDescriptionService;
        _nodeStateService = nodeStateService;

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
        _nodes = _clusterDescriptionService.ClusterNodes.ToDictionary(n => n.NodeId, n => n);

        SwitchToBehaviour(_currentRole);

        return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }

    public Task<VoteResponse> ProcessVoteRequestAsync(VoteRequest voteRequest)
    {
        TryUpdateTerm(voteRequest.Term);

        var (lastLogIndedx, lastLogTerm) = _nodeStateService.GetLastLogInfo();
        var logOk = voteRequest.LastLogTerm > lastLogTerm || (voteRequest.LastLogTerm == lastLogTerm && voteRequest.LastLogIndex >= lastLogIndedx);

        if (voteRequest.Term == _nodeStateService.CurrentTerm && logOk && (_nodeStateService.VotedFor == null || _nodeStateService.VotedFor == voteRequest.CandidateId))
        {
            _nodeStateService.Vote(voteRequest.CandidateId);
            return Task.FromResult(new VoteResponse(){ Term = _nodeStateService.CurrentTerm, VoteGranted = true});
        }
        else
            return Task.FromResult(new VoteResponse(){ Term = _nodeStateService.CurrentTerm, VoteGranted = false});
    }

    public void SwitchToBehaviour(NodeRole nodeRole) 
    {
        _logger.LogInformation($"NODE: { _currentNode }. Switching node to '{ nodeRole }'.");
        // Need to switch with lock?
        _currentBehavior = _behavioursServices[nodeRole];
        _currentBehavior.Select();        
    }

    private bool TryUpdateTerm(int term)
    {
        if (term > _nodeStateService.CurrentTerm)
        {
            _nodeStateService.CurrentTerm = term;
            _nodeStateService.Vote(null);
            SwitchToBehaviour(NodeRole.Follower);
            return true;
        }

        return false;
    }
}
