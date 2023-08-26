using System.Collections.Immutable;
using Akka.Actor;
using RaftCore.Common;

namespace RaftCore.Services;

public class LeaderNodeState : NodeState
{
    private Dictionary<string, int> _nextIndex = new Dictionary<string, int>();

    private Dictionary<string, int> _matchIndex = new Dictionary<string, int>();

    private Dictionary<string, IActorRef> _pendingResponses = new Dictionary<string, IActorRef>();

    public LeaderNodeState(int currentTerm, string? votedFor, IList<LogEntry> log, int commitLength, string? currentLeader, List<string> _nodesIds) : base(currentTerm, votedFor, log, commitLength, currentLeader)
    {
        _nextIndex = _nodesIds.ToDictionary(id => id, id => log.Count);
        _matchIndex = _nodesIds.ToDictionary(id => id, id => 0);
    }

    public LeaderNodeState(int currentTerm, string? votedFor, IList<LogEntry> log, int commitLength, string? currentLeader, Dictionary<string, int> nextIndex, Dictionary<string, int> matchIndex, Dictionary<string, IActorRef> pendingResponses) 
        : base(currentTerm, votedFor, log, commitLength, currentLeader)
    {
        _nextIndex = nextIndex;
        _matchIndex = matchIndex;
        _pendingResponses = pendingResponses;
    }

    public void AddLog(string leaderId, LogEntry logEntry)
    {
        
        _log.Add(logEntry);
        if(!_matchIndex.ContainsKey(leaderId))
            _matchIndex.Add(leaderId, LogCount);
        else
            _matchIndex[leaderId] = LogCount;
    }

    public (int, int, ImmutableList<LogEntry>) GetNodeNextInfo(string nodeId)
    {
        var nodeNextIndex = _nextIndex[nodeId];
        if (nodeNextIndex == 0)
            return (nodeNextIndex, 0, ImmutableList<LogEntry>.Empty);
        
        return (nodeNextIndex, GetLogEntry(nodeNextIndex - 1).Term, _log.Skip(nodeNextIndex).ToImmutableList());
    }

    public void AddPendingResponse(string logEntryId, IActorRef sender) 
    {
        if (!_pendingResponses.ContainsKey(logEntryId))
            _pendingResponses.Add(logEntryId, sender);
        else
            _pendingResponses[logEntryId] = sender;
    }

    public int GetNodeMatchIndex(string nodeId) => _matchIndex[nodeId];

    public void SetNodeNextIndex(string nodeId, int nodeNextIndex) => _nextIndex[nodeId] = nodeNextIndex;

    public void SetNodeMatchIndex(string nodeId, int matchIndex) => _matchIndex[nodeId] = matchIndex;

    public override NodeState Copy() => new LeaderNodeState(_currentTerm, _votedFor, new List<LogEntry>(_log), _commitLength, _currentLeader, new Dictionary<string, int>(_nextIndex), new Dictionary<string, int>(_matchIndex), new Dictionary<string, IActorRef>(_pendingResponses));

    public override NodeState CopyAsBase() => new NodeState(_currentTerm, _votedFor, new List<LogEntry>(_log), _commitLength, _currentLeader);

    public override CandidateNodeState CopyAsCandidate() => new CandidateNodeState(_currentTerm, _votedFor, new List<LogEntry>(_log), _commitLength, _currentLeader, new HashSet<string>());

    public override LeaderNodeState CopyAsLeader(List<string> nodesIds) => new LeaderNodeState(_currentTerm, _votedFor, new List<LogEntry>(_log), _commitLength, _currentLeader, new Dictionary<string, int>(_nextIndex), new Dictionary<string, int>(_matchIndex), new Dictionary<string, IActorRef>(_pendingResponses)); 
}