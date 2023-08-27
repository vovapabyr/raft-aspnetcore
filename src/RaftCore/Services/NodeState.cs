using System.Collections.Immutable;
using Akka.Actor;

namespace RaftCore.Services;

public class NodeState
{
    // TODO Check against docs.
    #region Raft persisted

    protected int _currentTerm = 0;

    protected string? _votedFor = null;

    protected IList<LogEntry> _log = new List<LogEntry>();

    protected int _commitLength = 0;

    #endregion

    // TODO Check against docs.
    #region Raft not-persisted

    protected string? _currentLeader = null;

    // Relates to LeaderNodeState as only leader can receive and answear to clients. 
    // The reason it's in NodeState is because in case of leader -> follower -> leader again it should still be able to respond to client. 
    protected Dictionary<string, IActorRef> _pendingResponses = new Dictionary<string, IActorRef>();

    #endregion

    public NodeState() {}

    public NodeState(int currentTerm, string? votedFor, IList<LogEntry> log, int commitLength, string? currentLeader, Dictionary<string, IActorRef> pendingResponses)
    {
        _currentTerm = currentTerm;
        _votedFor = votedFor;
        _log = log;
        _commitLength = commitLength;
        _currentLeader = currentLeader;
        _pendingResponses = pendingResponses;
    }

    public int CurrentTerm 
    {
        get => _currentTerm;
        set => _currentTerm = value;
    }

    public int IncrementTerm()
    {
        return ++_currentTerm;
    }

    public string? VotedFor => _votedFor;

    public void Vote(string? candidateId)
    {
        _votedFor = candidateId;
    } 

    public bool CanVoteFor(string candidateId) => _votedFor == null || _votedFor == candidateId; 

    public string? CurrentLeader 
    {
        get => _currentLeader;
        set => _currentLeader = value;
    }

    public int LogCount => _log.Count;

    public LogEntry GetLogEntry(int index) => _log[index];

    public void CropLogEntry(int lastIndex)
    {
        _log = _log.Take(lastIndex).ToList();
    }

    public virtual void AddLog(LogEntry logEntry) => _log.Add(logEntry);

    public int CommitLength
    {
        get => _commitLength;
        set => _commitLength = value;
    }

    public (int, int) GetLastLogInfo()
    {

        var lastLogIndedx = _log.Count - 1;
        if (lastLogIndedx < 0)
            return (0, 0);

        var lastLogTerm = _log[lastLogIndedx].Term;

        return (lastLogIndedx, lastLogTerm);
    }

    public virtual NodeState Copy() => new NodeState(_currentTerm, _votedFor, new List<LogEntry>(_log), _commitLength, _currentLeader, new Dictionary<string, IActorRef>(_pendingResponses));

    public virtual NodeState CopyAsBase() => new NodeState(_currentTerm, _votedFor, new List<LogEntry>(_log), _commitLength, _currentLeader, new Dictionary<string, IActorRef>(_pendingResponses));

    public virtual CandidateNodeState CopyAsCandidate() => new CandidateNodeState(_currentTerm, _votedFor, new List<LogEntry>(_log), _commitLength, _currentLeader, new Dictionary<string, IActorRef>(_pendingResponses), new HashSet<string>());

    public virtual LeaderNodeState CopyAsLeader(List<string> nodesIds) => new LeaderNodeState(_currentTerm, _votedFor, new List<LogEntry>(_log), _commitLength, _currentLeader, new Dictionary<string, IActorRef>(_pendingResponses), nodesIds);  

    public override string ToString()
    {
        return $"TERM: '{ CurrentTerm }'. LEADER: '{ CurrentLeader }'. VOTED_FOR: '{ VotedFor }'. LOG_COUNT: '{ LogCount }'. COMMIT_LENGTH: '{ CommitLength }'.";
    }
}