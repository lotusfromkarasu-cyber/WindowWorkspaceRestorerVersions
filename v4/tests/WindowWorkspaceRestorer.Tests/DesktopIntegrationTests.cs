using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using WindowWorkspaceRestorer.Interop;
using WindowWorkspaceRestorer.Models;
using WindowWorkspaceRestorer.Services;

namespace WindowWorkspaceRestorer.Tests;

public sealed class DesktopFactAttribute : FactAttribute
{
    public DesktopFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("WWR_DESKTOP_TESTS") != "1")
            Skip = "Set WWR_DESKTOP_TESTS=1 to exercise real Explorer/PowerPoint windows.";
    }
}

[CollectionDefinition("Desktop", DisableParallelization = true)]
public sealed class DesktopCollection { }

[Collection("Desktop")]
public sealed class DesktopIntegrationTests
{
    [DesktopFact]
    [Trait("Category", "Desktop")]
    public async Task ExplorerRestoresTabsRepeatedlyWithoutReplacingHomeOrExistingFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "WWR-v4-Explorer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var ownedHandles = new HashSet<nint>();
        var adapter = new ExplorerAdapter();
        try
        {
            var items = Enumerable.Range(0, 6).Select(index =>
            {
                var path = Directory.CreateDirectory(Path.Combine(root, $"文件夹 {index}")).FullName;
                return new WorkspaceItem(WorkspaceItemType.ExplorerFolder, path, $"folder {index}", "test-group");
            }).ToArray();
            for (var round = 0; round < 3; round++)
            {
                var context = new RestoreContext(new HashSet<string>(), TimeSpan.FromSeconds(15));
                var first = await adapter.RestoreAsync(items[0], context, default);
                Assert.Equal(RestoreItemStatus.Opened, first.Status);
                var handle = context.ExplorerGroupHandles["test-group"];
                ownedHandles.Add(handle);
                // Leave a real Home tab beside the first folder. Its null path
                // triggered the old "new tab" heuristic on every subsequent restore.
                await StaRunner.RunAsync(() =>
                {
                    var before = ReadTabs(handle).Count;
                    nint active = 0;
                    NativeMethods.EnumChildWindows(handle, (child, _) =>
                    {
                        var name = new StringBuilder(64);
                        NativeMethods.GetClassName(child, name, name.Capacity);
                        if (name.ToString() == "ShellTabWindowClass" && NativeMethods.IsWindowVisible(child))
                        { active = child; return false; }
                        return true;
                    }, 0);
                    Assert.NotEqual((nint)0, active);
                    Assert.True(NativeMethods.PostMessage(active, NativeMethods.WindowMessageCommand,
                        NativeMethods.ExplorerCommandNewTab, 0));
                    var timer = Stopwatch.StartNew();
                    while (ReadTabs(handle).Count <= before && timer.Elapsed < TimeSpan.FromSeconds(10)) Thread.Sleep(100);
                    Assert.Equal(before + 1, ReadTabs(handle).Count);
                    return true;
                });
                var baseline = await StaRunner.RunAsync(() => ReadTabs(handle));
                foreach (var item in items.Skip(1))
                {
                    var result = await adapter.RestoreAsync(item, context, default);
                    Assert.True(result.Status == RestoreItemStatus.Opened, result.Message);
                }
                var actual = await StaRunner.RunAsync(() => ReadTabs(handle));
                Assert.Equal(baseline.Count + items.Length - 1, actual.Count);
                foreach (var path in baseline) Assert.Contains(path, actual);
                foreach (var item in items) Assert.Contains(actual, path => path is not null && PathNormalizer.Same(path, item.Path));

                var now = DateTimeOffset.Now;
                var record = new WorkspaceRecord(Guid.NewGuid(), "integration", now, now, items);
                var summary = await new WorkspaceRestoreService(new[] { adapter }).RestoreAsync(record);
                Assert.All(summary.Results, result => Assert.Equal(RestoreItemStatus.AlreadyOpen, result.Status));
                Assert.Equal(actual.Count, (await StaRunner.RunAsync(() => ReadTabs(handle))).Count);
                NativeMethods.PostMessage(handle, 0x0112, 0xF060, 0);
                var closeTimer = Stopwatch.StartNew();
                while (NativeMethods.IsWindow(handle) && closeTimer.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(100);
                Assert.False(NativeMethods.IsWindow(handle));
                ownedHandles.Remove(handle);
            }
        }
        finally
        {
            foreach (var handle in ownedHandles) NativeMethods.PostMessage(handle, 0x0112, 0xF060, 0);
            Directory.Delete(root, true);
        }
    }

    private static IReadOnlyList<string?> ReadTabs(nint handle)
    {
        var result = new List<string?>();
        var shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        object? windows = null;
        try
        {
            windows = ((dynamic)shell).Windows();
            foreach (var window in (System.Collections.IEnumerable)windows)
            {
                try
                {
                    if (Convert.ToInt64(((dynamic)window).HWND) == handle.ToInt64())
                        result.Add(ShellInterop.TryGetFileSystemPath((string?)((dynamic)window).LocationURL));
                }
                finally { Marshal.ReleaseComObject(window); }
            }
        }
        finally
        {
            PowerPointWindows.Release(windows);
            PowerPointWindows.Release(shell);
        }
        return result;
    }

    [DesktopFact]
    [Trait("Category", "Desktop")]
    public async Task PowerPointCaptureSaveCloseRestoreAndRescan()
    {
        var root = Path.Combine(Path.GetTempPath(), "WWR-v4-PowerPoint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var paths = new[] { Path.Combine(root, "演示 文稿一.pptx"), Path.Combine(root, "演示 文稿二.pptx") };
        var wasRunning = new WindowScanner().HasVisibleProcess("POWERPNT.EXE");
        try
        {
            await StaRunner.RunAsync(() =>
            {
                var app = PowerPointWindows.TryGetApplication() ?? Activator.CreateInstance(Type.GetTypeFromProgID("PowerPoint.Application")!)!;
                object? presentations = null;
                try
                {
                    ((dynamic)app).Visible = -1;
                    presentations = ((dynamic)app).Presentations;
                    foreach (var path in paths)
                    {
                        object presentation = ((dynamic)presentations).Add(-1);
                        object slides = ((dynamic)presentation).Slides;
                        object slide = ((dynamic)slides).Add(1, 12);
                        try { ((dynamic)presentation).SaveAs(path); }
                        finally
                        {
                            PowerPointWindows.Release(slide);
                            PowerPointWindows.Release(slides);
                            PowerPointWindows.Release(presentation);
                        }
                    }
                }
                finally { PowerPointWindows.Release(presentations); PowerPointWindows.Release(app); }
                return true;
            });
            var adapter = new PowerPointAdapter();
            var captured = (await adapter.ScanAsync(default)).Where(candidate => paths.Contains(candidate.Item.Path)).ToArray();
            Assert.Equal(2, captured.Length);
            Assert.All(captured, candidate => { Assert.True(candidate.CanPersist); Assert.NotEqual((nint)0, candidate.WindowHandle); });
            // Verify the native window fallback separately from ROT enumeration.
            await StaRunner.RunAsync(() =>
            {
                foreach (var candidate in captured)
                {
                    var native = PowerPointWindows.TryGetDocumentWindow(candidate.WindowHandle);
                    try { Assert.NotNull(native); }
                    finally { PowerPointWindows.Release(native); }
                }
                return true;
            });
            var now = DateTimeOffset.Now;
            var store = new PersistenceService(Path.Combine(root, "records"));
            await store.SaveAsync(new[] { new WorkspaceRecord(Guid.NewGuid(), "PPT integration", now, now, captured.Select(c => c.Item).ToArray()) });
            await ClosePresentations(paths, quitIfEmpty: !wasRunning);
            var loaded = await store.LoadAsync();
            var summary = await new WorkspaceRestoreService(new[] { adapter }).RestoreAsync(Assert.Single(loaded.Records));
            Assert.All(summary.Results, result => Assert.True(result.Status == RestoreItemStatus.Opened, result.Message));
            var restored = (await adapter.ScanAsync(default)).Where(candidate => paths.Contains(candidate.Item.Path)).ToArray();
            Assert.Equal(2, restored.Length);
            Assert.All(restored, candidate => Assert.NotEqual((nint)0, candidate.WindowHandle));
            var again = await new WorkspaceRestoreService(new[] { adapter }).RestoreAsync(loaded.Records[0]);
            Assert.All(again.Results, result => Assert.Equal(RestoreItemStatus.AlreadyOpen, result.Status));
        }
        finally
        {
            await ClosePresentations(paths, quitIfEmpty: !wasRunning);
            Directory.Delete(root, true);
        }
    }

    private static Task<bool> ClosePresentations(string[] paths, bool quitIfEmpty) => StaRunner.RunAsync(() =>
    {
        var app = PowerPointWindows.TryGetApplication();
        object? presentations = null;
        try
        {
            if (app is null) return true;
            presentations = ((dynamic)app).Presentations;
            for (var i = (int)((dynamic)presentations).Count; i >= 1; i--)
            {
                object presentation = ((dynamic)presentations).Item(i);
                try
                {
                    if (paths.Contains((string)((dynamic)presentation).FullName)) ((dynamic)presentation).Close();
                }
                finally { PowerPointWindows.Release(presentation); }
            }
            if (quitIfEmpty && (int)((dynamic)presentations).Count == 0) ((dynamic)app).Quit();
        }
        finally { PowerPointWindows.Release(presentations); PowerPointWindows.Release(app); }
        return true;
    });
}
