namespace NetTrace.Models;

public enum NetProtocol : byte
{
    Tcp = 0,
    Udp = 1,
}

/// <summary>values match the MIB_TCP_STATE enumeration.</summary>
public enum TcpConnectionState
{
    None = 0,
    Closed = 1,
    Listen = 2,
    SynSent = 3,
    SynReceived = 4,
    Established = 5,
    FinWait1 = 6,
    FinWait2 = 7,
    CloseWait = 8,
    Closing = 9,
    LastAck = 10,
    TimeWait = 11,
    DeleteTcb = 12,
}

/// <summary>
/// allocation-free identity of an endpoint row as returned by the ip helper api.
/// addresses are kept as raw 128-bit values so a refresh can match existing rows
/// without building strings or IPAddress instances.
/// <para>
/// <see cref="Owner"/> is the owning pid for udp endpoints and tcp listeners (several processes
/// may share those with address reuse) and zero for tcp connections, whose 4-tuple is unique.
/// this keeps a connection's identity when windows hands it to pid 0 in time_wait.
/// </para>
/// </summary>
public readonly record struct ConnectionKey(
    NetProtocol Protocol,
    bool IsIPv6,
    UInt128 LocalAddress,
    uint LocalScope,
    ushort LocalPort,
    UInt128 RemoteAddress,
    ushort RemotePort,
    uint Owner);

/// <summary>
/// a tcp connection or udp endpoint observed by the monitor. the instance is reused
/// across refreshes for as long as the endpoint stays alive.
/// </summary>
public sealed class ConnectionEntry
{
    public ConnectionEntry(
        ConnectionKey key,
        string localAddress,
        string remoteAddress,
        TcpConnectionState state,
        ProcessIdentity process,
        DateTimeOffset firstSeen)
    {
        Key = key;
        LocalAddress = localAddress;
        RemoteAddress = remoteAddress;
        State = state;
        Process = process;
        FirstSeen = firstSeen;
        LastSeen = firstSeen;

        LocalEndpoint = Helpers.EndpointFormatter.Format(localAddress, key.LocalPort, key.IsIPv6);
        RemoteEndpoint = key.Protocol == NetProtocol.Udp
            ? string.Empty
            : Helpers.EndpointFormatter.Format(remoteAddress, key.RemotePort, key.IsIPv6);
        ProtocolText = (key.Protocol, key.IsIPv6) switch
        {
            (NetProtocol.Tcp, false) => "tcp",
            (NetProtocol.Tcp, true) => "tcp6",
            (NetProtocol.Udp, false) => "udp",
            _ => "udp6",
        };
    }

    public ConnectionKey Key { get; }
    public NetProtocol Protocol => Key.Protocol;
    public bool IsIPv6 => Key.IsIPv6;
    public uint Pid => Process.Pid;

    /// <summary>refresh pass in which the entry was last seen; owned by the monitor.</summary>
    internal int Generation;
    public ushort LocalPort => Key.LocalPort;
    public ushort RemotePort => Key.RemotePort;

    public string LocalAddress { get; }
    public string RemoteAddress { get; }
    public string LocalEndpoint { get; }
    public string RemoteEndpoint { get; }
    public string ProtocolText { get; }

    public TcpConnectionState State { get; internal set; }
    public string StateText => Helpers.TcpStateNames.ToText(Protocol, State);

    public ProcessIdentity Process { get; internal set; }
    public DateTimeOffset FirstSeen { get; }
    public DateTimeOffset LastSeen { get; internal set; }

    /// <summary>true for remote endpoints that are loopback or unspecified.</summary>
    public bool IsLocalOnly =>
        Protocol == NetProtocol.Udp ||
        RemoteAddress is "0.0.0.0" or "::" or "127.0.0.1" or "::1";
}

public sealed class ConnectionSnapshot
{
    public static readonly ConnectionSnapshot Empty = new(DateTimeOffset.MinValue, [], [], 0, 0, 0, null);

    public ConnectionSnapshot(
        DateTimeOffset timestamp,
        IReadOnlyList<ConnectionEntry> entries,
        IReadOnlyList<ConnectionEntry> added,
        int tcpCount,
        int udpCount,
        int processCount,
        string? error)
    {
        Timestamp = timestamp;
        Entries = entries;
        Added = added;
        TcpCount = tcpCount;
        UdpCount = udpCount;
        ProcessCount = processCount;
        Error = error;
    }

    public DateTimeOffset Timestamp { get; }
    public IReadOnlyList<ConnectionEntry> Entries { get; }
    /// <summary>entries that appeared for the first time in this snapshot.</summary>
    public IReadOnlyList<ConnectionEntry> Added { get; }
    public int TcpCount { get; }
    public int UdpCount { get; }
    public int ProcessCount { get; }
    public string? Error { get; }
    public bool IsEmpty => Timestamp == DateTimeOffset.MinValue;
}
