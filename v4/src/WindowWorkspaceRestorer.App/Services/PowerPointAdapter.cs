using System.IO;
using System.Runtime.InteropServices;
using WindowWorkspaceRestorer.Models;

namespace WindowWorkspaceRestorer.Services;

public sealed class PowerPointAdapter : IWorkspaceAdapter
{
    public string AdapterId => "Microsoft PowerPoint";
    public WorkspaceItemType ItemType => WorkspaceItemType.PowerPointPresentation;

    public Task<IReadOnlyList<CaptureCandidate>> ScanAsync(CancellationToken cancellationToken) =>
        StaRunner.RunAsync<IReadOnlyList<CaptureCandidate>>(() =>
        {
            var results = new List<CaptureCandidate>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Discover document windows even before Office registers in the ROT.
            foreach (var snapshot in new WindowScanner().Scan().Where(window =>
                         string.Equals(window.ProcessName, "POWERPNT.EXE", StringComparison.OrdinalIgnoreCase)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var window = PowerPointWindows.TryGetDocumentWindow(snapshot.Handle);
                object? presentation = null;
                try
                {
                    if (window is null) continue;
                    presentation = ((dynamic)window).Presentation;
                    AddPresentation(results, seen, presentation, snapshot.Handle);
                }
                catch (COMException) { }
                finally
                {
                    PowerPointWindows.Release(presentation);
                    PowerPointWindows.Release(window);
                }
            }

            var application = PowerPointWindows.TryGetApplication();
            object? windows = null;
            try
            {
                if (application is not null)
                {
                    windows = ((dynamic)application).Windows;
                    foreach (var window in (System.Collections.IEnumerable)windows)
                    {
                        object? presentation = null;
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            presentation = ((dynamic)window).Presentation;
                            nint handle = 0;
                            try { handle = new nint(Convert.ToInt64(((dynamic)window).HWND)); }
                            catch (Exception ex) when (ex is COMException or MissingMemberException) { }
                            AddPresentation(results, seen, presentation, handle);
                        }
                        catch (COMException) { }
                        finally
                        {
                            PowerPointWindows.Release(presentation);
                            PowerPointWindows.Release(window);
                        }
                    }
                }
            }
            finally
            {
                PowerPointWindows.Release(windows);
                PowerPointWindows.Release(application);
            }
            return results;
        }, cancellationToken);

    private void AddPresentation(List<CaptureCandidate> results, ISet<string> seen, object presentation, nint handle)
    {
        dynamic value = presentation;
        string? path = NormalizePresentationPath((string?)value.Path, (string?)value.FullName);
        if (path is null)
        {
            if (seen.Add($"unsaved:{handle}"))
                results.Add(new CaptureCandidate(
                    new WorkspaceItem(ItemType, string.Empty, "未保存 PowerPoint 演示文稿"),
                    canPersist: false, warning: "请先保存演示文稿，再扫描并记录。", windowHandle: handle));
        }
        else if (seen.Add(PathNormalizer.Normalize(path)))
        {
            results.Add(new CaptureCandidate(
                new WorkspaceItem(ItemType, path, Path.GetFileName(path)), windowHandle: handle));
        }
    }

    internal static string? NormalizePresentationPath(string? directory, string? fullName)
    {
        // Unsaved names may end in .pptx: never invent a path relative to cwd.
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fullName) ||
            !Path.IsPathFullyQualified(fullName)) return null;
        try { return Path.GetFullPath(fullName); }
        catch (ArgumentException) { return null; }
    }

    public Task<RestoreItemResult> RestoreAsync(WorkspaceItem item, RestoreContext context,
        CancellationToken cancellationToken) => StaRunner.RunAsync(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        object? application = null;
        object? presentations = null;
        object? presentation = null;
        object? windows = null;
        object? window = null;
        object? previousSecurity = null;
        try
        {
            application = PowerPointWindows.TryGetApplication();
            if (application is null)
            {
                var type = Type.GetTypeFromProgID("PowerPoint.Application", throwOnError: false);
                if (type is null)
                    return new RestoreItemResult(item, RestoreItemStatus.ApplicationUnavailable, "未安装 Microsoft PowerPoint。");
                application = Activator.CreateInstance(type);
            }
            if (application is null)
                return new RestoreItemResult(item, RestoreItemStatus.ApplicationUnavailable, "无法启动 Microsoft PowerPoint。");
            dynamic app = application;
            app.Visible = -1;
            presentations = app.Presentations;
            ComHelpers.TrySetAutomationSecurity(app, out previousSecurity);
            presentation = ((dynamic)presentations).Open(item.Path, ReadOnly: 0, Untitled: 0, WithWindow: -1);
            windows = ((dynamic)presentation).Windows;
            if ((int)((dynamic)windows).Count < 1)
                return new RestoreItemResult(item, RestoreItemStatus.RequiresUserAction, "文件已打开，但 PowerPoint 尚未创建文档窗口。");
            window = ((dynamic)windows).Item(1);
            ((dynamic)window).Activate();
            return new RestoreItemResult(item, RestoreItemStatus.Opened, "已打开并确认 PowerPoint 文档窗口。");
        }
        catch (COMException ex)
        {
            return new RestoreItemResult(item, RestoreItemStatus.RequiresUserAction, $"PowerPoint 无法打开文件：{ex.Message}");
        }
        finally
        {
            if (application is not null) ComHelpers.TryRestoreAutomationSecurity((dynamic)application, previousSecurity);
            PowerPointWindows.Release(window);
            PowerPointWindows.Release(windows);
            PowerPointWindows.Release(presentation);
            PowerPointWindows.Release(presentations);
            PowerPointWindows.Release(application);
        }
    }, cancellationToken);
}
