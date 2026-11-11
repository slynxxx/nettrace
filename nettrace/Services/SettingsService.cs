using System.Text.Json;
using NetTrace.Helpers;
using NetTrace.Models;

namespace NetTrace.Services;

/// <summary>
/// persists <see cref="AppSettings"/> as json in the local app data folder.
/// writes are serialized and coalesced so rapid changes (sliders, combo boxes) cost one write.
/// </summary>
public sealed class SettingsService : IDisposable
{
    private readonly object _sync = new();
    private readonly string _path;
    private readonly Timer _saveTimer;
    private bool _disposed;

    public SettingsService()
        : this(AppPaths.SettingsFile)
    {
    }

    public SettingsService(string path)
    {
        _path = path;
        Current = Load(path);
        _saveTimer = new Timer(_ => SaveNow(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>raised after a setting changes, with the property name. may be raised on any thread.</summary>
    public event EventHandler<string>? SettingChanged;

    public AppSettings Current { get; }

    public void Update(string propertyName, Action<AppSettings> mutate)
    {
        lock (_sync)
        {
            mutate(Current);
            Normalize(Current);
            if (!_disposed)
            {
                _saveTimer.Change(400, Timeout.Infinite);
            }
        }

        SettingChanged?.Invoke(this, propertyName);
    }

    public void SaveNow()
    {
        string json;
        lock (_sync)
        {
            json = JsonSerializer.Serialize(Current, SettingsJsonContext.Default.AppSettings);
        }

        try
        {
            string temp = _path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _path, overwrite: true);
        }
        catch (IOException)
        {
            // a failed write keeps the previous file; the next change retries
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _saveTimer.Dispose();
        }

        SaveNow();
    }

    private static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                AppSettings? loaded = JsonSerializer.Deserialize(File.ReadAllText(path), SettingsJsonContext.Default.AppSettings);
                if (loaded is not null)
                {
                    Normalize(loaded);
                    return loaded;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            // a corrupt or unreadable settings file falls back to defaults
        }

        return new AppSettings();
    }

    private static void Normalize(AppSettings s)
    {
        s.RefreshIntervalSeconds = Math.Clamp(s.RefreshIntervalSeconds, 1, 60);
        s.TrafficSampleIntervalMs = Math.Clamp(s.TrafficSampleIntervalMs, 250, 10_000);
        s.HistoryRetentionDays = Math.Clamp(s.HistoryRetentionDays, 0, 3650);
        s.SelectedInterfaceId ??= string.Empty;
        if (!Enum.IsDefined(s.StartupPage))
        {
            s.StartupPage = AppPage.Overview;
        }

        if (!Enum.IsDefined(s.Theme))
        {
            s.Theme = AppTheme.Dark;
        }
    }
}
