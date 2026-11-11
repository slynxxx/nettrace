using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using NetTrace.Models;

namespace NetTrace.Services;

/// <summary>
/// forward and reverse lookups through the system resolver, with a short-lived result cache
/// so repeated queries do not hit the network again.
/// </summary>
public sealed class DnsService
{
    private const int RecentCapacity = 20;
    private static readonly TimeSpan SuccessTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FailureTtl = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<(string Query, DnsLookupMode Mode, DnsAddressFamily Family), (DnsLookupResult Result, DateTimeOffset Expires)> _cache = new();
    private readonly List<DnsLookupResult> _recent = new();
    private readonly object _recentSync = new();

    public event EventHandler? RecentChanged;

    public IReadOnlyList<DnsLookupResult> Recent
    {
        get
        {
            lock (_recentSync)
            {
                return _recent.ToArray();
            }
        }
    }

    public void ClearRecent()
    {
        lock (_recentSync)
        {
            _recent.Clear();
        }

        _cache.Clear();
        RecentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>normalizes user input: trims, strips a url scheme and path, and a trailing dot.</summary>
    public static string Normalize(string input)
    {
        string query = input.Trim();
        if (query.Contains("://", StringComparison.Ordinal) && Uri.TryCreate(query, UriKind.Absolute, out Uri? uri))
        {
            query = uri.Host;
        }

        if (query.StartsWith('[') && query.EndsWith(']'))
        {
            query = query[1..^1];
        }

        return query.TrimEnd('.').ToLowerInvariant();
    }

    public static DnsLookupMode ResolveMode(string query, DnsLookupMode mode) =>
        mode != DnsLookupMode.Auto ? mode : IPAddress.TryParse(query, out _) ? DnsLookupMode.Reverse : DnsLookupMode.Forward;

    public async Task<DnsLookupResult> LookupAsync(string input, DnsLookupMode mode, DnsAddressFamily family, CancellationToken ct)
    {
        string query = Normalize(input);
        DnsLookupMode resolvedMode = ResolveMode(query, mode);
        var key = (query, resolvedMode, resolvedMode == DnsLookupMode.Reverse ? DnsAddressFamily.Any : family);

        if (_cache.TryGetValue(key, out var cached) && cached.Expires > DateTimeOffset.Now)
        {
            var hit = new DnsLookupResult
            {
                Query = cached.Result.Query,
                Mode = cached.Result.Mode,
                Family = cached.Result.Family,
                Records = cached.Result.Records,
                Timestamp = cached.Result.Timestamp,
                Duration = cached.Result.Duration,
                Error = cached.Result.Error,
                FromCache = true,
            };
            AddRecent(hit);
            return hit;
        }

        var stopwatch = Stopwatch.StartNew();
        DnsLookupResult result;
        string? validation = Validate(query, resolvedMode);
        if (validation is not null)
        {
            result = Failure(query, resolvedMode, key.Item3, validation, stopwatch.Elapsed);
            AddRecent(result);
            return result;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);

        try
        {
            IReadOnlyList<DnsRecord> records = resolvedMode == DnsLookupMode.Reverse
                ? await ReverseAsync(query, timeout.Token).ConfigureAwait(false)
                : await ForwardAsync(query, family, timeout.Token).ConfigureAwait(false);

            result = records.Count == 0
                ? Failure(query, resolvedMode, key.Item3, resolvedMode == DnsLookupMode.Reverse ? "no ptr record for this address" : "no records of the requested type", stopwatch.Elapsed)
                : new DnsLookupResult
                {
                    Query = query,
                    Mode = resolvedMode,
                    Family = key.Item3,
                    Records = records,
                    Timestamp = DateTimeOffset.Now,
                    Duration = stopwatch.Elapsed,
                };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // superseded by a newer lookup; nothing to cache or record
        }
        catch (OperationCanceledException)
        {
            result = Failure(query, resolvedMode, key.Item3, "the lookup timed out", stopwatch.Elapsed);
        }
        catch (SocketException ex)
        {
            result = Failure(query, resolvedMode, key.Item3, Describe(ex), stopwatch.Elapsed);
        }
        catch (ArgumentException)
        {
            result = Failure(query, resolvedMode, key.Item3, "not a valid host name or address", stopwatch.Elapsed);
        }

        _cache[key] = (result, DateTimeOffset.Now + (result.Succeeded ? SuccessTtl : FailureTtl));
        AddRecent(result);
        return result;
    }

    private static async Task<IReadOnlyList<DnsRecord>> ForwardAsync(string host, DnsAddressFamily family, CancellationToken ct)
    {
        AddressFamily addressFamily = family switch
        {
            DnsAddressFamily.IPv4 => AddressFamily.InterNetwork,
            DnsAddressFamily.IPv6 => AddressFamily.InterNetworkV6,
            _ => AddressFamily.Unspecified,
        };

        IPHostEntry entry = await Dns.GetHostEntryAsync(host, addressFamily, ct).ConfigureAwait(false);
        var records = new List<DnsRecord>();

        if (!string.IsNullOrEmpty(entry.HostName) && !string.Equals(entry.HostName.TrimEnd('.'), host, StringComparison.OrdinalIgnoreCase))
        {
            records.Add(new DnsRecord("canonical", entry.HostName.ToLowerInvariant()));
        }

        foreach (string alias in entry.Aliases)
        {
            records.Add(new DnsRecord("alias", alias.ToLowerInvariant()));
        }

        foreach (IPAddress address in entry.AddressList
                     .OrderBy(a => a.AddressFamily == AddressFamily.InterNetworkV6)
                     .DistinctBy(a => a.ToString()))
        {
            records.Add(new DnsRecord(address.AddressFamily == AddressFamily.InterNetworkV6 ? "aaaa" : "a", address.ToString()));
        }

        return records;
    }

    private static async Task<IReadOnlyList<DnsRecord>> ReverseAsync(string address, CancellationToken ct)
    {
        IPHostEntry entry = await Dns.GetHostEntryAsync(address, ct).ConfigureAwait(false);
        var records = new List<DnsRecord>();

        // when no ptr exists windows echoes the address back as the host name
        if (!string.IsNullOrEmpty(entry.HostName) && !IPAddress.TryParse(entry.HostName, out _))
        {
            records.Add(new DnsRecord("ptr", entry.HostName.ToLowerInvariant()));
        }

        foreach (string alias in entry.Aliases)
        {
            records.Add(new DnsRecord("alias", alias.ToLowerInvariant()));
        }

        return records;
    }

    private static string? Validate(string query, DnsLookupMode mode)
    {
        if (query.Length == 0)
        {
            return "enter a host name or an ip address";
        }

        if (mode == DnsLookupMode.Reverse)
        {
            return IPAddress.TryParse(query, out _) ? null : "reverse lookups need an ipv4 or ipv6 address";
        }

        if (IPAddress.TryParse(query, out _))
        {
            return "forward lookups need a host name. use auto or reverse for an address";
        }

        return Uri.CheckHostName(query) == UriHostNameType.Dns ? null : "not a valid host name";
    }

    private static string Describe(SocketException ex) => ex.SocketErrorCode switch
    {
        SocketError.HostNotFound => "no such host",
        SocketError.TryAgain => "temporary dns failure, try again",
        SocketError.NoData => "no records of the requested type",
        SocketError.NoRecovery => "the dns server refused the query",
        SocketError.NetworkDown or SocketError.NetworkUnreachable => "the network is unreachable",
        _ => ex.Message.ToLowerInvariant(),
    };

    private static DnsLookupResult Failure(string query, DnsLookupMode mode, DnsAddressFamily family, string error, TimeSpan duration) => new()
    {
        Query = query,
        Mode = mode,
        Family = family,
        Records = [],
        Timestamp = DateTimeOffset.Now,
        Duration = duration,
        Error = error,
    };

    private void AddRecent(DnsLookupResult result)
    {
        if (result.Query.Length == 0)
        {
            return;
        }

        lock (_recentSync)
        {
            _recent.RemoveAll(r => r.Query == result.Query && r.Mode == result.Mode && r.Family == result.Family);
            _recent.Insert(0, result);
            if (_recent.Count > RecentCapacity)
            {
                _recent.RemoveRange(RecentCapacity, _recent.Count - RecentCapacity);
            }
        }

        RecentChanged?.Invoke(this, EventArgs.Empty);
    }
}
