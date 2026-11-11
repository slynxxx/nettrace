using System.Text.Json.Serialization;

namespace NetTrace.Models;

public enum AppPage
{
    Overview,
    Connections,
    Processes,
    Dns,
    History,
    Settings,
}

public enum AppTheme
{
    Dark,
    Light,
}

public sealed class AppSettings
{
    /// <summary>connection table refresh interval.</summary>
    public int RefreshIntervalSeconds { get; set; } = 2;

    /// <summary>traffic counter sampling interval.</summary>
    public int TrafficSampleIntervalMs { get; set; } = 1000;

    public AppPage StartupPage { get; set; } = AppPage.Overview;

    /// <summary>network interface id, empty for all interfaces.</summary>
    public string SelectedInterfaceId { get; set; } = string.Empty;

    /// <summary>days to keep history, 0 keeps everything.</summary>
    public int HistoryRetentionDays { get; set; } = 7;

    public bool HistoryCollectionEnabled { get; set; } = true;

    public AppTheme Theme { get; set; } = AppTheme.Dark;

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
