using System.Runtime.InteropServices;

namespace NetTrace.Native;

/// <summary>
/// minimal process query api. only PROCESS_QUERY_LIMITED_INFORMATION is requested,
/// which is the least privileged right that still exposes the image path.
/// </summary>
internal static unsafe partial class ProcessNative
{
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const int ERROR_ACCESS_DENIED = 5;
    public const int ERROR_INVALID_PARAMETER = 87;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint hObject);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryFullProcessImageName(nint hProcess, uint dwFlags, char* lpExeName, ref int lpdwSize);
}
