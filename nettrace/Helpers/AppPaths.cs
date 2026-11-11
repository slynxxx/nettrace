using System.Reflection;

namespace NetTrace.Helpers;

public static class AppPaths
{
    public static string DataDirectory { get; } = CreateDataDirectory();

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static string HistoryDatabase => Path.Combine(DataDirectory, "history.db");

    public static string IconFile => Path.Combine(AppContext.BaseDirectory, "Assets", "nettrace.ico");

    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "1.0.0";

    private static string CreateDataDirectory()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "nettrace");
        Directory.CreateDirectory(path);
        return path;
    }
}
