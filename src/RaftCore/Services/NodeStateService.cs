using Microsoft.Extensions.Logging;
using RaftCore.Commands;
using RaftCore.Common;

namespace RaftCore.Services;

public class NodeStateService
{
        // TODO Check against docs.
    #region Raft persisted

    private int _currentTerm = 0;

    private SemaphoreSlim _currentTermLock = new SemaphoreSlim(1);

    private string? _votedFor = null;

    private SemaphoreSlim _voteLock = new SemaphoreSlim(1);

    private IList<LogEntry> _log = new List<LogEntry>();

    private SemaphoreSlim _logLock = new SemaphoreSlim(1);

    private long _commitLength = 0;

    #endregion

    // TODO Check against docs.
    #region Raft not-persisted

    private string? _currentLeader = null;

    private HashSet<string> _votesReceived = new HashSet<string>();

    private readonly ILogger<NodeStateService> _logger;

    #endregion

    public NodeStateService(ILogger<NodeStateService> logger)
    {
        _logger = logger;        
    }

    public int CurrentTerm 
    {
        get { return _currentTerm; }
        set 
        {
            _currentTermLock.Wait(); 
            _logger.LogInformation($"Setting current term from '{ _currentTerm }' to '{ value }'.");
            _currentTerm = value;
            _currentTermLock.Release(); 
        }
    }

    public int IncrementTerm()
    {
        var incrementedTerm = Interlocked.Increment(ref _currentTerm);
        _logger.LogInformation($"Current term inremented. Value: '{ incrementedTerm }'.");
        return incrementedTerm;
    }

    public string? VotedFor => _votedFor;

    public void Vote(string? candidateId)
    {
        _voteLock.Wait();
        _logger.LogInformation($"Voting for '{ candidateId }'.");
        _votedFor = candidateId;
        _voteLock.Release();
    } 

    public (int, int) GetLastLogInfo()
    {
        _logLock.Wait();

        var lastLogIndedx = _log.Count - 1;
        var lastLogTerm = _log[lastLogIndedx].Term;

        _logLock.Release();

        return (lastLogIndedx, lastLogTerm);
    }
}