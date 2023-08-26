namespace RaftCore.Messages;

public class CommandSuccessfullyCommited
{
    public CommandSuccessfullyCommited(string id, string command, int term)
    {
        Id = id;
        Command = command;
        Term = term;
    }

    public string Id { get; }

    public string Command { get; }

    public int Term { get; }
}