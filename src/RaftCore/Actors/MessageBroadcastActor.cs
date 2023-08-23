using Akka.Actor;
using Akka.Event;
using Grpc.Net.ClientFactory;
using RaftCore.Common;
using RaftCore.Services;

namespace RaftCore.Actors;

public class MessageBroadcastActor : ReceiveActor
{
    private readonly ILoggingAdapter _logger = Context.GetLogger();
    private readonly List<NodeInfo> _clusterNodes;
    private readonly List<string> _clientsIds = new List<string>();

    public MessageBroadcastActor(IClusterInfoService clusterInfoService, GrpcClientFactory grpcClientFactory)
    {
        _clusterNodes = clusterInfoService.ClusterNodes;
        _clientsIds = _clusterNodes.Select(x => x.NodeId).ToList();

        Receive<VoteRequest>(voteRequest => {
            _logger.Debug($"Broadcasting vote request from '{ voteRequest.CandidateId }' with term '{ voteRequest.Term }'.");
            foreach (var clientId in _clientsIds)
                Context.ActorOf(MessageDispatcherActor.Props(clusterInfoService, grpcClientFactory), $"vote-request-{ voteRequest.CandidateId }-{ clientId }-{ voteRequest.Term }-{ Guid.NewGuid() }")
                    .Tell((clientId, voteRequest));
        });

        Receive<(VoteRequest, VoteResponse)>(message => {
            var (request, response) = message;
            Context.ActorOf(MessageDispatcherActor.Props(clusterInfoService, grpcClientFactory), $"vote-response-{ response.NodeId }-{ request.CandidateId }-{ response.Term }-{ Guid.NewGuid() }")
                .Tell(message);
        });

        Receive<AppendEntriesRequest>(appendEntriesRequest => {
            _logger.Debug($"Broadcasting append entries request from leader '{ appendEntriesRequest.LeaderId }' with term '{ appendEntriesRequest.Term }'.");
            foreach (var clientId in _clientsIds)
                Context.ActorOf(MessageDispatcherActor.Props(clusterInfoService, grpcClientFactory), $"append-entries-request-{ appendEntriesRequest.LeaderId }-{ clientId }-{ appendEntriesRequest.Term }-{ Guid.NewGuid() }")
                    .Tell((clientId, appendEntriesRequest));
        });

        Receive<(AppendEntriesRequest, AppendEntriesResponse)>(message => {
            var (request, response) = message;
            Context.ActorOf(MessageDispatcherActor.Props(clusterInfoService, grpcClientFactory), $"append-entries-response-{ response.NodeId }-{ request.LeaderId }-{ response.Term }-{ Guid.NewGuid() }")
                .Tell(message);
        });
    }

    protected override void PreStart()
    {
        _logger.Info("Starting broadcast actor.");
        base.PreStart();
    }

    public static Props Props(IClusterInfoService clusterInfoService, GrpcClientFactory grpcClientFactory) => Akka.Actor.Props.Create(() => new MessageBroadcastActor(clusterInfoService, grpcClientFactory)).WithSupervisorStrategy(Akka.Actor.SupervisorStrategy.StoppingStrategy);
}