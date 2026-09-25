using System.Windows.Automation;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using WindowWorkspaceRestorer.Interop;
using WindowWorkspaceRestorer.Models;

namespace WindowWorkspaceRestorer.Services;

public sealed class ExplorerAdapter : IWorkspaceAdapter
{
    public string AdapterId => "文件资源管理器";

    public WorkspaceItemType ItemType => WorkspaceItemType.ExplorerFolder;

    public Task<IReadOnlyList<CaptureCandidate>> ScanAsync(CancellationToken cancellationToken)
    {
        return StaRunner.RunAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var results = new List<CaptureCandidate>();
            var shellType = Type.GetTypeFromProgID("Shell.Application", throwOnError: false);
            if (shellType is null)
            {
                return (IReadOnlyList<CaptureCandidate>)results;
            }

            object? shellObject = null;
            object? windows = null;
            try
            {
                shellObject = Activator.CreateInstance(shellType);
                if (shellObject is null)
                {
                    return (IReadOnlyList<CaptureCandidate>)results;
                }

                dynamic shell = shellObject;
                windows = shell.Windows();
                foreach (var window in (System.Collections.IEnumerable)windows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    dynamic shellWindow = window;
                    try
                    {
                        var fullName = (string?)shellWindow.FullName;
                        if (!string.Equals(
                                Path.GetFileName(fullName),
                                "explorer.exe",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var windowHandle = TryGetWindowHandle(shellWindow);
                        var groupKey = windowHandle == nint.Zero
                            ? null
                            : $"explorer:{windowHandle.ToInt64().ToString(CultureInfo.InvariantCulture)}";
                        var path = ShellInterop.TryGetFileSystemPath((string?)shellWindow.LocationURL);
                        if (string.IsNullOrWhiteSpace(path))
                        {
                            results.Add(new CaptureCandidate(
                                new WorkspaceItem(ItemType, string.Empty, "资源管理器虚拟页面"),
                                canPersist: false,
                                warning: "该页面没有普通文件夹路径，无法在下次恢复。",
                                windowHandle: windowHandle));
                            continue;
                        }

                        results.Add(new CaptureCandidate(
                            new WorkspaceItem(
                                ItemType,
                                path,
                                GetDisplayName(path),
                                groupKey),
                            windowHandle: windowHandle));
                    }
                    catch (COMException ex)
                    {
                        results.Add(new CaptureCandidate(
                            new WorkspaceItem(ItemType, string.Empty, "资源管理器窗口"),
                            canPersist: false,
                            warning: $"无法读取文件夹路径：{ex.Message}"));
                    }
                    finally
                    {
                        ComHelpers.Release(window);
                    }
                }
            }
            finally
            {
                ComHelpers.Release(windows);
                ComHelpers.Release(shellObject);
            }

            return (IReadOnlyList<CaptureCandidate>)results;
        }, cancellationToken);
    }

    public Task<RestoreItemResult> RestoreAsync(
        WorkspaceItem item,
        RestoreContext context,
        CancellationToken cancellationToken)
    {
        return StaRunner.RunAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrWhiteSpace(item.GroupKey) &&
                context.ExplorerGroupsRequiringUserAction.Contains(item.GroupKey))
            {
                return new RestoreItemResult(
                    item,
                    RestoreItemStatus.RequiresUserAction,
                    "同组资源管理器标签页无法自动操作，请在现有窗口中手动打开此文件夹。");
            }

            if (!string.IsNullOrWhiteSpace(item.GroupKey) &&
                context.ExplorerGroupHandles.TryGetValue(item.GroupKey, out var existingHandle))
            {
                if (!NativeMethods.IsWindow(existingHandle))
                {
                    context.ExplorerGroupHandles.Remove(item.GroupKey);
                }
                else if (ExplorerTabAutomation.OpenInNewTab(existingHandle, item.Path, context.OpenTimeout, cancellationToken))
                {
                    return new RestoreItemResult(
                        item,
                        RestoreItemStatus.Opened,
                        "已在同一个资源管理器窗口中打开新标签页。");
                }
                else
                {
                    context.ExplorerGroupsRequiringUserAction.Add(item.GroupKey);
                    return new RestoreItemResult(
                        item,
                        RestoreItemStatus.RequiresUserAction,
                        "无法在现有资源管理器窗口中创建标签页，未另开独立窗口。");
                }
            }

            var firstWindowResult = OpenFirstWindow(item.Path);
            if (firstWindowResult.Status != RestoreItemStatus.Opened)
            {
                return new RestoreItemResult(item, firstWindowResult.Status, firstWindowResult.Message);
            }

            if (string.IsNullOrWhiteSpace(item.GroupKey))
            {
                return new RestoreItemResult(item, RestoreItemStatus.Opened, firstWindowResult.Message);
            }

            var handle = FindWindowForPath(item.Path, context.OpenTimeout, cancellationToken, context.ExplorerGroupHandles.Values.ToHashSet());
            if (handle == nint.Zero)
            {
                context.ExplorerGroupsRequiringUserAction.Add(item.GroupKey);
                return new RestoreItemResult(
                    item,
                    RestoreItemStatus.RequiresUserAction,
                    "已打开文件夹，但无法确认其窗口句柄；后续标签页无法安全合并。");
            }

            context.ExplorerGroupHandles[item.GroupKey] = handle;
            return new RestoreItemResult(
                item,
                RestoreItemStatus.Opened,
                "已打开资源管理器窗口，后续同组文件夹将加入该窗口的标签页。");
        }, cancellationToken);
    }

    private static RestoreItemResult OpenFirstWindow(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "/n," + QuoteArgument(path))
            {
                UseShellExecute = true
            });
            return new RestoreItemResult(
                new WorkspaceItem(WorkspaceItemType.ExplorerFolder, path, GetDisplayName(path)),
                RestoreItemStatus.Opened,
                "已通过 explorer.exe 打开。");
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return new RestoreItemResult(
                new WorkspaceItem(WorkspaceItemType.ExplorerFolder, path, GetDisplayName(path)),
                RestoreItemStatus.ApplicationUnavailable,
                $"无法启动文件资源管理器：{ex.Message}");
        }
    }

    private static nint FindWindowForPath(
        string path,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        ISet<nint> excludedHandles)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow <= deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var handle = TryFindWindowForPath(path, excludedHandles);
            if (handle != nint.Zero)
            {
                return handle;
            }

            Thread.Sleep(100);
        }

        return nint.Zero;
    }

    private static nint TryFindWindowForPath(string path, ISet<nint> excludedHandles)
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application", throwOnError: false);
        if (shellType is null)
        {
            return nint.Zero;
        }

        object? shellObject = null;
        object? windows = null;
        try
        {
            shellObject = Activator.CreateInstance(shellType);
            if (shellObject is null)
            {
                return nint.Zero;
            }

            dynamic shell = shellObject;
            windows = shell.Windows();
            foreach (var window in (System.Collections.IEnumerable)windows)
            {
                dynamic shellWindow = window;
                try
                {
                    if (!string.Equals(
                            Path.GetFileName((string?)shellWindow.FullName),
                            "explorer.exe",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var currentPath = ShellInterop.TryGetFileSystemPath((string?)shellWindow.LocationURL);
                    if (currentPath is not null && PathNormalizer.Same(currentPath, path) &&
                        !excludedHandles.Contains(TryGetWindowHandle(shellWindow)))
                    {
                        return TryGetWindowHandle(shellWindow);
                    }
                }
                finally
                {
                    ComHelpers.Release(window);
                }
            }
        }
        catch (COMException)
        {
            return nint.Zero;
        }
        finally
        {
            ComHelpers.Release(windows);
            ComHelpers.Release(shellObject);
        }

        return nint.Zero;
    }

    private static nint TryGetWindowHandle(dynamic shellWindow)
    {
        try
        {
            return new nint(Convert.ToInt64(shellWindow.HWND, CultureInfo.InvariantCulture));
        }
        catch
        {
            return nint.Zero;
        }
    }

    private static string GetDisplayName(string path)
    {
        return Path.GetFileName(path) is { Length: > 0 } name ? name : path;
    }

    private static string QuoteArgument(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }
}

internal static class ExplorerTabAutomation
{
    internal static bool OpenInNewTab(nint windowHandle, string path, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!NativeMethods.IsWindow(windowHandle))
        {
            return false;
        }

        NativeMethods.ShowWindow(windowHandle, NativeMethods.ShowCommandShow);
        // Hold the original COM identities alive throughout the operation. Paths
        // are mutable (and Home has no filesystem path), so cannot identify tabs.
        var originalTabs = GetShellWindowEntries(windowHandle);
        try
        {
            return ExplorerTabNavigation.Open(
                originalTabs,
                () => TryCreateTabWithUiAutomation(windowHandle) || TryCreateTabWithWindowCommand(windowHandle),
                () => GetShellWindowEntries(windowHandle),
                path, timeout, cancellationToken);
        }
        finally
        {
            foreach (var tab in originalTabs) tab.Dispose();
        }
    }

    private static bool TryCreateTabWithUiAutomation(nint windowHandle)
    {
        try
        {
            var explorerWindow = AutomationElement.FromHandle(windowHandle);
            var addButtonCondition = new PropertyCondition(
                AutomationElement.AutomationIdProperty,
                "AddButton");
            var addButton = explorerWindow.FindFirst(
                TreeScope.Descendants,
                addButtonCondition);
            if (addButton is null ||
                addButton.GetCurrentPattern(InvokePattern.Pattern) is not InvokePattern invokePattern)
            {
                return false;
            }

            invokePattern.Invoke();
            return true;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
        catch (ElementNotEnabledException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryCreateTabWithWindowCommand(nint windowHandle)
    {
        nint shellTabWindow = nint.Zero;
        NativeMethods.EnumChildWindows(
            windowHandle,
            (childWindow, _) =>
            {
                var className = new System.Text.StringBuilder(64);
                NativeMethods.GetClassName(childWindow, className, className.Capacity);
                if (!string.Equals(
                        className.ToString(),
                        "ShellTabWindowClass",
                        StringComparison.Ordinal))
                {
                    return true;
                }

                if (NativeMethods.IsWindowVisible(childWindow))
                {
                    shellTabWindow = childWindow;
                    return false;
                }
                return true;
            },
            nint.Zero);

        if (shellTabWindow == nint.Zero)
        {
            return false;
        }

        return NativeMethods.PostMessage(
            shellTabWindow,
            NativeMethods.WindowMessageCommand,
            new nint(NativeMethods.ExplorerCommandNewTab),
            nint.Zero);
    }

    private static IReadOnlyList<IExplorerTab> GetShellWindowEntries(nint windowHandle)
    {
        var results = new List<IExplorerTab>();
        var shellType = Type.GetTypeFromProgID("Shell.Application", throwOnError: false);
        if (shellType is null)
        {
            return results;
        }

        object? shellObject = null;
        object? windows = null;
        try
        {
            shellObject = Activator.CreateInstance(shellType);
            if (shellObject is null)
            {
                return results;
            }

            dynamic shell = shellObject;
            windows = shell.Windows();
            foreach (var window in (System.Collections.IEnumerable)windows)
            {
                try
                {
                    dynamic shellWindow = window;
                    if (GetWindowHandle(shellWindow) != windowHandle ||
                        !string.Equals(
                            Path.GetFileName((string?)shellWindow.FullName),
                            "explorer.exe",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        ComHelpers.Release(window);
                        continue;
                    }

                    results.Add(new ExplorerShellTab(
                        window));
                }
                catch (COMException)
                {
                    ComHelpers.Release(window);
                }
            }
        }
        finally
        {
            ComHelpers.Release(windows);
            ComHelpers.Release(shellObject);
        }

        return results;
    }



    private static nint GetWindowHandle(dynamic shellWindow)
    {
        try
        {
            return new nint(Convert.ToInt64(shellWindow.HWND, CultureInfo.InvariantCulture));
        }
        catch
        {
            return nint.Zero;
        }
    }
}
