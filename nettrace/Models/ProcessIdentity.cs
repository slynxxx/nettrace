namespace NetTrace.Models;

/// <summary>
/// what could be learned about a process without elevated rights.
/// <see cref="Path"/> is null when windows denies access (protected or other-user processes).
/// </summary>
public sealed class ProcessIdentity
{
    public ProcessIdentity(uint pid, string name, string? path, bool accessDenied)
    {
        Pid = pid;
        Name = name;
        Path = path;
        AccessDenied = accessDenied;
    }

    public uint Pid { get; }
    public string Name { get; }
    public string? Path { get; }
    public bool AccessDenied { get; }

    public string PathText => Path ?? (AccessDenied ? "access denied" : "unavailable");
}
