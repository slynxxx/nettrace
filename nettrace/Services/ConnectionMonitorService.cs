using System.Buffers.Binary;
using System.ComponentModel;
using System.Net;
using NetTrace.Models;
using NetTrace.Native;

namespace NetTrace.Services;

/// <summary>
/// polls the tcp and udp endpoint tables through the ip helper api on a single background loop.
/// refreshes never overlap: a manual refresh only wakes the loop early.
/// </summary>
public sealed class ConnectionMonitorService : IDisposable
{
    private const int RecentCapacity = 50;

    private readonly ProcessInfoService _processes;
    private readonly SettingsService _settings;
    private readonly Dictionary<ConnectionKey, ConnectionEntry> _entries = new(512);
    private readonly HashSet<uint> _pids = new(128);
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly LinkedList<ConnectionEntry> _recent = new();
    private readonly object _recentSync = new();
    private readonly List<ConnectionEntry> _added = new(64);

    // reusable native table buffers; they only grow
    private byte[] _tcp4 = new byte[64 * 1024];
    private byte[] _tcp6 = new byte[32 * 1024];
    private byte[] _udp4 = new byte[16 * 1024];
    private byte[] _udp6 = new byte[16 * 1024];

    private int _generation;
    private bool _baselineTaken;
    private Task? _loop;
    private volatile ConnectionSnapshot _latest = ConnectionSnapshot.Empty;

    public ConnectionMonitorService(ProcessInfoService processes, SettingsService settings)
    {
        _processes = processes;
        _settings = settings;
        _settings.SettingChanged += OnSettingChanged;
    }

    /// <summary>raised on the monitor thread after every refresh.</summary>
    public event EventHandler<ConnectionSnapshot>? SnapshotUpdated;

    public ConnectionSnapshot Latest => _latest;

    /// <summary>most recently detected endpoints, newest first. endpoints that existed at startup are excluded.</summary>
    public IReadOnlyList<ConnectionEntry> GetRecentlyDetected(int count)
    {
        lock (_recentSync)
        {
            return _recent.Take(count).ToArray();
        }
    }

    public void Start()
    {
        _loop ??= Task.Run(() => RunAsync(_cts.Token));
    }

    /// <summary>wakes the loop for an immediate refresh. a refresh already in progress is not duplicated.</summary>
    public void RequestRefresh()
    {
        try
        {
            if (_wake.CurrentCount == 0)
            {
                _wake.Release();
            }
        }
        catch (SemaphoreFullException)
        {
        }
    }

    public void Dispose()
    {
        _settings.SettingChanged -= OnSettingChanged;
        _cts.Cancel();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        _cts.Dispose();
        _wake.Dispose();
    }

    private void OnSettingChanged(object? sender, string name)
    {
        if (name == nameof(AppSettings.RefreshIntervalSeconds))
        {
            RequestRefresh();
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            ConnectionSnapshot snapshot = Refresh();
            _latest = snapshot;

            try
            {
                SnapshotUpdated?.Invoke(this, snapshot);
            }
            catch (Exception ex)
            {
                // a failing subscriber must not stop monitoring
                System.Diagnostics.Debug.WriteLine($"snapshot subscriber failed: {ex}");
            }

            try
            {
                await _wake.WaitAsync(TimeSpan.FromSeconds(_settings.Current.RefreshIntervalSeconds), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private ConnectionSnapshot Refresh()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        int generation = ++_generation;
        _added.Clear();
        _pids.Clear();
        string? error = null;
        int tcp = 0, udp = 0;

        try
        {
            tcp += ReadTcp4(generation, now);
            tcp += ReadTcp6(generation, now);
            udp += ReadUdp4(generation, now);
            udp += ReadUdp6(generation, now);
        }
        catch (Win32Exception ex)
        {
            error = $"could not read the endpoint table: {ex.Message.ToLowerInvariant()}";
        }

        var entries = new ConnectionEntry[_entries.Count];
        int index = 0;
        foreach ((ConnectionKey key, ConnectionEntry entry) in _entries)
        {
            if (entry.Generation != generation)
            {
                // the endpoint disappeared since the previous pass
                _entries.Remove(key);
                continue;
            }

            entries[index++] = entry;
            _pids.Add(entry.Pid);
        }

        if (index != entries.Length)
        {
            Array.Resize(ref entries, index);
        }

        _processes.Retain(_pids);

        ConnectionEntry[] added = [];
        if (_baselineTaken && _added.Count > 0)
        {
            added = _added.ToArray();
            lock (_recentSync)
            {
                foreach (ConnectionEntry entry in added)
                {
                    _recent.AddFirst(entry);
                }

                while (_recent.Count > RecentCapacity)
                {
                    _recent.RemoveLast();
                }
            }
        }

        _baselineTaken = true;
        int processCount = _pids.Count - (_pids.Contains(0) ? 1 : 0);
        return new ConnectionSnapshot(now, entries, added, tcp, udp, processCount, error);
    }

    private int ReadTcp4(int generation, DateTimeOffset now)
    {
        Span<byte> table = Query(ref _tcp4, tcp: true, IpHelper.AF_INET);
        int count = RowCount(table, IpHelper.TcpRowV4Size);
        for (int i = 0; i < count; i++)
        {
            Span<byte> row = table.Slice(4 + (i * IpHelper.TcpRowV4Size), IpHelper.TcpRowV4Size);
            var state = (TcpConnectionState)BinaryPrimitives.ReadUInt32LittleEndian(row);
            Span<byte> local = row.Slice(4, 4);
            ushort localPort = IpHelper.NetworkPortToHost(BinaryPrimitives.ReadUInt32LittleEndian(row[8..]));
            Span<byte> remote = row.Slice(12, 4);
            ushort remotePort = IpHelper.NetworkPortToHost(BinaryPrimitives.ReadUInt32LittleEndian(row[16..]));
            uint pid = BinaryPrimitives.ReadUInt32LittleEndian(row[20..]);

            var key = new ConnectionKey(
                NetProtocol.Tcp, false,
                BinaryPrimitives.ReadUInt32BigEndian(local), 0, localPort,
                BinaryPrimitives.ReadUInt32BigEndian(remote), state == TcpConnectionState.Listen ? (ushort)0 : remotePort,
                state == TcpConnectionState.Listen ? pid : 0);
            Observe(key, pid, state, local, 0, remote, 0, generation, now);
        }

        return count;
    }

    private int ReadTcp6(int generation, DateTimeOffset now)
    {
        Span<byte> table = Query(ref _tcp6, tcp: true, IpHelper.AF_INET6);
        int count = RowCount(table, IpHelper.TcpRowV6Size);
        for (int i = 0; i < count; i++)
        {
            Span<byte> row = table.Slice(4 + (i * IpHelper.TcpRowV6Size), IpHelper.TcpRowV6Size);
            Span<byte> local = row[..16];
            uint localScope = BinaryPrimitives.ReadUInt32LittleEndian(row[16..]);
            ushort localPort = IpHelper.NetworkPortToHost(BinaryPrimitives.ReadUInt32LittleEndian(row[20..]));
            Span<byte> remote = row.Slice(24, 16);
            uint remoteScope = BinaryPrimitives.ReadUInt32LittleEndian(row[40..]);
            ushort remotePort = IpHelper.NetworkPortToHost(BinaryPrimitives.ReadUInt32LittleEndian(row[44..]));
            var state = (TcpConnectionState)BinaryPrimitives.ReadUInt32LittleEndian(row[48..]);
            uint pid = BinaryPrimitives.ReadUInt32LittleEndian(row[52..]);

            var key = new ConnectionKey(
                NetProtocol.Tcp, true,
                BinaryPrimitives.ReadUInt128BigEndian(local), localScope, localPort,
                BinaryPrimitives.ReadUInt128BigEndian(remote), state == TcpConnectionState.Listen ? (ushort)0 : remotePort,
                state == TcpConnectionState.Listen ? pid : 0);
            Observe(key, pid, state, local, localScope, remote, remoteScope, generation, now);
        }

        return count;
    }

    private int ReadUdp4(int generation, DateTimeOffset now)
    {
        Span<byte> table = Query(ref _udp4, tcp: false, IpHelper.AF_INET);
        int count = RowCount(table, IpHelper.UdpRowV4Size);
        for (int i = 0; i < count; i++)
        {
            Span<byte> row = table.Slice(4 + (i * IpHelper.UdpRowV4Size), IpHelper.UdpRowV4Size);
            Span<byte> local = row[..4];
            ushort localPort = IpHelper.NetworkPortToHost(BinaryPrimitives.ReadUInt32LittleEndian(row[4..]));
            uint pid = BinaryPrimitives.ReadUInt32LittleEndian(row[8..]);

            var key = new ConnectionKey(NetProtocol.Udp, false, BinaryPrimitives.ReadUInt32BigEndian(local), 0, localPort, 0, 0, pid);
            Observe(key, pid, TcpConnectionState.None, local, 0, [], 0, generation, now);
        }

        return count;
    }

    private int ReadUdp6(int generation, DateTimeOffset now)
    {
        Span<byte> table = Query(ref _udp6, tcp: false, IpHelper.AF_INET6);
        int count = RowCount(table, IpHelper.UdpRowV6Size);
        for (int i = 0; i < count; i++)
        {
            Span<byte> row = table.Slice(4 + (i * IpHelper.UdpRowV6Size), IpHelper.UdpRowV6Size);
            Span<byte> local = row[..16];
            uint localScope = BinaryPrimitives.ReadUInt32LittleEndian(row[16..]);
            ushort localPort = IpHelper.NetworkPortToHost(BinaryPrimitives.ReadUInt32LittleEndian(row[20..]));
            uint pid = BinaryPrimitives.ReadUInt32LittleEndian(row[24..]);

            var key = new ConnectionKey(NetProtocol.Udp, true, BinaryPrimitives.ReadUInt128BigEndian(local), localScope, localPort, 0, 0, pid);
            Observe(key, pid, TcpConnectionState.None, local, localScope, [], 0, generation, now);
        }

        return count;
    }

    private void Observe(
        in ConnectionKey key,
        uint pid,
        TcpConnectionState state,
        ReadOnlySpan<byte> local,
        uint localScope,
        ReadOnlySpan<byte> remote,
        uint remoteScope,
        int generation,
        DateTimeOffset now)
    {
        if (_entries.TryGetValue(key, out ConnectionEntry? entry))
        {
            if (entry.Generation == generation)
            {
                return; // duplicate row within the same table read
            }

            entry.Generation = generation;
            entry.State = state;
            entry.LastSeen = now;

            // time_wait sockets are reassigned to pid 0; keep the original owner in that case
            if (pid != 0 && entry.Process.Pid != pid)
            {
                entry.Process = _processes.Get(pid);
            }

            return;
        }

        string localText = FormatAddress(local, localScope);
        string remoteText = remote.IsEmpty ? string.Empty : FormatAddress(remote, remoteScope);
        entry = new ConnectionEntry(key, localText, remoteText, state, _processes.Get(pid), now)
        {
            Generation = generation,
        };
        _entries.Add(key, entry);
        _added.Add(entry);
    }

    private static string FormatAddress(ReadOnlySpan<byte> raw, uint scope)
    {
        IPAddress address = raw.Length == 4 ? new IPAddress(raw) : new IPAddress(raw, scope);
        return address.ToString();
    }

    private static int RowCount(Span<byte> table, int rowSize)
    {
        if (table.Length < 4)
        {
            return 0;
        }

        int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(table);
        int max = (table.Length - 4) / rowSize;
        return Math.Clamp(count, 0, max);
    }

    private static unsafe Span<byte> Query(ref byte[] buffer, bool tcp, int family)
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            int size = buffer.Length;
            uint result;
            fixed (byte* pointer = buffer)
            {
                result = tcp
                    ? IpHelper.GetExtendedTcpTable(pointer, ref size, false, family, IpHelper.TCP_TABLE_OWNER_PID_ALL, 0)
                    : IpHelper.GetExtendedUdpTable(pointer, ref size, false, family, IpHelper.UDP_TABLE_OWNER_PID, 0);
            }

            if (result == IpHelper.NO_ERROR)
            {
                return buffer.AsSpan(0, Math.Min(size, buffer.Length));
            }

            if (result == IpHelper.ERROR_INSUFFICIENT_BUFFER)
            {
                // the table can grow between the size query and the read; leave headroom
                buffer = new byte[size + (size / 2) + 4096];
                continue;
            }

            throw new Win32Exception((int)result);
        }

        throw new Win32Exception((int)IpHelper.ERROR_INSUFFICIENT_BUFFER);
    }
}
