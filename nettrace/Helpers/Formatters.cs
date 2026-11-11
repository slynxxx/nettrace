using System.Globalization;
using NetTrace.Models;

namespace NetTrace.Helpers;

public static class EndpointFormatter
{
    public static string Format(string address, int port, bool isIPv6)
    {
        if (string.IsNullOrEmpty(address))
        {
            return string.Empty;
        }

        string portText = port == 0 ? "*" : port.ToString(CultureInfo.InvariantCulture);
        return isIPv6 ? $"[{address}]:{portText}" : $"{address}:{portText}";
    }
}

public static class TcpStateNames
{
    public static string ToText(NetProtocol protocol, TcpConnectionState state)
    {
        if (protocol == NetProtocol.Udp)
        {
            return "bound";
        }

        return state switch
        {
            TcpConnectionState.Closed => "closed",
            TcpConnectionState.Listen => "listening",
            TcpConnectionState.SynSent => "syn sent",
            TcpConnectionState.SynReceived => "syn received",
            TcpConnectionState.Established => "established",
            TcpConnectionState.FinWait1 => "fin wait 1",
            TcpConnectionState.FinWait2 => "fin wait 2",
            TcpConnectionState.CloseWait => "close wait",
            TcpConnectionState.Closing => "closing",
            TcpConnectionState.LastAck => "last ack",
            TcpConnectionState.TimeWait => "time wait",
            TcpConnectionState.DeleteTcb => "delete tcb",
            _ => "unknown",
        };
    }
}

public static class ByteFormatter
{
    private static readonly string[] Units = ["b", "kb", "mb", "gb", "tb"];

    public static string FormatBytes(double bytes)
    {
        if (bytes < 0 || double.IsNaN(bytes))
        {
            bytes = 0;
        }

        int unit = 0;
        while (bytes >= 1024 && unit < Units.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }

        string format = unit == 0 ? "0" : bytes >= 100 ? "0" : bytes >= 10 ? "0.0" : "0.00";
        return bytes.ToString(format, CultureInfo.InvariantCulture) + " " + Units[unit];
    }

    public static string FormatRate(double bytesPerSecond) => FormatBytes(bytesPerSecond) + "/s";

    public static string FormatLinkSpeed(long bitsPerSecond)
    {
        if (bitsPerSecond <= 0)
        {
            return "unknown speed";
        }

        return bitsPerSecond switch
        {
            >= 1_000_000_000 => (bitsPerSecond / 1_000_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + " gbps",
            >= 1_000_000 => (bitsPerSecond / 1_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + " mbps",
            _ => (bitsPerSecond / 1_000d).ToString("0.#", CultureInfo.InvariantCulture) + " kbps",
        };
    }
}

public static class TimeFormatter
{
    public static string Format(DateTimeOffset value)
    {
        if (value == DateTimeOffset.MinValue)
        {
            return string.Empty;
        }

        DateTimeOffset local = value.ToLocalTime();
        return local.Date == DateTimeOffset.Now.Date
            ? local.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
            : local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    public static string FormatRelative(DateTimeOffset value)
    {
        TimeSpan age = DateTimeOffset.Now - value;
        if (age < TimeSpan.FromSeconds(5))
        {
            return "just now";
        }

        if (age < TimeSpan.FromMinutes(1))
        {
            return $"{(int)age.TotalSeconds}s ago";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return $"{(int)age.TotalMinutes}m ago";
        }

        return Format(value);
    }
}
