using System.Net;

namespace RaftNode.Models;

public class NodeInfo 
{
    public NodeInfo(string hostName, IPAddress iPAddress)
    {
        HostName = hostName;
        IPAddress = iPAddress;
    }

    public IPAddress IPAddress { get; set; }

    public string HostName { get; set; }

    public override string ToString() => $"{{ { HostName }, { IPAddress } }}";
}