using Akka.Actor;
using Akka.Event;
using Grpc.Net.ClientFactory;
using RaftCore.Common;
using RaftCore.Services;

namespace RaftCore.Actors;

public class MessageDispatcherActor : ReceiveActor
{
    private readonly ILoggingAdapter _logger = Context.GetLogger();
    private readonly List<NodeInfo> _clusterNodes;
    private readonly Dictionary<string, RaftMessagingService.RaftMessagingServiceClient> _messagingClients = new Dictionary<string, RaftMessagingService.RaftMessagingServiceClient>();

    public MessageDispatcherActor(IClusterInfoService clusterInfoService, GrpcClientFactory grpcClientFactory)
    {
        _clusterNodes = clusterInfoService.ClusterNodes;
        foreach (var node in _clusterNodes)
        {
            _messagingClients.Add(node.NodeId, grpcClientFactory.CreateClient<RaftMessagingService.RaftMessagingServiceClient>(node.HostName));
        }

        Receive<(string, VoteRequest)>(message => {
            var (clientId, voteRequest) = message;

            _logger.Debug($"Sending vote request to '{ clientId }'.");
            _messagingClients[clientId].SendVoteRequestAsync(voteRequest);
            Context.Stop(Self);
        });

        Receive<(VoteRequest, VoteResponse)>(message => {
            var (request, response) = message;
            _logger.Debug($"Sending vote response from '{ response.NodeId }' to '{ request.CandidateId}' with status '{ response.VoteGranted }'.");
            _messagingClients[request.CandidateId].SendVoteResponseAsync(response);
            Context.Stop(Self);
        });

        Receive<(string, AppendEntriesRequest)>(message => {
            var (clientId, appendEntriesRequest) = message;
            
            _logger.Debug($"Sending append entries request to '{ clientId }'.");
            _messagingClients[clientId].SendAppendEntriesRequestAsync(appendEntriesRequest);
            Context.Stop(Self);
        });

        Receive<(AppendEntriesRequest, AppendEntriesResponse)>(message => {
            var (request, response) = message;
            _logger.Debug($"Sending append entries response from '{ response.NodeId }' to '{ request.LeaderId}' with status '{ response.Success }'.");
            _messagingClients[request.LeaderId].SendAppendEntriesResponseAsync(response);
            Context.Stop(Self);
        });
    }

    protected override void PreStart()
    {
        _logger.Info("Starting message dispatcher actor.");
        base.PreStart();
    }

    public static Props Props(IClusterInfoService clusterInfoService, GrpcClientFactory grpcClientFactory) => Akka.Actor.Props.Create(() => new MessageDispatcherActor(clusterInfoService, grpcClientFactory));
}