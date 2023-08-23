using System.Net;
using Microsoft.Extensions.Options;
using Polly;
using RaftCore.Common;
using RaftCore.Services;
using RaftNode.Options;

namespace RaftNode.Services;

public class SimpleClusterInfoService : IClusterInfoService
{
    private readonly ClusterInfoOptions _clusterInfoOptions;
    private readonly ILogger<SimpleClusterInfoService> _logger;
    private SemaphoreSlim _listLock = new SemaphoreSlim(1);

    public SimpleClusterInfoService(IOptions<ClusterInfoOptions> clusterInfoOptions, ILogger<SimpleClusterInfoService> logger)
    {
        _clusterInfoOptions = clusterInfoOptions.Value;
        VoteTimeoutMinValue = _clusterInfoOptions.VoteTimeoutMinValue;
        VoteTimeoutMaxValue = _clusterInfoOptions.VoteTimeoutMaxValue;
        AppendEntriesTimeoutMinValue = _clusterInfoOptions.AppendEntriesTimeoutMinValue;
        AppendEntriesTimeoutMaxValue = _clusterInfoOptions.AppendEntriesTimeoutMaxValue;
        _logger = logger;
    }

    public NodeInfo CurrentNode { get; private set; }
     
    // Need to lock?
    public List<NodeInfo> ClusterNodes { get; } = new List<NodeInfo>();

    public int VoteTimeoutMinValue { get; private set; }

    public int VoteTimeoutMaxValue { get; private set; }

    public int AppendEntriesTimeoutMinValue { get; private set; }

    public int AppendEntriesTimeoutMaxValue { get; private set; }

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