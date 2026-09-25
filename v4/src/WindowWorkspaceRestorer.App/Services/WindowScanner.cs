using System.Diagnostics;
using System.IO;
using System.Text;
using WindowWorkspaceRestorer.Interop;
using WindowWorkspaceRestorer.Models;

namespace WindowWorkspaceRestorer.Services;

public sealed class WindowScanner
{
    public IReadOnlyList<WindowSnapshot> Scan() => Scan(false);

    public IReadOnlyList<WindowSnapshot> Scan(bool includeHidden)
    {
        var windows = new List<WindowSnapshot>();
        NativeMethods.EnumWindows((handle, _) =>
        {
            if (!includeHidden && !NativeMethods.IsWindowVisible(handle))
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(handle, out var processId);
            var processPath = NativeMethods.TryGetProcessPath(processId);
            if (string.IsNullOrWhiteSpace(processPath))
            {
                return true;
            }

            var titleBuffer = new StringBuilder(512);
            NativeMethods.GetWindowText(handle, titleBuffer, titleBuffer.Capacity);
            windows.Add(new WindowSnapshot(
                handle,
                processId,
                Path.GetFileName(processPath),
                titleBuffer.ToString()));
            return true;
        }, nint.Zero);

        return windows;
    }

    public bool HasVisibleProcess(string executableName)
    {
        return Scan().Any(window => string.Equals(
            window.ProcessName,
            executableName,
            StringComparison.OrdinalIgnoreCase));
    }
}
