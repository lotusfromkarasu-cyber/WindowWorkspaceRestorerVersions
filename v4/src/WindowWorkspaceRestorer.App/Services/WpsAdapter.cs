using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using WindowWorkspaceRestorer.Interop;
using WindowWorkspaceRestorer.Models;

namespace WindowWorkspaceRestorer.Services;

public sealed class WpsAdapter : IWorkspaceAdapter
{
    public WpsAdapter(WorkspaceItemType itemType)
    {
        if (!IsWps(itemType)) throw new ArgumentOutOfRangeException(nameof(itemType));
        ItemType = itemType;
    }
    public WorkspaceItemType ItemType { get; }
    public string AdapterId => ItemType switch
    {
        WorkspaceItemType.WpsWriterDocument => "WPS 文字",
        WorkspaceItemType.WpsSpreadsheet => "WPS 表格",
        _ => "WPS 演示"
    };
    private string ProgId => ItemType switch
    {
        WorkspaceItemType.WpsWriterDocument => "kwps.Application",
        WorkspaceItemType.WpsSpreadsheet => "ket.Application",
        _ => "kwpp.Application"
    };
    internal static bool IsWps(WorkspaceItemType type) => type is WorkspaceItemType.WpsWriterDocument
        or WorkspaceItemType.WpsSpreadsheet or WorkspaceItemType.WpsPresentation;

    public Task<IReadOnlyList<CaptureCandidate>> ScanAsync(CancellationToken cancellationToken) =>
        StaRunner.RunAsync<IReadOnlyList<CaptureCandidate>>(() => Scan(cancellationToken), cancellationToken);

    private IReadOnlyList<CaptureCandidate> Scan(CancellationToken cancellationToken)
    {
        var results = new List<CaptureCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var applications = GetApplications();
        try
        {
            foreach (var entry in applications)
            {
                object? collection = null;
                try
                {
                    collection = GetDocuments(entry.Application);
                    for (var index = 1; index <= (int)((dynamic)collection).Count; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        object? document = null;
                        try
                        {
                            document = ((dynamic)collection).Item(index);
                            var path = NormalizePath((string?)((dynamic)document).Path, (string?)((dynamic)document).FullName);
                            var handle = GetDocumentHandle(document, entry.Handle);
                            if (handle == 0 || !NativeMethods.IsWindowVisible(handle)) continue;
                            var name = (string?)((dynamic)document).Name ?? AdapterId;
                            var group = $"wps:{handle.ToInt64()}";
                            if (!seen.Add($"{group}:{path ?? name}")) continue;
                            results.Add(new CaptureCandidate(new WorkspaceItem(ItemType, path ?? string.Empty, name, group),
                                canPersist: path is not null, warning: path is null ? "请先保存文件，再记录此标签页。" : null,
                                windowHandle: handle));
                        }
                        catch (Exception ex) when (IsComFailure(ex)) { }
                        finally { Release(document); }
                    }
                }
                catch (Exception ex) when (IsComFailure(ex)) { }
                finally { Release(collection); }
            }
        }
        finally { foreach (var entry in applications) Release(entry.Application); }
        return results;
    }

    internal static string? NormalizePath(string? directory, string? fullName)
    {
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fullName) || !Path.IsPathFullyQualified(fullName)) return null;
        try { return Path.GetFullPath(fullName); }
        catch (ArgumentException) { return null; }
    }

    internal object GetDocuments(dynamic application) => ItemType switch
    {
        WorkspaceItemType.WpsWriterDocument => application.Documents,
        WorkspaceItemType.WpsSpreadsheet => application.Workbooks,
        _ => application.Presentations
    };

    private nint GetDocumentHandle(dynamic document, nint fallback)
    {
        object? windows = null;
        object? window = null;
        try
        {
            windows = document.Windows;
            if ((int)((dynamic)windows).Count == 0) return 0;
            window = ((dynamic)windows).Item(1);
            var child = new nint(Convert.ToInt64(((dynamic)window).Hwnd));
            var root = NativeMethods.GetAncestor(child, 2);
            return root != 0 ? root : fallback;
        }
        catch (Exception ex) when (IsComFailure(ex)) { return fallback; }
        finally { Release(window); Release(windows); }
    }

    internal List<(object Application, nint Handle)> GetApplications()
    {
        var results = new List<(object Application, nint Handle)>();
        var identities = new HashSet<nint>();
        foreach (var snapshot in new WindowScanner().Scan(includeHidden: true).Where(window =>
                     window.ProcessName.ToLowerInvariant() is "wps.exe" or "et.exe" or "wpp.exe"))
        {
            NativeMethods.EnumChildWindows(snapshot.Handle, (child, _) =>
            {
                var name = new StringBuilder(128);
                NativeMethods.GetClassName(child, name, name.Capacity);
                var matches = ItemType switch
                {
                    WorkspaceItemType.WpsWriterDocument => name.ToString() == "_WwG",
                    WorkspaceItemType.WpsSpreadsheet => name.ToString() == "EXCEL7",
                    _ => name.ToString() is "paneClassDC" or "mdiClass"
                };
                if (!matches) return true;
                object? native = null;
                object? application = null;
                try
                {
                    var iid = new Guid("00020400-0000-0000-C000-000000000046");
                    if (NativeMethods.AccessibleObjectFromWindow(child, 0xFFFFFFF0, ref iid, out native) == 0 && native is not null)
                        application = ((dynamic)native).Application;
                }
                catch (Exception ex) when (IsComFailure(ex)) { }
                finally { Release(native); }
                if (application is not null)
                {
                    var identity = Marshal.GetIUnknownForObject(application);
                    try
                    {
                        if (identities.Add(identity)) results.Add((application, snapshot.Handle));
                        else Release(application);
                    }
                    finally { Marshal.Release(identity); }
                }
                // An integrated WPS window may host several independent COM
                // Applications, even for tabs of the same document type.
                return true;
            }, 0);
        }
        // ROT is a fallback only: WPS may register an empty automation instance
        // while the user's integrated window belongs to another application.
        if (results.Count == 0)
        {
            var active = NativeMethods.TryGetActiveComObject(ProgId);
            if (active is not null) results.Add((active, 0));
        }
        return results;
    }

    public Task<RestoreItemResult> RestoreAsync(WorkspaceItem item, RestoreContext context, CancellationToken cancellationToken) =>
        StaRunner.RunAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = Scan(cancellationToken).FirstOrDefault(candidate => candidate.CanPersist && PathNormalizer.Same(candidate.Item.Path, item.Path));
            if (existing is not null) return new RestoreItemResult(item, RestoreItemStatus.AlreadyOpen, "WPS 标签页已打开。");
            var targetHandle = item.GroupKey is not null && context.WpsGroupHandles.TryGetValue(item.GroupKey, out var handle) ? handle : 0;
            if (targetHandle != 0 && NativeMethods.IsWindow(targetHandle))
            {
                NativeMethods.ShowWindow(targetHandle, 9);
                NativeMethods.SetForegroundWindow(targetHandle);
            }
            var openedInHost = TryOpenInHost(item.Path, targetHandle);
            if (!openedInHost)
            {
                var executable = FindExecutable();
                if (executable is null) return new RestoreItemResult(item, RestoreItemStatus.ApplicationUnavailable, $"未找到{AdapterId}。");
                try
                {
                    // Start the first tab via WPS's normal integrated entry point.
                    var start = new ProcessStartInfo(executable) { UseShellExecute = true };
                    start.ArgumentList.Add(item.Path);
                    using var process = Process.Start(start);
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                { return new RestoreItemResult(item, RestoreItemStatus.ApplicationUnavailable, $"WPS 启动失败：{ex.Message}"); }
            }
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < context.OpenTimeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var opened = Scan(cancellationToken).FirstOrDefault(candidate => candidate.CanPersist && PathNormalizer.Same(candidate.Item.Path, item.Path));
                if (opened is not null)
                {
                    if (targetHandle != 0 && NativeMethods.IsWindow(targetHandle) && opened.WindowHandle != targetHandle)
                        return new RestoreItemResult(item, RestoreItemStatus.RequiresUserAction, "文件已打开，但 WPS 未合并到原窗口；请在 WPS 中启用整合模式后恢复。");
                    if (item.GroupKey is not null) context.WpsGroupHandles[item.GroupKey] = opened.WindowHandle;
                    return new RestoreItemResult(item, RestoreItemStatus.Opened, "已恢复并确认 WPS 文档标签页。");
                }
                if (cancellationToken.WaitHandle.WaitOne(200)) cancellationToken.ThrowIfCancellationRequested();
            }
            return new RestoreItemResult(item, RestoreItemStatus.RequiresUserAction, "已交给 WPS 打开，尚未确认标签页；请检查 WPS 中是否有提示窗口。");
        }, cancellationToken);

    private bool TryOpenInHost(string path, nint targetHandle)
    {
        var applications = GetApplications();
        try
        {
            var host = applications.FirstOrDefault(entry => entry.Handle != 0 &&
                NativeMethods.IsWindowVisible(entry.Handle) && (targetHandle == 0 || entry.Handle == targetHandle));
            if (host.Application is null) return false;
            object? documents = null;
            object? opened = null;
            object? security = null;
            try
            {
                documents = GetDocuments(host.Application);
                ComHelpers.TrySetAutomationSecurity((dynamic)host.Application, out security);
                opened = ItemType == WorkspaceItemType.WpsPresentation
                    ? ((dynamic)documents).Open(path, 0, 0, -1)
                    : ((dynamic)documents).Open(path);
                return opened is not null;
            }
            finally
            {
                ComHelpers.TryRestoreAutomationSecurity((dynamic)host.Application, security);
                Release(opened); Release(documents);
            }
        }
        finally { foreach (var entry in applications) Release(entry.Application); }
    }

    private string? FindExecutable()
    {
        using var prog = Registry.ClassesRoot.OpenSubKey(ProgId + @"\CLSID");
        var clsid = prog?.GetValue(null) as string;
        using var server = clsid is null ? null : Registry.ClassesRoot.OpenSubKey(@"CLSID\" + clsid + @"\LocalServer32");
        var command = server?.GetValue(null) as string;
        if (string.IsNullOrWhiteSpace(command)) return null;
        var args = ProcessCommandLine.ParseArguments(command);
        return args.Count > 0 && File.Exists(args[0]) ? args[0] : null;
    }
    private static bool IsComFailure(Exception ex) => ex is COMException or MissingMemberException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException;
    internal static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }
}
