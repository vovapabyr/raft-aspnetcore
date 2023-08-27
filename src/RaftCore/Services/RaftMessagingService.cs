using Akka.Actor;
using Akka.Hosting;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using RaftCore.Actors;
using static RaftCore.RaftMessagingService;

namespace RaftCore.Services;

public class RaftMessagingService : RaftMessagingServiceBase
{
    private readonly IRequiredActor<RaftActor> _raftARefProvider;

    public RaftMessagingService(IRequiredActor<RaftActor> raftARefProvider)
    {
        _raftARefProvider = raftARefProvider;
    }

    public override Task<Empty> SendVoteRequest(VoteRequest request, ServerCallContext context)
    {
        _raftARefProvider.ActorRef.Tell(request);
        return Task.FromResult(new Empty());
    }


    public override Task<Empty> SendVoteResponse(VoteResponse request, ServerCallContext context)
    {
        _raftARefProvider.ActorRef.Tell(request);
        return Task.FromResult(new Empty());
    }

    public override Task<Empty> SendAppendEntriesRequest(AppendEntriesRequest request, ServerCallContext context)
    {
        _raftARefProvider.ActorRef.Tell(request);
        return Task.FromResult(new Empty());
    }

    public override Task<Empty> SendAppendEntriesResponse(AppendEntriesResponse request, ServerCallContext context)
    {
        _raftARefProvider.ActorRef.Tell(request);
        return Task.FromResult(new Empty());
    }
} 