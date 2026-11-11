using System.Diagnostics;
using System.Net.NetworkInformation;
using NetTrace.Models;

namespace NetTrace.Services;

/// <summary>
/// samples system-wide interface byte counters. windows does not expose per-process byte
/// counts to unprivileged apps, so every figure here is per interface (or all interfaces).
/// </summary>
public sealed class TrafficMonitorService : IDisposable
{
    public const int HistoryCapacity = 300;

    private readonly SettingsService _settings;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly Dictionary<string, (long Received, long Sent)> _baseline = new();
    private readonly TrafficSample[] _ring = new TrafficSample[HistoryCapacity];
    private readonly Stopwatch _clock = new();
    private readonly object _sync = new();
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private readonly List<string> _stale = new();

    private NetworkInterface[] _interfaces = [];
    private volatile bool _interfacesDirty = true;
    private DateTime _lastInterfaceScan = DateTime.MinValue;
    private string _activeSelection = string.Empty;
    private int _ringStart;
    private int _ringCount;
    private long _sessionReceived;
    private long _sessionSent;
    private double _peakReceived;
    private double _peakSent;
    private Task? _loop;
    private volatile IReadOnlyList<NetworkInterfaceInfo> _interfaceInfos = [NetworkInterfaceInfo.All];

    public TrafficMonitorService(SettingsService settings)
    {
        _settings = settings;
        _settings.SettingChanged += OnSettingChanged;
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
    }

    /// <summary>raised on the sampling thread after every sample.</summary>
    public event EventHandler<TrafficState>? Updated;

    /// <summary>raised when the set of interfaces or their status changes.</summary>
    public event EventHandler? InterfacesChanged;

    public TrafficState? Latest { get; private set; }

    /// <summary>"all interfaces" followed by every non-loopback interface.</summary>
    public IReadOnlyList<NetworkInterfaceInfo> Interfaces => _interfaceInfos;

    public int UpInterfaceCount { get; private set; }

    public int TotalInterfaceCount { get; private set; }

    public void Start()
    {
        _loop ??= Task.Run(() => RunAsync(_cts.Token));
    }

    public void Dispose()
    {
        _settings.SettingChanged -= OnSettingChanged;
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
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

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        _interfacesDirty = true;
        Wake();
    }

    private void OnSettingChanged(object? sender, string name)
    {
        if (name is nameof(AppSettings.SelectedInterfaceId) or nameof(AppSettings.TrafficSampleIntervalMs))
        {
            Wake();
        }
    }

    private void Wake()
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
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TrafficState state;
            try
            {
                state = Sample();
            }
            catch (Exception ex) when (ex is NetworkInformationException or InvalidOperationException)
            {
                state = Snapshot(0, 0, 0, 0, "interface statistics are unavailable: " + ex.Message.ToLowerInvariant());
            }

            Latest = state;
            try
            {
                Updated?.Invoke(this, state);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"traffic subscriber failed: {ex}");
            }

            try
            {
                await _wake.WaitAsync(_settings.Current.TrafficSampleIntervalMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private TrafficState Sample()
    {
        if (_interfacesDirty || DateTime.UtcNow - _lastInterfaceScan > TimeSpan.FromSeconds(15))
        {
            ScanInterfaces();
        }

        string selection = _settings.Current.SelectedInterfaceId;
        if (selection != _activeSelection)
        {
            ResetSession(selection);
        }

        double elapsed = _clock.IsRunning ? _clock.Elapsed.TotalSeconds : 0;
        _clock.Restart();

        long deltaReceived = 0, deltaSent = 0, totalReceived = 0, totalSent = 0;
        HashSet<string> seen = _seen;
        seen.Clear();

        foreach (NetworkInterface nic in _interfaces)
        {
            if (!Includes(nic, selection))
            {
                continue;
            }

            IPInterfaceStatistics stats;
            try
            {
                stats = nic.GetIPStatistics();
            }
            catch (NetworkInformationException)
            {
                continue; // the adapter went away between the scan and the read
            }

            long received = stats.BytesReceived;
            long sent = stats.BytesSent;
            totalReceived += received;
            totalSent += sent;
            seen.Add(nic.Id);

            if (_baseline.TryGetValue(nic.Id, out (long Received, long Sent) previous))
            {
                // counters can reset when an adapter restarts; never report negative traffic
                deltaReceived += Math.Max(0, received - previous.Received);
                deltaSent += Math.Max(0, sent - previous.Sent);
            }

            _baseline[nic.Id] = (received, sent);
        }

        _stale.Clear();
        foreach (string id in _baseline.Keys)
        {
            if (!seen.Contains(id))
            {
                _stale.Add(id);
            }
        }

        foreach (string id in _stale)
        {
            _baseline.Remove(id);
        }

        double rx = 0, tx = 0;
        if (elapsed > 0.05)
        {
            rx = deltaReceived / elapsed;
            tx = deltaSent / elapsed;
            _sessionReceived += deltaReceived;
            _sessionSent += deltaSent;
            _peakReceived = Math.Max(_peakReceived, rx);
            _peakSent = Math.Max(_peakSent, tx);
            Push(new TrafficSample(DateTimeOffset.Now, rx, tx));
        }

        string? error = seen.Count == 0
            ? selection.Length == 0 ? "no active network interfaces" : "the selected interface is not available"
            : null;
        return Snapshot(rx, tx, totalReceived, totalSent, error);
    }

    private static bool Includes(NetworkInterface nic, string selection)
    {
        if (selection.Length > 0)
        {
            return nic.Id == selection;
        }

        return nic.OperationalStatus == OperationalStatus.Up &&
               nic.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel);
    }

    private void ScanInterfaces()
    {
        _interfacesDirty = false;
        _lastInterfaceScan = DateTime.UtcNow;

        NetworkInterface[] all;
        try
        {
            all = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            all = [];
        }

        NetworkInterface[] relevant = all
            .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .ToArray();

        var infos = new List<NetworkInterfaceInfo>(relevant.Length + 1) { NetworkInterfaceInfo.All };
        infos.AddRange(relevant
            .OrderByDescending(n => n.OperationalStatus == OperationalStatus.Up)
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .Select(n => new NetworkInterfaceInfo(
                n.Id,
                n.Name,
                n.Description,
                DescribeType(n.NetworkInterfaceType),
                n.OperationalStatus == OperationalStatus.Up,
                SafeSpeed(n))));

        bool changed = !infos.SequenceEqual(_interfaceInfos);
        _interfaces = relevant;
        TotalInterfaceCount = relevant.Length;
        UpInterfaceCount = relevant.Count(n => n.OperationalStatus == OperationalStatus.Up);

        if (changed)
        {
            _interfaceInfos = infos;
            InterfacesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static long SafeSpeed(NetworkInterface nic)
    {
        try
        {
            return nic.Speed;
        }
        catch (NetworkInformationException)
        {
            return 0;
        }
    }

    private static string DescribeType(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT
            or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit => "ethernet",
        NetworkInterfaceType.Wireless80211 => "wi-fi",
        NetworkInterfaceType.Tunnel => "tunnel",
        NetworkInterfaceType.Ppp => "ppp",
        NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2 => "cellular",
        _ => type.ToString().ToLowerInvariant(),
    };

    private void ResetSession(string selection)
    {
        lock (_sync)
        {
            _activeSelection = selection;
            _baseline.Clear();
            _clock.Reset();
            _ringStart = 0;
            _ringCount = 0;
            _sessionReceived = 0;
            _sessionSent = 0;
            _peakReceived = 0;
            _peakSent = 0;
        }
    }

    private void Push(TrafficSample sample)
    {
        lock (_sync)
        {
            int index = (_ringStart + _ringCount) % HistoryCapacity;
            _ring[index] = sample;
            if (_ringCount < HistoryCapacity)
            {
                _ringCount++;
            }
            else
            {
                _ringStart = (_ringStart + 1) % HistoryCapacity;
            }
        }
    }

    private TrafficState Snapshot(double rx, double tx, long totalReceived, long totalSent, string? error)
    {
        TrafficSample[] history;
        lock (_sync)
        {
            history = new TrafficSample[_ringCount];
            for (int i = 0; i < _ringCount; i++)
            {
                history[i] = _ring[(_ringStart + i) % HistoryCapacity];
            }
        }

        string selection = _activeSelection;
        string name = selection.Length == 0
            ? NetworkInterfaceInfo.All.Name
            : _interfaceInfos.FirstOrDefault(i => i.Id == selection)?.DisplayName ?? "unavailable interface";

        return new TrafficState
        {
            ReceivedPerSecond = rx,
            SentPerSecond = tx,
            SessionReceived = _sessionReceived,
            SessionSent = _sessionSent,
            InterfaceReceived = totalReceived,
            InterfaceSent = totalSent,
            PeakReceivedPerSecond = _peakReceived,
            PeakSentPerSecond = _peakSent,
            History = history,
            InterfaceName = name,
            Error = error,
        };
    }
}
