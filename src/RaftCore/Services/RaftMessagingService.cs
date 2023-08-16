using Grpc.Core;
using static RaftCore.RaftMessagingService;

namespace RaftCore.Services;

public class RaftMessagingService : RaftMessagingServiceBase
{
    private readonly RaftModule _raftModule;

    public RaftMessagingService(RaftModule raftModule)
    {
        _raftModule = raftModule;
    }

    public override Task<VoteResponse> Vote(VoteRequest request, ServerCallContext context)
    {
        return base.Vote(request, context);
    }
} 