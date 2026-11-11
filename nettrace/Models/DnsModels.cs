namespace NetTrace.Models;

public enum DnsLookupMode
{
    /// <summary>reverse lookup when the query parses as an ip address, forward otherwise.</summary>
    Auto,
    Forward,
    Reverse,
}

public enum DnsAddressFamily
{
    Any,
    IPv4,
    IPv6,
}

public sealed record DnsRecord(string Type, string Value);

public sealed class DnsLookupResult
{
    public required string Query { get; init; }
    public required DnsLookupMode Mode { get; init; }
    public required DnsAddressFamily Family { get; init; }
    public required IReadOnlyList<DnsRecord> Records { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required TimeSpan Duration { get; init; }
    public string? Error { get; init; }
    public bool FromCache { get; init; }

    public bool Succeeded => Error is null;

    public string ModeText => Mode == DnsLookupMode.Reverse ? "reverse" : "forward";
    public string Summary => Error is not null
        ? Error
        : Records.Count == 1 ? "1 record" : $"{Records.Count} records";
}
