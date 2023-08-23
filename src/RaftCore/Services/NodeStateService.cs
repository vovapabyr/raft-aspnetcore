using Microsoft.Extensions.Logging;
using RaftCore.Commands;
using RaftCore.Common;

namespace RaftCore.Services;

public class NodeStateService
{
    // TODO Check against docs.
    #region Raft persisted

    private int _currentTerm = 0;

    private string? _votedFor = null;

    private IList<LogEntry> _log = new List<LogEntry>();

    private long _commitLength = 0;

    #endregion

    // TODO Check against docs.
    #region Raft not-persisted

    private string? _currentLeader = null;

    private HashSet<string> _votesReceived = new HashSet<string>();

    #endregion

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

    public bool AddVote(string candidateId) => _votesReceived.Add(candidateId);

    public void ClearVotes() => _votesReceived.Clear();

    public int VotesCount => _votesReceived.Count;

    public string? CurrentLeader 
    {
        get => _currentLeader;
        set => _currentLeader = value;
    }

    public (int, int) GetLastLogInfo()
    {

        var lastLogIndedx = _log.Count - 1;
        if (lastLogIndedx < 0)
            return (0, 0);

        var lastLogTerm = _log[lastLogIndedx].Term;

        return (lastLogIndedx, lastLogTerm);
    }

    public override string ToString()
    {
        return $"TERM: '{ CurrentTerm }'. LEADER: '{ CurrentLeader }'. VOTED_FOR: '{ VotedFor }'.";
    }
}