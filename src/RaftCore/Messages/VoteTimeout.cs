namespace RaftCore.Messages;

// Need to different timeout messages AppendEntriesTimeout and VoteTimeout, so that Leader can ignore VoteTimeout sent after becomming Leader (send by timer),
// and vice versa so that Follower, Candidate can ignore AppendEntriesTimeout sent after downgrading from leader (sent bi timer).
// Other approach would be to use single StateTimeout message, but then we need to think of all the places where to cancel timers.   
public class VoteTimeout
{
    private VoteTimeout(){ }

    public static VoteTimeout Instance => new VoteTimeout();
}