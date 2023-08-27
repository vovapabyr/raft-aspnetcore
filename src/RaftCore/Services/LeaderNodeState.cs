using System.Collections.Immutable;
using System.Text;
using Akka.Actor;

namespace RaftCore.Services;

public class LeaderNodeState : NodeState
{
    private Dictionary<string, int> _nextIndex;

    private Dictionary<string, int> _matchIndex;

    private Dictionary<string, IActorRef> _pendingResponses = new Dictionary<string, IActorRef>();

    private readonly int _majority;

    public LeaderNodeState(int currentTerm, string? votedFor, IList<LogEntry> log, int commitLength, string? currentLeader, List<string> _nodesIds) : base(currentTerm, votedFor, log, commitLength, currentLeader)
    {
        _nextIndex = _nodesIds.ToDictionary(id => id, id => log.Count);
        _matchIndex = _nodesIds.ToDictionary(id => id, id => 0);
        _majority = (int)Math.Ceiling((_nodesIds.Count + 1) / (double)2);
    }

    public LeaderNodeState(int currentTerm, string? votedFor, IList<LogEntry> log, int commitLength, string? currentLeader, Dictionary<string, int> nextIndex, Dictionary<string, int> matchIndex, Dictionary<string, IActorRef> pendingResponses, int majority) 
        : base(currentTerm, votedFor, log, commitLength, currentLeader)
    {
        _nextIndex = nextIndex;
        _matchIndex = matchIndex;
        _pendingResponses = pendingResponses;
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
        // Ensures that new leader cannot commit logs from previos terms until it gets new message. 
        while (CommitLength < LogCount)
        {
            var matchedNodesCount = 0;
            foreach (var matchNodeInfo in _matchIndex)
            {
                if (matchNodeInfo.Value > CommitLength)
                    matchedNodesCount++;
            }

            if (matchedNodesCount >= _majority)
            {
                yield return GetLogEntry(CommitLength);
                CommitLength += 1;
            }
            else
                yield break;
        }
    }

    public override NodeState Copy() => new LeaderNodeState(_currentTerm, _votedFor, new List<LogEntry>(_log), _commitLength, _currentLeader, new Dictionary<string, int>(_nextIndex), new Dictionary<string, int>(_matchIndex), new Dictionary<string, IActorRef>(_pendingResponses), _majority);

    public override NodeState CopyAsBase() => new NodeState(_currentTerm, _votedFor, new List<LogEntry>(_log), _commitLength, _currentLeader);

    public override CandidateNodeState CopyAsCandidate() => new CandidateNodeState(_currentTerm, _votedFor, new List<LogEntry>(_log), _commitLength, _currentLeader, new HashSet<string>());

    public override LeaderNodeState CopyAsLeader(List<string> nodesIds) => new LeaderNodeState(_currentTerm, _votedFor, new List<LogEntry>(_log), _commitLength, _currentLeader, new Dictionary<string, int>(_nextIndex), new Dictionary<string, int>(_matchIndex), new Dictionary<string, IActorRef>(_pendingResponses), _majority);

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