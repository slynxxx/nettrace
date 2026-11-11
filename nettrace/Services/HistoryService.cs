using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NetTrace.Helpers;
using NetTrace.Models;

namespace NetTrace.Services;

public enum ExportFormat
{
    Csv,
    Json,
}

/// <summary>
/// sqlite-backed connection history. every row is one observed lifetime of an endpoint:
/// inserted when it first appears, its last-seen time refreshed while it stays alive.
/// a single connection is kept open and guarded so the monitor and the ui never collide.
/// </summary>
public sealed class HistoryService : IDisposable
{
    private static readonly TimeSpan TouchInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(1);

    private readonly SettingsService _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<ConnectionEntry, Tracked> _tracked = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ConnectionEntry> _alive = new(ReferenceEqualityComparer.Instance);
    private readonly List<ConnectionEntry> _gone = new();
    private readonly SqliteConnection _connection;
    private readonly SqliteCommand _insert;
    private readonly SqliteCommand _touch;
    private DateTimeOffset _lastPrune = DateTimeOffset.MinValue;
    private bool _disposed;

    public HistoryService(SettingsService settings)
        : this(settings, AppPaths.HistoryDatabase)
    {
    }

    public HistoryService(SettingsService settings, string databasePath)
    {
        _settings = settings;
        DatabasePath = databasePath;

        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        _connection.Open();

        Execute("""
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            CREATE TABLE IF NOT EXISTS connections (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                process_name    TEXT    NOT NULL,
                pid             INTEGER NOT NULL,
                protocol        TEXT    NOT NULL,
                local_address   TEXT    NOT NULL,
                local_port      INTEGER NOT NULL,
                remote_address  TEXT    NOT NULL,
                remote_port     INTEGER NOT NULL,
                state           TEXT    NOT NULL,
                first_seen      INTEGER NOT NULL,
                last_seen       INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_connections_last_seen ON connections (last_seen);
            """);

        _insert = _connection.CreateCommand();
        _insert.CommandText = """
            INSERT INTO connections (process_name, pid, protocol, local_address, local_port, remote_address, remote_port, state, first_seen, last_seen)
            VALUES ($name, $pid, $protocol, $local, $lport, $remote, $rport, $state, $first, $last);
            SELECT last_insert_rowid();
            """;
        foreach (string p in new[] { "$name", "$pid", "$protocol", "$local", "$lport", "$remote", "$rport", "$state", "$first", "$last" })
        {
            _insert.Parameters.Add(new SqliteParameter { ParameterName = p });
        }

        _touch = _connection.CreateCommand();
        _touch.CommandText = "UPDATE connections SET last_seen = $last, state = $state WHERE id = $id;";
        _touch.Parameters.Add(new SqliteParameter { ParameterName = "$last" });
        _touch.Parameters.Add(new SqliteParameter { ParameterName = "$state" });
        _touch.Parameters.Add(new SqliteParameter { ParameterName = "$id" });

        _settings.SettingChanged += OnSettingChanged;
    }

    /// <summary>raised after history rows are cleared or pruned.</summary>
    public event EventHandler? Changed;

    public string DatabasePath { get; }

    /// <summary>
    /// records a monitor snapshot. called on the monitor thread, so writes never overlap refreshes.
    /// </summary>
    public void Record(ConnectionSnapshot snapshot)
    {
        if (!_settings.Current.HistoryCollectionEnabled || snapshot.Error is not null)
        {
            return;
        }

        _gate.Wait();
        try
        {
            if (_disposed)
            {
                return;
            }

            using SqliteTransaction transaction = _connection.BeginTransaction();
            _insert.Transaction = transaction;
            _touch.Transaction = transaction;

            _alive.Clear();
            foreach (ConnectionEntry entry in snapshot.Entries)
            {
                _alive.Add(entry);
                string state = entry.StateText;

                if (!_tracked.TryGetValue(entry, out Tracked? tracked))
                {
                    long id = Insert(entry, state);
                    _tracked[entry] = new Tracked(id, state, entry.LastSeen);
                    continue;
                }

                if (tracked.State != state || entry.LastSeen - tracked.Written >= TouchInterval)
                {
                    Touch(tracked.Id, entry.LastSeen, state);
                    tracked.State = state;
                    tracked.Written = entry.LastSeen;
                }
            }

            // endpoints that disappeared get their final last-seen time
            _gone.Clear();
            foreach ((ConnectionEntry entry, Tracked tracked) in _tracked)
            {
                if (!_alive.Contains(entry))
                {
                    _gone.Add(entry);
                    if (tracked.Written != entry.LastSeen)
                    {
                        Touch(tracked.Id, entry.LastSeen, tracked.State);
                    }
                }
            }

            foreach (ConnectionEntry entry in _gone)
            {
                _tracked.Remove(entry);
            }

            transaction.Commit();
        }
        catch (SqliteException ex)
        {
            System.Diagnostics.Debug.WriteLine($"history write failed: {ex.Message}");
            _tracked.Clear();
        }
        finally
        {
            _insert.Transaction = null;
            _touch.Transaction = null;
            _gate.Release();
        }

        if (DateTimeOffset.Now - _lastPrune > PruneInterval)
        {
            _ = PruneAsync(CancellationToken.None);
        }
    }

    public async Task<HistoryPage> QueryAsync(HistoryQuery query, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return await Task.Run(() =>
            {
                using SqliteCommand command = _connection.CreateCommand();
                string where = BuildWhere(command, query);

                command.CommandText = $"SELECT COUNT(*) FROM connections {where};";
                long matching = (long)(command.ExecuteScalar() ?? 0L);

                command.CommandText = "SELECT COUNT(*) FROM connections;";
                long total = (long)(command.ExecuteScalar() ?? 0L);

                command.CommandText = $"""
                    SELECT id, process_name, pid, protocol, local_address, local_port, remote_address, remote_port, state, first_seen, last_seen
                    FROM connections {where}
                    ORDER BY last_seen DESC
                    LIMIT $limit;
                    """;
                command.Parameters.AddWithValue("$limit", query.Limit);

                var records = new List<HistoryRecord>(Math.Min(query.Limit, 1024));
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    records.Add(ReadRecord(reader));
                }

                return new HistoryPage(records, matching, total);
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<long> ExportAsync(HistoryQuery query, Stream destination, ExportFormat format, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return await Task.Run(async () =>
            {
                using SqliteCommand command = _connection.CreateCommand();
                string where = BuildWhere(command, query);
                command.CommandText = $"""
                    SELECT id, process_name, pid, protocol, local_address, local_port, remote_address, remote_port, state, first_seen, last_seen
                    FROM connections {where}
                    ORDER BY first_seen ASC;
                    """;

                using SqliteDataReader reader = command.ExecuteReader();
                return format == ExportFormat.Csv
                    ? await WriteCsvAsync(reader, destination, ct).ConfigureAwait(false)
                    : await WriteJsonAsync(reader, destination, ct).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await Task.Run(() =>
            {
                Execute("DELETE FROM connections; DELETE FROM sqlite_sequence WHERE name = 'connections';");
                Execute("VACUUM;");
            }, ct).ConfigureAwait(false);

            // live endpoints are recorded again from the next snapshot
            _tracked.Clear();
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task PruneAsync(CancellationToken ct)
    {
        int days = _settings.Current.HistoryRetentionDays;
        _lastPrune = DateTimeOffset.Now;
        if (days <= 0)
        {
            return;
        }

        int removed;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            removed = await Task.Run(() =>
            {
                using SqliteCommand command = _connection.CreateCommand();
                command.CommandText = "DELETE FROM connections WHERE last_seen < $cutoff;";
                command.Parameters.AddWithValue("$cutoff", DateTimeOffset.UtcNow.AddDays(-days).ToUnixTimeMilliseconds());
                return command.ExecuteNonQuery();
            }, ct).ConfigureAwait(false);
        }
        catch (SqliteException)
        {
            return;
        }
        finally
        {
            _gate.Release();
        }

        if (removed > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public long GetDatabaseSize()
    {
        try
        {
            long size = new FileInfo(DatabasePath).Length;
            string wal = DatabasePath + "-wal";
            return File.Exists(wal) ? size + new FileInfo(wal).Length : size;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    public void Dispose()
    {
        _settings.SettingChanged -= OnSettingChanged;
        _gate.Wait();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _insert.Dispose();
            _touch.Dispose();
            _connection.Dispose();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnSettingChanged(object? sender, string name)
    {
        if (name == nameof(AppSettings.HistoryRetentionDays))
        {
            _ = PruneAsync(CancellationToken.None);
        }
        else if (name == nameof(AppSettings.HistoryCollectionEnabled) && !_settings.Current.HistoryCollectionEnabled)
        {
            _gate.Wait();
            try
            {
                _tracked.Clear();
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private long Insert(ConnectionEntry entry, string state)
    {
        SqliteParameterCollection p = _insert.Parameters;
        p[0].Value = entry.Process.Name;
        p[1].Value = (long)entry.Pid;
        p[2].Value = entry.ProtocolText;
        p[3].Value = entry.LocalAddress;
        p[4].Value = (int)entry.LocalPort;
        p[5].Value = entry.RemoteAddress;
        p[6].Value = (int)entry.RemotePort;
        p[7].Value = state;
        p[8].Value = entry.FirstSeen.ToUnixTimeMilliseconds();
        p[9].Value = entry.LastSeen.ToUnixTimeMilliseconds();
        return (long)(_insert.ExecuteScalar() ?? 0L);
    }

    private void Touch(long id, DateTimeOffset lastSeen, string state)
    {
        _touch.Parameters[0].Value = lastSeen.ToUnixTimeMilliseconds();
        _touch.Parameters[1].Value = state;
        _touch.Parameters[2].Value = id;
        _touch.ExecuteNonQuery();
    }

    private void Execute(string sql)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string BuildWhere(SqliteCommand command, HistoryQuery query)
    {
        var clauses = new List<string>();

        DateTimeOffset? since = query.Range switch
        {
            HistoryRange.LastHour => DateTimeOffset.UtcNow.AddHours(-1),
            HistoryRange.Last24Hours => DateTimeOffset.UtcNow.AddDays(-1),
            HistoryRange.Last7Days => DateTimeOffset.UtcNow.AddDays(-7),
            _ => null,
        };
        if (since is not null)
        {
            clauses.Add("last_seen >= $since");
            command.Parameters.AddWithValue("$since", since.Value.ToUnixTimeMilliseconds());
        }

        if (!string.IsNullOrEmpty(query.Protocol))
        {
            clauses.Add("protocol LIKE $protocol");
            command.Parameters.AddWithValue("$protocol", query.Protocol + "%");
        }

        string search = query.Search?.Trim() ?? string.Empty;
        if (search.Length > 0)
        {
            string like = "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            command.Parameters.AddWithValue("$like", like);
            var text = new StringBuilder("(process_name LIKE $like ESCAPE '\\' OR local_address LIKE $like ESCAPE '\\' OR remote_address LIKE $like ESCAPE '\\' OR state LIKE $like ESCAPE '\\'");
            if (long.TryParse(search, NumberStyles.None, CultureInfo.InvariantCulture, out long number))
            {
                command.Parameters.AddWithValue("$number", number);
                text.Append(" OR pid = $number OR local_port = $number OR remote_port = $number");
            }

            text.Append(')');
            clauses.Add(text.ToString());
        }

        return clauses.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", clauses);
    }

    private static HistoryRecord ReadRecord(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        ProcessName = reader.GetString(1),
        Pid = (uint)reader.GetInt64(2),
        Protocol = reader.GetString(3),
        LocalAddress = reader.GetString(4),
        LocalPort = reader.GetInt32(5),
        RemoteAddress = reader.GetString(6),
        RemotePort = reader.GetInt32(7),
        State = reader.GetString(8),
        FirstSeen = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(9)),
        LastSeen = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(10)),
    };

    private static async Task<long> WriteCsvAsync(SqliteDataReader reader, Stream destination, CancellationToken ct)
    {
        await using var writer = new StreamWriter(destination, new UTF8Encoding(false), 64 * 1024, leaveOpen: true);
        await writer.WriteLineAsync("id,process_name,pid,protocol,local_address,local_port,remote_address,remote_port,state,first_seen,last_seen").ConfigureAwait(false);

        long count = 0;
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            HistoryRecord r = ReadRecord(reader);
            await writer.WriteLineAsync(string.Join(',',
                r.Id.ToString(CultureInfo.InvariantCulture),
                Csv(r.ProcessName),
                r.Pid.ToString(CultureInfo.InvariantCulture),
                Csv(r.Protocol),
                Csv(r.LocalAddress),
                r.LocalPort.ToString(CultureInfo.InvariantCulture),
                Csv(r.RemoteAddress),
                r.RemotePort.ToString(CultureInfo.InvariantCulture),
                Csv(r.State),
                r.FirstSeen.ToString("o", CultureInfo.InvariantCulture),
                r.LastSeen.ToString("o", CultureInfo.InvariantCulture))).ConfigureAwait(false);
            count++;
        }

        await writer.FlushAsync(ct).ConfigureAwait(false);
        return count;
    }

    private static string Csv(string value)
    {
        // quote fields that contain separators, quotes or a leading formula character
        bool needsQuote = value.AsSpan().IndexOfAny(",\"\r\n") >= 0 || (value.Length > 0 && value[0] is '=' or '+' or '-' or '@');
        if (!needsQuote)
        {
            return value;
        }

        string escaped = value.Replace("\"", "\"\"");
        return value.Length > 0 && value[0] is '=' or '+' or '-' or '@' ? $"\"'{escaped}\"" : $"\"{escaped}\"";
    }

    private static async Task<long> WriteJsonAsync(SqliteDataReader reader, Stream destination, CancellationToken ct)
    {
        await using var json = new Utf8JsonWriter(destination, new JsonWriterOptions { Indented = true });
        json.WriteStartArray();
        long count = 0;
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            HistoryRecord r = ReadRecord(reader);
            json.WriteStartObject();
            json.WriteNumber("id", r.Id);
            json.WriteString("processName", r.ProcessName);
            json.WriteNumber("pid", r.Pid);
            json.WriteString("protocol", r.Protocol);
            json.WriteString("localAddress", r.LocalAddress);
            json.WriteNumber("localPort", r.LocalPort);
            json.WriteString("remoteAddress", r.RemoteAddress);
            json.WriteNumber("remotePort", r.RemotePort);
            json.WriteString("state", r.State);
            json.WriteString("firstSeen", r.FirstSeen);
            json.WriteString("lastSeen", r.LastSeen);
            json.WriteEndObject();
            count++;

            if (json.BytesPending > 32 * 1024)
            {
                await json.FlushAsync(ct).ConfigureAwait(false);
            }
        }

        json.WriteEndArray();
        await json.FlushAsync(ct).ConfigureAwait(false);
        return count;
    }

    private sealed class Tracked(long id, string state, DateTimeOffset written)
    {
        public long Id { get; } = id;
        public string State { get; set; } = state;
        public DateTimeOffset Written { get; set; } = written;
    }
}
