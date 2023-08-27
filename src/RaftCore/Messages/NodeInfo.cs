using RaftCore.Common;

namespace RaftCore.Messages;

public class NodeInfo
{
    public NodeInfo(string hostName, NodeRole role, int term, int commitLength, List<LogEntry> log)
    {
        HostName = hostName;
        Role = role;
        Term = term;
        Log = log;
        CommitLength = commitLength;
    }

    public string HostName { get; }
    
    public NodeRole Role { get; }

    public int Term { get; }

    public int CommitLength { get; }

    public List<LogEntry> Log { get; }
}