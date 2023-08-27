namespace RaftCore.Messages;

public class AddNewCommand
{
    public AddNewCommand(string command)
    {
        Command = command;
    }

    public string Command { get; }
}