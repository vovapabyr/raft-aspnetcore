
using RaftCore.Commands;

namespace RaftCore.Common;

public class LogEntry
{
    public LogEntry(ICommand command, int term)
    {
        Command = command;
        Term = term;
    }

    public ICommand Command { get; }

    public int Term { get; }
}