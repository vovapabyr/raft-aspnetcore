using System.Net;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.Options;
using RaftNode.Extensions;
using RaftNode.Options;

namespace RaftNode.Services;

public class RaftNodeHostedService : BackgroundService
{
    private readonly ClusterService _clusterService;
    private readonly GrpcClientFactory _grpcClientFactory;
    private readonly ILogger<RaftNodeHostedService> _logger;

    public RaftNodeHostedService(ClusterService clusterService, GrpcClientFactory grpcClientFactory, ILogger<RaftNodeHostedService> logger)
    {
        _clusterService = clusterService;
        _grpcClientFactory = grpcClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await DiscoverClusterNodesAsync();
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