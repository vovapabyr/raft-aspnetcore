using Akka.Actor;

namespace RaftCore.States;

public class CandidateNodeState : NodeState
{
    private HashSet<string> _votesReceived = new HashSet<string>();

    public CandidateNodeState(int currentTerm, string? votedFor, IList<LogEntry> log, int commitLength, string? currentLeader, Dictionary<string, IActorRef> pendingResponses, HashSet<string> votesReceived) : base(currentTerm, votedFor, log, commitLength, currentLeader, pendingResponses)
    {
        _votesReceived = votesReceived;
    }

    public bool AddVote(string candidateId) => _votesReceived.Add(candidateId);

    public int VotesCount => _votesReceived.Count;

    public override NodeState Copy() => new CandidateNodeState(_currentTerm, _votedFor, new List<LogEntry>(_log), _commitLength, _currentLeader, new Dictionary<string, IActorRef>(_pendingResponses), new HashSet<string>(_votesReceived));

    public override NodeState CopyAsBase() => new NodeState(_currentTerm, _votedFor, new List<LogEntry>(_log), _commitLength, _currentLeader, new Dictionary<string, IActorRef>(_pendingResponses));

    public override CandidateNodeState CopyAsCandidate() => new CandidateNodeState(_currentTerm, _votedFor, new List<LogEntry>(_log), _commitLength, _currentLeader, new Dictionary<string, IActorRef>(_pendingResponses), new HashSet<string>(_votesReceived));

    public override LeaderNodeState CopyAsLeader(List<string> nodesIds) => new LeaderNodeState(_currentTerm, _votedFor, new List<LogEntry>(_log), _commitLength, _currentLeader, new Dictionary<string, IActorRef>(_pendingResponses), nodesIds); 
}