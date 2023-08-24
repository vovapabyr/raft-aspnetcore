using RaftCore.Common;

namespace RaftCore.Services;

public class CandidateNodeState : NodeState
{
    private HashSet<string> _votesReceived = new HashSet<string>();

    public CandidateNodeState(int currentTerm, string? votedFor, IList<LogEntry> log, long commitLength, string? currentLeader, HashSet<string> votesReceived) : base(currentTerm, votedFor, log, commitLength, currentLeader)
    {
        _votesReceived = votesReceived;
    }

    public bool AddVote(string candidateId) => _votesReceived.Add(candidateId);

    public int VotesCount => _votesReceived.Count;

    public override NodeState Copy() => new CandidateNodeState(_currentTerm, _votedFor, new List<LogEntry>(_log), _commitLength, _currentLeader, new HashSet<string>(_votesReceived));

    public override NodeState CopyAsBase() => new NodeState(_currentTerm, _votedFor, new List<LogEntry>(_log), _commitLength, _currentLeader);

    public override CandidateNodeState CopyAsCandidate() => new CandidateNodeState(_currentTerm, _votedFor, new List<LogEntry>(_log), _commitLength, _currentLeader, new HashSet<string>(_votesReceived)); 
}