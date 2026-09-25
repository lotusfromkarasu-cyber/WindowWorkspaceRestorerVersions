using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using WindowWorkspaceRestorer.Interop;
using WindowWorkspaceRestorer.Models;

namespace WindowWorkspaceRestorer.Services;

public sealed class ComsolAdapter : IWorkspaceAdapter
{
    private readonly Func<IReadOnlyList<WindowSnapshot>> _windowProvider;
    private readonly Func<uint, string?> _commandLineProvider;

    public ComsolAdapter(
        Func<IReadOnlyList<WindowSnapshot>>? windowProvider = null,
        Func<uint, string?>? commandLineProvider = null)
    {
        _windowProvider = windowProvider ?? new WindowScanner().Scan;
        _commandLineProvider = commandLineProvider ?? ProcessCommandLine.TryGet;
    }

    public string AdapterId => "COMSOL Multiphysics";

    public WorkspaceItemType ItemType => WorkspaceItemType.ComsolModel;

    public Task<IReadOnlyList<CaptureCandidate>> ScanAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var processIds = _windowProvider()
                .Where(window => IsComsolDesktopProcessName(window.ProcessName))
                .Select(window => window.ProcessId)
                .Distinct()
                .ToArray();

            var results = new List<CaptureCandidate>();
            foreach (var processId in processIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var commandLine = _commandLineProvider(processId);
                var paths = TryGetOpenModelPaths(commandLine);
                if (paths.Count == 0)
                {
                    results.Add(new CaptureCandidate(
                        new WorkspaceItem(ItemType, string.Empty, "未保存 COMSOL 模型"),
                        canPersist: false,
                        warning: commandLine is null
                            ? "无法读取 COMSOL 进程命令行（可能权限不足）。"
                            : "未保存模型没有可用磁盘路径，无法在下次恢复。"));
                    continue;
                }

                foreach (var path in paths)
                {
                    results.Add(new CaptureCandidate(
                        new WorkspaceItem(ItemType, path, Path.GetFileName(path))));
                }
            }

            return (IReadOnlyList<CaptureCandidate>)results;
        }, cancellationToken);
    }

    public Task<RestoreItemResult> RestoreAsync(
        WorkspaceItem item,
        RestoreContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(LaunchWithFileAssociation(item));
    }

    private static bool IsComsolDesktopProcessName(string processName)
    {
        return string.Equals(processName, "ComsolUI.exe", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(processName, "comsol.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> TryGetOpenModelPaths(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return Array.Empty<string>();
        }

        try
        {
            var arguments = ProcessCommandLine.ParseArguments(commandLine);
            var paths = new List<string>();
            for (var i = 0; i < arguments.Count - 1; i++)
            {
                if (!string.Equals(arguments[i], "-open", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = arguments[i + 1].Trim();
                if (value.Length == 0)
                {
                    continue;
                }

                try
                {
                    paths.Add(Path.GetFullPath(value));
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    // Malformed paths are skipped; a warning candidate is added when no valid path remains.
                }
            }

            return paths;
        }
        catch (Win32Exception)
        {
            return Array.Empty<string>();
        }
    }

    private static RestoreItemResult LaunchWithFileAssociation(WorkspaceItem item)
    {
        try
        {
            Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
            return new RestoreItemResult(
                item,
                RestoreItemStatus.Opened,
                "已通过系统 .mph 文件关联打开 COMSOL 模型。");
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return new RestoreItemResult(
                item,
                RestoreItemStatus.ApplicationUnavailable,
                $"无法启动 COMSOL：{ex.Message}");
        }
    }
}