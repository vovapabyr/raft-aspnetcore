namespace RaftCore.Messages;

public class RedirectToLeader
{
    public RedirectToLeader(int term, string? leader)
    {
        Leader = leader;
        Term = term;
    }

    public int Term { get; }

    public string? Leader { get; }
}