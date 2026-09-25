using System.Diagnostics;
using System.Runtime.InteropServices;
using WindowWorkspaceRestorer.Interop;

namespace WindowWorkspaceRestorer.Services;

internal interface IExplorerTab : IDisposable
{
    nint Identity { get; }
    string? Path { get; }
    bool Busy { get; }
    void Navigate(string path);
}

internal sealed class ExplorerShellTab : IExplorerTab
{
    private readonly object _window;
    private bool _disposed;
    public ExplorerShellTab(object window)
    {
        _window = window;
        Identity = Marshal.GetIUnknownForObject(window);
    }
    public nint Identity { get; }
    public string? Path => ShellInterop.TryGetFileSystemPath((string?)((dynamic)_window).LocationURL);
    public bool Busy => (bool)((dynamic)_window).Busy;
    public void Navigate(string path) => ((dynamic)_window).Navigate2(path);
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Marshal.Release(Identity);
        // Enumeration may return the same RCW as the baseline snapshot. Never
        // FinalReleaseComObject here: that invalidates the baseline reference.
        Marshal.ReleaseComObject(_window);
    }
}

internal static class ExplorerTabNavigation
{
    internal static bool Open(
        IReadOnlyList<IExplorerTab> originalTabs,
        Func<bool> createTab,
        Func<IReadOnlyList<IExplorerTab>> enumerateTabs,
        string path,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var existing = originalTabs.Select(tab => tab.Identity).ToHashSet();
        if (existing.Count == 0 || !createTab()) return false;
        var timer = Stopwatch.StartNew();
        nint target = 0;
        IExplorerTab? retainedTarget = null;
        var settled = 0;
        try
        {
            while (timer.Elapsed < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var tabs = enumerateTabs();
                try
                {
                    if (target == 0)
                    {
                        var added = tabs.Where(tab => !existing.Contains(tab.Identity)).ToArray();
                        // If another actor also added a tab, guessing could overwrite it.
                        if (added.Length > 1) return false;
                        if (added.Length == 1 && !added[0].Busy)
                        {
                            target = added[0].Identity;
                            retainedTarget = added[0];
                            added[0].Navigate(path);
                        }
                    }
                    else
                    {
                        var tab = tabs.FirstOrDefault(tab => tab.Identity == target);
                        if (tab is null) return false;
                        settled = !tab.Busy && tab.Path is { } current && PathNormalizer.Same(current, path)
                            ? settled + 1 : 0;
                        // Navigation is asynchronous. Confirm completion before the next tab.
                        if (settled >= 3) return true;
                    }
                }
                catch (COMException)
                {
                    settled = 0;
                }
                finally
                {
                    foreach (var tab in tabs)
                        if (!ReferenceEquals(tab, retainedTarget)) tab.Dispose();
                }
                if (cancellationToken.WaitHandle.WaitOne(100))
                    cancellationToken.ThrowIfCancellationRequested();
            }
            return false;
        }
        finally { retainedTarget?.Dispose(); }
    }
}
