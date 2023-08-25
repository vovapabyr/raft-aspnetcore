using RaftCore.Services;

namespace RaftCore.Messages;

public class BroadcastAppendEntries
{
    public BroadcastAppendEntries(LeaderNodeState leaderNodeState, string leaderId)
    {
        LeaderNodeState = leaderNodeState;
        LeaderId = leaderId;
    }

    public LeaderNodeState LeaderNodeState { get; }

    public string LeaderId { get; } 
}