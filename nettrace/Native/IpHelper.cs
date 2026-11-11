using System.Runtime.InteropServices;

namespace NetTrace.Native;

/// <summary>
/// p/invoke surface for the windows ip helper api (iphlpapi.dll).
/// only read-only table queries are used; nothing here modifies system state.
/// </summary>
internal static unsafe partial class IpHelper
{
    public const int AF_INET = 2;
    public const int AF_INET6 = 23;

    public const int TCP_TABLE_OWNER_PID_ALL = 5;
    public const int UDP_TABLE_OWNER_PID = 1;

    public const uint NO_ERROR = 0;
    public const uint ERROR_INSUFFICIENT_BUFFER = 122;

    // row layouts (all fields are dword aligned, the table starts with a dword entry count)
    public const int TcpRowV4Size = 24;   // MIB_TCPROW_OWNER_PID
    public const int TcpRowV6Size = 56;   // MIB_TCP6ROW_OWNER_PID
    public const int UdpRowV4Size = 12;   // MIB_UDPROW_OWNER_PID
    public const int UdpRowV6Size = 28;   // MIB_UDP6ROW_OWNER_PID

    [LibraryImport("iphlpapi.dll", SetLastError = false)]
    public static partial uint GetExtendedTcpTable(
        byte* pTcpTable,
        ref int pdwSize,
        [MarshalAs(UnmanagedType.Bool)] bool bOrder,
        int ulAf,
        int tableClass,
        uint reserved);

    [LibraryImport("iphlpapi.dll", SetLastError = false)]
    public static partial uint GetExtendedUdpTable(
        byte* pUdpTable,
        ref int pdwSize,
        [MarshalAs(UnmanagedType.Bool)] bool bOrder,
        int ulAf,
        int tableClass,
        uint reserved);

    /// <summary>ports are stored in network byte order in the low 16 bits of a dword.</summary>
    public static ushort NetworkPortToHost(uint port) =>
        (ushort)(((port & 0xFF) << 8) | ((port >> 8) & 0xFF));
}
