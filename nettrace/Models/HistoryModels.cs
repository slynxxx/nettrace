namespace NetTrace.Models;

public sealed class HistoryRecord
{
    public long Id { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public uint Pid { get; init; }
    public string Protocol { get; init; } = string.Empty;
    public string LocalAddress { get; init; } = string.Empty;
    public int LocalPort { get; init; }
    public string RemoteAddress { get; init; } = string.Empty;
    public int RemotePort { get; init; }
    public string State { get; init; } = string.Empty;
    public DateTimeOffset FirstSeen { get; init; }
    public DateTimeOffset LastSeen { get; init; }

    public bool IsUdp => Protocol.StartsWith("udp", StringComparison.Ordinal);
    public bool IsIPv6 => Protocol.EndsWith('6');

    public string LocalEndpoint => Helpers.EndpointFormatter.Format(LocalAddress, LocalPort, IsIPv6);
    public string RemoteEndpoint => IsUdp ? string.Empty : Helpers.EndpointFormatter.Format(RemoteAddress, RemotePort, IsIPv6);
    public string ProcessText => $"{ProcessName}  ·  {Pid}";
    public string FirstSeenText => Helpers.TimeFormatter.Format(FirstSeen);
    public string LastSeenText => Helpers.TimeFormatter.Format(LastSeen);
}

public enum HistoryRange
{
    LastHour,
    Last24Hours,
    Last7Days,
    All,
}

public sealed record HistoryQuery(string? Search, string? Protocol, HistoryRange Range, int Limit);

public sealed record HistoryPage(IReadOnlyList<HistoryRecord> Records, long MatchingCount, long TotalCount);
