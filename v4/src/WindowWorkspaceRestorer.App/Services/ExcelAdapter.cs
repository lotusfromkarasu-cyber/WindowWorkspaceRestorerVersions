using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using WindowWorkspaceRestorer.Interop;
using WindowWorkspaceRestorer.Models;

namespace WindowWorkspaceRestorer.Services;

public sealed class ExcelAdapter : IWorkspaceAdapter
{
    public string AdapterId => "Microsoft Excel";

    public WorkspaceItemType ItemType => WorkspaceItemType.ExcelWorkbook;

    public Task<IReadOnlyList<CaptureCandidate>> ScanAsync(CancellationToken cancellationToken)
    {
        return StaRunner.RunAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var results = new List<CaptureCandidate>();
            var applicationObject = NativeMethods.TryGetActiveComObject("Excel.Application");
            if (applicationObject is null)
            {
                return (IReadOnlyList<CaptureCandidate>)results;
            }

            dynamic application = applicationObject;
            object? workbooks = null;
            try
            {
                workbooks = application.Workbooks;
                foreach (var workbook in (System.Collections.IEnumerable)workbooks)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var path = TryGetWorkbookPath(workbook);
                        if (string.IsNullOrWhiteSpace(path))
                        {
                            results.Add(new CaptureCandidate(
                                new WorkspaceItem(ItemType, string.Empty, "未保存 Excel 工作簿"),
                                canPersist: false,
                                warning: "该工作簿尚未保存到磁盘，无法在下次恢复。"));
                            continue;
                        }

                        results.Add(new CaptureCandidate(
                            new WorkspaceItem(ItemType, path, Path.GetFileName(path))));
                    }
                    catch (COMException ex)
                    {
                        results.Add(new CaptureCandidate(
                            new WorkspaceItem(ItemType, string.Empty, "Excel 工作簿"),
                            canPersist: false,
                            warning: $"无法读取工作簿路径：{ex.Message}"));
                    }
                    finally
                    {
                        ComHelpers.Release(workbook);
                    }
                }
            }
            finally
            {
                ComHelpers.Release(workbooks);
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
            var applicationObject = NativeMethods.TryGetActiveComObject("Excel.Application");
            if (applicationObject is null)
            {
                return LaunchWithFileAssociation(item);
            }

            dynamic application = applicationObject;
            object? workbooks = null;
            object? workbook = null;
            object? previousSecurity = null;
            try
            {
                workbooks = application.Workbooks;
                ComHelpers.TrySetAutomationSecurity(application, out previousSecurity);
                dynamic workbooksProxy = workbooks;
                workbook = workbooksProxy.Open(
                    item.Path,
                    UpdateLinks: 0,
                    ReadOnly: false,
                    AddToMru: false);
                return new RestoreItemResult(item, RestoreItemStatus.Opened, "已通过 Excel 打开。");
            }
            catch (COMException ex)
            {
                return new RestoreItemResult(item, RestoreItemStatus.RequiresUserAction, $"Excel 无法打开文件：{ex.Message}");
            }
            finally
            {
                ComHelpers.TryRestoreAutomationSecurity(application, previousSecurity);
                ComHelpers.Release(workbook);
                ComHelpers.Release(workbooks);
                ComHelpers.Release(applicationObject);
            }
        }, cancellationToken);
    }

    private static string? TryGetWorkbookPath(dynamic workbook)
    {
        try
        {
            var fullName = (string?)workbook.FullName;
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
            return new RestoreItemResult(item, RestoreItemStatus.Opened, "Excel 未在运行，已通过系统文件关联打开。");
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return new RestoreItemResult(item, RestoreItemStatus.ApplicationUnavailable, $"无法启动 Excel：{ex.Message}");
        }
    }
}
