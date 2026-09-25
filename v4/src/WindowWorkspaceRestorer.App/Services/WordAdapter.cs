using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using WindowWorkspaceRestorer.Interop;
using WindowWorkspaceRestorer.Models;

namespace WindowWorkspaceRestorer.Services;

public sealed class WordAdapter : IWorkspaceAdapter
{
    public string AdapterId => "Microsoft Word";

    public WorkspaceItemType ItemType => WorkspaceItemType.WordDocument;

    public Task<IReadOnlyList<CaptureCandidate>> ScanAsync(CancellationToken cancellationToken)
    {
        return StaRunner.RunAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var results = new List<CaptureCandidate>();
            var applicationObject = NativeMethods.TryGetActiveComObject("Word.Application");
            if (applicationObject is null)
            {
                return (IReadOnlyList<CaptureCandidate>)results;
            }

            dynamic application = applicationObject;
            object? documents = null;
            try
            {
                documents = application.Documents;
                foreach (var document in (System.Collections.IEnumerable)documents)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var path = TryGetDocumentPath(document);
                        if (string.IsNullOrWhiteSpace(path))
                        {
                            results.Add(new CaptureCandidate(
                                new WorkspaceItem(ItemType, string.Empty, "未保存 Word 文档"),
                                canPersist: false,
                                warning: "该文档尚未保存到磁盘，无法在下次恢复。"));
                            continue;
                        }

                        results.Add(new CaptureCandidate(
                            new WorkspaceItem(ItemType, path, Path.GetFileName(path))));
                    }
                    catch (COMException ex)
                    {
                        results.Add(new CaptureCandidate(
                            new WorkspaceItem(ItemType, string.Empty, "Word 文档"),
                            canPersist: false,
                            warning: $"无法读取文档路径：{ex.Message}"));
                    }
                    finally
                    {
                        ComHelpers.Release(document);
                    }
                }
            }
            finally
            {
                ComHelpers.Release(documents);
                ComHelpers.Release(applicationObject);
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
            var applicationObject = NativeMethods.TryGetActiveComObject("Word.Application");
            if (applicationObject is null)
            {
                return LaunchWithFileAssociation(item);
            }

            dynamic application = applicationObject;
            object? documents = null;
            object? document = null;
            object? previousSecurity = null;
            try
            {
                documents = application.Documents;
                ComHelpers.TrySetAutomationSecurity(application, out previousSecurity);
                dynamic documentsProxy = documents;
                document = documentsProxy.Open(
                    item.Path,
                    ConfirmConversions: false,
                    ReadOnly: false,
                    AddToRecentFiles: false,
                    Visible: true);
                return new RestoreItemResult(item, RestoreItemStatus.Opened, "已通过 Word 打开。");
            }
            catch (COMException ex)
            {
                return new RestoreItemResult(item, RestoreItemStatus.RequiresUserAction, $"Word 无法打开文件：{ex.Message}");
            }
            finally
            {
                ComHelpers.TryRestoreAutomationSecurity(application, previousSecurity);
                ComHelpers.Release(document);
                ComHelpers.Release(documents);
                ComHelpers.Release(applicationObject);
            }
        }, cancellationToken);
    }

    private static string? TryGetDocumentPath(dynamic document)
    {
        try
        {
            var fullName = (string?)document.FullName;
            return string.IsNullOrWhiteSpace(fullName) || !Path.HasExtension(fullName)
                ? null
                : Path.GetFullPath(fullName);
        }
        catch (COMException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static RestoreItemResult LaunchWithFileAssociation(WorkspaceItem item)
    {
        try
        {
            Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
            return new RestoreItemResult(item, RestoreItemStatus.Opened, "Word 未在运行，已通过系统文件关联打开。");
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return new RestoreItemResult(item, RestoreItemStatus.ApplicationUnavailable, $"无法启动 Word：{ex.Message}");
        }
    }
}
