using System.Collections.Immutable;
using System.Text;
using Akka.Actor;

namespace RaftCore.States;

public class LeaderNodeState : NodeState
{
    #region Raft

    private Dictionary<string, int> _nextIndex;

    private Dictionary<string, int> _matchIndex;

    #endregion

    private readonly int _majority;

    public LeaderNodeState(int currentTerm, string? votedFor, IList<LogEntry> log, int commitLength, string? currentLeader, Dictionary<string, IActorRef> pendingResponses, List<string> _nodesIds) : base(currentTerm, votedFor, log, commitLength, currentLeader, pendingResponses)
    {
        _nextIndex = _nodesIds.ToDictionary(id => id, id => log.Count);
        _matchIndex = _nodesIds.ToDictionary(id => id, id => 0);
        // Node after transition to leader can acknowledge all its own logs.
        _matchIndex.Add(currentLeader, log.Count);
        _majority = (int)Math.Ceiling((_nodesIds.Count + 1) / (double)2);
    }

    public LeaderNodeState(int currentTerm, string? votedFor, IList<LogEntry> log, int commitLength, string? currentLeader, Dictionary<string, IActorRef> pendingResponses, Dictionary<string, int> nextIndex, Dictionary<string, int> matchIndex, int majority) 
        : base(currentTerm, votedFor, log, commitLength, currentLeader, pendingResponses)
    {
        _nextIndex = nextIndex;
        _matchIndex = matchIndex;
        _majority = majority;
    }

    public override void AddLog(LogEntry logEntry)
    {
        base.AddLog(logEntry);
        if(!_matchIndex.ContainsKey(CurrentLeader))
            _matchIndex.Add(CurrentLeader, LogCount);
        else
            _matchIndex[CurrentLeader] = LogCount;
    }

    public (int, int, ImmutableList<LogEntry>) GetNodeNextInfo(string nodeId)
    {
        var nodeNextIndex = _nextIndex[nodeId];
        var newEntries = _log.Skip(nodeNextIndex).ToImmutableList();
        if (nodeNextIndex == 0)
            return (nodeNextIndex, 0, newEntries);
        
        return (nodeNextIndex, GetLogEntry(nodeNextIndex - 1).Term, newEntries);
    }

    public void AddPendingResponse(string logEntryId, IActorRef sender) 
    {
        if (!_pendingResponses.ContainsKey(logEntryId))
            _pendingResponses.Add(logEntryId, sender);
        else
            _pendingResponses[logEntryId] = sender;
    }

    public bool TryGetPendingResponseSender(string logEntryId, out IActorRef actorRef) 
    {
        return _pendingResponses.TryGetValue(logEntryId, out actorRef);
    }

    public void TryRemovePendingResponseSender(string logEntryId) => _pendingResponses.Remove(logEntryId);

    public int GetNodeMatchIndex(string nodeId) => _matchIndex[nodeId];

    public void SetNodeNextIndex(string nodeId, int nodeNextIndex) => _nextIndex[nodeId] = nodeNextIndex;

    public void SetNodeMatchIndex(string nodeId, int matchIndex) => _matchIndex[nodeId] = matchIndex;

    public IEnumerable<LogEntry> TryCommitLogEntries()
    {
        // Search latest log entry that can be commited.
        var newCommitLength = CommitLength;
        while (newCommitLength < LogCount)
        {
            var matchedNodesCount = 0;
            foreach (var matchNodeInfo in _matchIndex)
            {
                if (matchNodeInfo.Value > newCommitLength)
                    matchedNodesCount++;
            }

            if (matchedNodesCount < _majority)
                break;

            newCommitLength++;
        }

        // Return if nothing changed.
        if (newCommitLength == CommitLength)
            yield break;

        // Ensures that new leader cannot commit logs from previos terms until it commits log entry from current term.
        if (GetLogEntry(newCommitLength - 1).Term == CurrentTerm)
        {
            // Commit all logs until newCommitLength.
            for (var i = CommitLength; i < newCommitLength; i++)
            {
                CommitLength = i + 1;
                yield return GetLogEntry(i);
            }
        }
    }

    public override NodeState Copy() => new LeaderNodeState(_currentTerm, _votedFor, new List<LogEntry>(_log), _commitLength, _currentLeader, new Dictionary<string, IActorRef>(_pendingResponses), new Dictionary<string, int>(_nextIndex), new Dictionary<string, int>(_matchIndex), _majority);

    public override NodeState CopyAsBase() => new NodeState(_currentTerm, _votedFor, new List<LogEntry>(_log), _commitLength, _currentLeader, new Dictionary<string, IActorRef>(_pendingResponses));

    public override CandidateNodeState CopyAsCandidate() => new CandidateNodeState(_currentTerm, _votedFor, new List<LogEntry>(_log), _commitLength, _currentLeader, new Dictionary<string, IActorRef>(_pendingResponses), new HashSet<string>());

    public override LeaderNodeState CopyAsLeader(List<string> nodesIds) => new LeaderNodeState(_currentTerm, _votedFor, new List<LogEntry>(_log), _commitLength, _currentLeader, new Dictionary<string, IActorRef>(_pendingResponses), new Dictionary<string, int>(_nextIndex), new Dictionary<string, int>(_matchIndex), _majority);

    public override string ToString()
    {
        var stringBuilder = new StringBuilder();
        stringBuilder.Append("MATCH_INDEX: { ");
        foreach (var node in _matchIndex)
        {
            stringBuilder.Append($"{{ node: { node.Key }, value: { node.Value }}}, ");
        }
        stringBuilder.Append(" }.");
        return $" { base.ToString() } { stringBuilder }";
    }
}