using Microsoft.AspNetCore.Localization;
using RaftCore.Services;

namespace RaftCore.Messages;

public class AppendEntries
{
    public AppendEntries(LeaderNodeState leaderNodeState, string leaderId, string nodeId)
    {
        LeaderNodeState = leaderNodeState;
        LeaderId = leaderId;
        NodeId = nodeId;
    }

    public LeaderNodeState LeaderNodeState { get;}

    public string LeaderId { get; }

    public string NodeId { get; }
}