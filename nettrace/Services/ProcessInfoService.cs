using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NetTrace.Models;
using NetTrace.Native;

namespace NetTrace.Services;

/// <summary>
/// resolves process ids to names and image paths with the least privileged access right,
/// caching the result for as long as the pid keeps owning network endpoints.
/// </summary>
public sealed class ProcessInfoService
{
    private readonly ConcurrentDictionary<uint, ProcessIdentity> _cache = new();

    public ProcessIdentity Get(uint pid)
    {
        if (_cache.TryGetValue(pid, out ProcessIdentity? cached))
        {
            return cached;
        }

        ProcessIdentity identity = Resolve(pid);
        _cache[pid] = identity;
        return identity;
    }

    /// <summary>drops cached entries for pids that no longer own endpoints, guarding against pid reuse.</summary>
    public void Retain(HashSet<uint> livePids)
    {
        foreach (uint pid in _cache.Keys)
        {
            if (!livePids.Contains(pid))
            {
                _cache.TryRemove(pid, out _);
            }
        }
    }

    private static ProcessIdentity Resolve(uint pid)
    {
        switch (pid)
        {
            case 0:
                return new ProcessIdentity(pid, "system idle", null, false);
            case 4:
                return new ProcessIdentity(pid, "system", null, false);
        }

        string? path = TryGetImagePath(pid, out bool accessDenied);
        if (path is not null)
        {
            return new ProcessIdentity(pid, Path.GetFileNameWithoutExtension(path), path, false);
        }

        // the image path is protected; the process name is still available from the system process list
        try
        {
            using Process process = Process.GetProcessById((int)pid);
            return new ProcessIdentity(pid, process.ProcessName, null, accessDenied);
        }
        catch (ArgumentException)
        {
            return new ProcessIdentity(pid, "exited process", null, false);
        }
        catch (InvalidOperationException)
        {
            return new ProcessIdentity(pid, "exited process", null, false);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return new ProcessIdentity(pid, $"pid {pid}", null, true);
        }
    }

    private static unsafe string? TryGetImagePath(uint pid, out bool accessDenied)
    {
        accessDenied = false;
        nint handle = ProcessNative.OpenProcess(ProcessNative.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == 0)
        {
            accessDenied = Marshal.GetLastPInvokeError() == ProcessNative.ERROR_ACCESS_DENIED;
            return null;
        }

        try
        {
            const int capacity = 1024;
            char* buffer = stackalloc char[capacity];
            int size = capacity;
            if (ProcessNative.QueryFullProcessImageName(handle, 0, buffer, ref size) && size > 0)
            {
                return new string(buffer, 0, size);
            }

            accessDenied = Marshal.GetLastPInvokeError() == ProcessNative.ERROR_ACCESS_DENIED;
            return null;
        }
        finally
        {
            ProcessNative.CloseHandle(handle);
        }
    }
}
