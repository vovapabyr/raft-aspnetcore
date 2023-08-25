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
    private readonly List<string> _nodesIds = new List<string>();

    public MessageBroadcastActor(IClusterInfoService clusterInfoService, GrpcClientFactory grpcClientFactory)
    {
        _clusterNodes = clusterInfoService.ClusterNodes;
        _nodesIds = _clusterNodes.Select(x => x.NodeId).ToList();

        Receive<VoteRequest>(voteRequest => {
            _logger.Debug($"Broadcasting vote request from '{ voteRequest.CandidateId }' with term '{ voteRequest.Term }'.");
            foreach (var nodeId in _nodesIds)
                Context.ActorOf(MessageDispatcherActor.Props(clusterInfoService, grpcClientFactory), $"vote-request-{ voteRequest.CandidateId }-{ nodeId }-{ voteRequest.Term }-{ Guid.NewGuid() }")
                    .Tell((nodeId, voteRequest));
        });

        Receive<(VoteRequest, VoteResponse)>(message => {
            var (request, response) = message;
            Context.ActorOf(MessageDispatcherActor.Props(clusterInfoService, grpcClientFactory), $"vote-response-{ response.NodeId }-{ request.CandidateId }-{ response.Term }-{ Guid.NewGuid() }")
                .Tell(message);
        });

        Receive<(LeaderNodeState, string, string)>(appendEntriesRequest => {
            var (leaderNodeState, leaderId, nodeId) = appendEntriesRequest;
            _logger.Debug($"Retrying append entries request from leader '{ leaderId }' to node '{ nodeId }' with term '{ leaderNodeState.CurrentTerm }'.");
            var (prevLogIndex, prevLogTerm) = leaderNodeState.GetNodeNextInfo(nodeId);
            Context.ActorOf(MessageDispatcherActor.Props(clusterInfoService, grpcClientFactory), $"append-entries-request-{ leaderId }-{ nodeId }-{ leaderNodeState.CurrentTerm }-{ Guid.NewGuid() }")
                .Tell((nodeId, new AppendEntriesRequest() { Term = leaderNodeState.CurrentTerm, LeaderId = leaderId, PrevLogIndex = prevLogIndex, PrevLogTerm = prevLogTerm, LeaderCommit = leaderNodeState.CommitLength }));
        });

        Receive<(LeaderNodeState, string)>(appendEntriesRequest => {
            var (leaderNodeState, leaderId) = appendEntriesRequest;
            _logger.Debug($"Broadcasting append entries request from leader '{ leaderId }' with term '{ leaderNodeState.CurrentTerm }'.");
            foreach (var nodeId in _nodesIds)
            {
                var (prevLogIndex, prevLogTerm) = leaderNodeState.GetNodeNextInfo(nodeId);
                Context.ActorOf(MessageDispatcherActor.Props(clusterInfoService, grpcClientFactory), $"append-entries-request-{ leaderId }-{ nodeId }-{ leaderNodeState.CurrentTerm }-{ Guid.NewGuid() }")
                    .Tell((nodeId, new AppendEntriesRequest() { Term = leaderNodeState.CurrentTerm, LeaderId = leaderId, PrevLogIndex = prevLogIndex, PrevLogTerm = prevLogTerm, LeaderCommit = leaderNodeState.CommitLength }));
            }
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