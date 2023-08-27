using RaftCore.Common;

namespace RaftCore.Messages;

public class NodeInfo
{
    public NodeInfo(NodeRole role, int term, int commitLength, List<LogEntry> log)
    {
        Role = role;
        Term = term;
        Log = log;
        CommitLength = commitLength;
    }

    public NodeRole Role { get; }

    public int Term { get; }

    public int CommitLength { get; }

    public List<LogEntry> Log { get; }
}