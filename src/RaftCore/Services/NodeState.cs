using System.Drawing;
using Microsoft.Extensions.Logging;
using RaftCore.Commands;
using RaftCore.Common;

namespace RaftCore.Services;

public class NodeState
{
    // TODO Check against docs.
    #region Raft persisted

    protected int _currentTerm = 0;

    protected string? _votedFor = null;

    protected IList<LogEntry> _log = new List<LogEntry>();

    protected long _commitLength = 0;

    #endregion

    // TODO Check against docs.
    #region Raft not-persisted

    protected string? _currentLeader = null;

    #endregion

    public NodeState() {}

    public NodeState(int currentTerm, string? votedFor, IList<LogEntry> log, long commitLength, string? currentLeader)
    {
        _currentTerm = currentTerm;
        _votedFor = votedFor;
        _log = log;
        _commitLength = commitLength;
        _currentLeader = currentLeader;
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

    public IList<LogEntry> Log => _log;

    public long CommitLength => _commitLength;

    public (int, int) GetLastLogInfo()
    {

        var lastLogIndedx = _log.Count - 1;
        if (lastLogIndedx < 0)
            return (0, 0);

        var lastLogTerm = _log[lastLogIndedx].Term;

        return (lastLogIndedx, lastLogTerm);
    }

    public virtual NodeState Copy() => new NodeState(_currentTerm, _votedFor, new List<LogEntry>(_log), _commitLength, _currentLeader);

    public virtual NodeState CopyAsBase() => new NodeState(_currentTerm, _votedFor, new List<LogEntry>(_log), _commitLength, _currentLeader);

    public virtual CandidateNodeState CopyAsCandidate() => new CandidateNodeState(_currentTerm, _votedFor, new List<LogEntry>(_log), _commitLength, _currentLeader, new HashSet<string>()); 

    public override string ToString()
    {
        return $"TERM: '{ CurrentTerm }'. LEADER: '{ CurrentLeader }'. VOTED_FOR: '{ VotedFor }'.";
    }
}