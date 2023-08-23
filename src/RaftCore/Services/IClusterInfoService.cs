using RaftCore.Common;

namespace RaftCore.Services;

public interface IClusterInfoService
{
    NodeInfo CurrentNode { get; }
     
    List<NodeInfo> ClusterNodes { get; }

    int VoteTimeoutMinValue { get; }

    int VoteTimeoutMaxValue { get; }

    int AppendEntriesTimeoutMinValue { get; }

    int AppendEntriesTimeoutMaxValue { get; }

    Task ResolveNodesDnsAsync();
}