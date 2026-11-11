namespace NetTrace.Models;

public readonly struct TrafficSample(DateTimeOffset timestamp, double receivedPerSecond, double sentPerSecond)
{
    public DateTimeOffset Timestamp { get; } = timestamp;
    public double ReceivedPerSecond { get; } = receivedPerSecond;
    public double SentPerSecond { get; } = sentPerSecond;
}

public sealed record NetworkInterfaceInfo(
    string Id,
    string Name,
    string Description,
    string TypeText,
    bool IsUp,
    long Speed)
{
    public const string AllInterfacesId = "";

    public static NetworkInterfaceInfo All { get; } = new(AllInterfacesId, "all interfaces", "combined counters of every active interface", "", true, 0);

    public string DisplayName => Id == AllInterfacesId ? Name : $"{Name.ToLowerInvariant()}";
}

public sealed class TrafficState
{
    public required double ReceivedPerSecond { get; init; }
    public required double SentPerSecond { get; init; }
    public required long SessionReceived { get; init; }
    public required long SessionSent { get; init; }
    public required long InterfaceReceived { get; init; }
    public required long InterfaceSent { get; init; }
    public required double PeakReceivedPerSecond { get; init; }
    public required double PeakSentPerSecond { get; init; }
    public required TrafficSample[] History { get; init; }
    public required string InterfaceName { get; init; }
    public string? Error { get; init; }
}
