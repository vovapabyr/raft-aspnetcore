using System.Net;

namespace RaftNode.Options;

// Not a part of Raft specification.
public class ClusterInfoOptions
{
    public const string Key = "ClusterInfo";

    public string NodeNamePrefix { get; set; } 

    public int NodesCount { get; set; }

    public int VoteTimeoutMinValue { get; set; }

    public int VoteTimeoutMaxValue { get; set; }

    public string GetNodeName(int index) => $"{ NodeNamePrefix }-{ index }";

    public IEnumerable<string> GetNodesNames()
    {        
        for (int i = 1; i <= NodesCount; i++)
            yield return GetNodeName(i);
    } 
}