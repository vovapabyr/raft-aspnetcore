using System.Net;
using Microsoft.Extensions.Options;
using Polly;
using RaftNode.Models;
using RaftNode.Options;

namespace RaftNode.Services;

public class ClusterService
{
    private readonly ClusterInfoOptions _clusterInfoOptions;
    private readonly ILogger<ClusterService> _logger;
    private SemaphoreSlim _listLock = new SemaphoreSlim(1);

    public ClusterService(IOptions<ClusterInfoOptions> clusterInfoOptions, ILogger<ClusterService> logger)
    {
        _clusterInfoOptions = clusterInfoOptions.Value;
        _logger = logger;
    }

    public NodeInfo CurrentNode { get; private set; }
     
    // Need to lock?
    public List<NodeInfo> ClusterNodes { get; } = new List<NodeInfo>(); 

    public async Task ResolveNodesDnsAsync()
    {
        _logger.LogInformation("NODES DNS RESOLUTION STARTED.");

        var hostIpAddress = Dns.GetHostAddresses(Dns.GetHostName()).First();
        var discoverNodesTasks = new List<Task>();
        foreach (var nodeName in _clusterInfoOptions.GetNodesNames())
            discoverNodesTasks.Add(Policy.Handle<Exception>().WaitAndRetryForeverAsync((retryAttempt, context) => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)))
                .ExecuteAsync(() => Dns.GetHostAddressesAsync(nodeName))
                .ContinueWith(t => 
                {
                    var nodeIpAddress = t.Result.First();
                    if (nodeIpAddress.Equals(hostIpAddress))
                    {
                        CurrentNode = new NodeInfo(nodeName, hostIpAddress);
                        _logger.LogInformation($"Current Node: { CurrentNode }.");
                    }
                    else
                    {
                        var node = new NodeInfo(nodeName, nodeIpAddress);
                        _listLock.Wait();
                        ClusterNodes.Add(node);
                        _listLock.Release();
                        _logger.LogInformation($"Node: { node }.");
                    }
                }));

        await Task.WhenAll(discoverNodesTasks);

        _logger.LogInformation($"CURRENT NODE: { CurrentNode } AND '{ ClusterNodes.Count }' NODES DNS RESOLVED."); 
    }
}