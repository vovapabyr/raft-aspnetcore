using RaftCore.Common;

namespace RaftCore.Services;

public interface IClusterInfoService
{
    NodeInfo CurrentNode { get; }
     
    List<NodeInfo> ClusterNodes { get; }
}