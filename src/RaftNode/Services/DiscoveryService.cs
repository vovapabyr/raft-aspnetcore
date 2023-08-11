using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using static RaftNode.DiscoveryService;

namespace RaftNode.Services;

public class DiscoveryService : DiscoveryServiceBase
{
    public override Task<Empty> Discover(Empty request, ServerCallContext context)
    {
        return Task.FromResult(new Empty());
    }
}