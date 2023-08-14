using Google.Protobuf.WellKnownTypes;
using Grpc.Net.ClientFactory;
using RaftCore;
using RaftCore.Services;
using RaftNode.Extensions;

namespace RaftNode.Services;

public class RaftNodeHostedService : BackgroundService
{
    private readonly RaftModule _raftModule;
    private readonly IClusterInfoService _clusterService;
    private readonly GrpcClientFactory _grpcClientFactory;
    private readonly IEnumerable<INodeRoleBehaviourService> _nodeRoleBehaviourServices;
    private readonly ILogger<RaftNodeHostedService> _logger;

    public RaftNodeHostedService(RaftModule raftModule, IClusterInfoService clusterService, GrpcClientFactory grpcClientFactory, IEnumerable<INodeRoleBehaviourService> nodeRoleBehaviourServices, ILogger<RaftNodeHostedService> logger)
    {
        _raftModule = raftModule;
        _clusterService = clusterService;
        _grpcClientFactory = grpcClientFactory;
        _nodeRoleBehaviourServices = nodeRoleBehaviourServices;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await DiscoverClusterNodesAsync();

        await _raftModule.StartAsync(stoppingToken);
    }

    private async Task DiscoverClusterNodesAsync()
    {
        await _clusterService.ResolveNodesDnsAsync();

        _logger.LogInformation("CLUSTER NODES DISCOVERY STARTED.");

        var discoverResults = new List<Task>();       
        foreach (var node in _clusterService.ClusterNodes)
        {
            _logger.LogInformation($"Trying to connect to { node }.");
            var discoverClient = _grpcClientFactory.CreateClient<RaftNode.DiscoveryService.DiscoveryServiceClient>(node.HostName);
            discoverResults.Add(PollyFactory.GetBasicResiliencePolicy(timeoutMilliseconds: 50).ExecuteAsync(() => discoverClient.DiscoverAsync(new Empty()).WaitForStatusAsync(_logger)));
        }

        await Task.WhenAll(discoverResults);

        _logger.LogInformation($"'{ discoverResults.Count }' CLUSTER NODES DISCOVERED.");
    }
}