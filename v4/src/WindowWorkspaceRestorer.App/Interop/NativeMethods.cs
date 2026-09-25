using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WindowWorkspaceRestorer.Interop;

internal static class NativeMethods
{
    [DllImport("user32.dll")]
    internal static extern nint GetAncestor(nint handle, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint handle);
    private const uint ProcessQueryLimitedInformation = 0x1000;
    internal const int ShowCommandShow = 5;
    internal const uint WindowMessageCommand = 0x0111;
    internal const int ExplorerCommandNewTab = 0xA21B;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumChildWindows(
        nint parentWindow,
        EnumWindowsProc callback,
        nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint hWnd, int command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetClassName(nint hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    internal static extern nint SendMessage(
        nint hWnd,
        uint message,
        nint wParam,
        nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(nint hWnd, uint message, nint wParam, nint lParam);

    [DllImport("oleacc.dll")]
    internal static extern int AccessibleObjectFromWindow(
        nint hWnd, uint objectId, ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out object? nativeObject);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetWindowText(nint hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryFullProcessImageName(
        nint process,
        uint flags,
        StringBuilder exeName,
        ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(nint handle);

    [DllImport("oleaut32.dll", PreserveSig = true)]
    private static extern int GetActiveObject(
        ref Guid classId,
        nint reserved,
        [MarshalAs(UnmanagedType.Interface)] out object? activeObject);

    internal static object? TryGetActiveComObject(string progId)
    {
        var type = Type.GetTypeFromProgID(progId, throwOnError: false);
        if (type is null)
        {
            return null;
        }

        var classId = type.GUID;
        var result = GetActiveObject(ref classId, nint.Zero, out var activeObject);
        return result == 0 ? activeObject : null;
    }

    internal static string? TryGetProcessPath(uint processId)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, inheritHandle: false, processId);
        if (process == nint.Zero)
        {
            return null;
        }

        try
        {
            var buffer = new StringBuilder(1024);
            var length = (uint)buffer.Capacity;
            return QueryFullProcessImageName(process, 0, buffer, ref length)
                ? buffer.ToString()
                : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    [DllImport("ntdll.dll")]
    internal static extern int NtQueryInformationProcess(
        nint processHandle,
        int processInformationClass,
        nint processInformation,
        uint processInformationLength,
        out uint returnLength);


    [DllImport("shell32.dll", SetLastError = true)]
    internal static extern nint CommandLineToArgvW(
        [MarshalAs(UnmanagedType.LPWStr)] string commandLine,
        out int argumentCount);

    [DllImport("kernel32.dll")]
    internal static extern nint GetProcessHeap();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HeapFree(nint heap, uint flags, nint memory);
}
