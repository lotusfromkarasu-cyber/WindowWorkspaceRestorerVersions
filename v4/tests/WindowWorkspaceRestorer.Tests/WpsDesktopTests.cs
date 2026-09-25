using WindowWorkspaceRestorer.Interop;
using WindowWorkspaceRestorer.Models;
using WindowWorkspaceRestorer.Services;

namespace WindowWorkspaceRestorer.Tests;

[Collection("Desktop")]
public sealed class WpsDesktopTests
{
    [DesktopFact]
    [Trait("Category", "WpsDesktop")]
    public async Task WriterSpreadsheetAndPresentationTabsCaptureRestoreInTheirWindows()
    {
        var root = Path.Combine(Path.GetTempPath(), "WWR-WpsTabs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var cases = new[] {
            (Type: WorkspaceItemType.WpsWriterDocument, ProgId: "kwps.Application", Extension: ".docx", Format: 12),
            (Type: WorkspaceItemType.WpsSpreadsheet, ProgId: "ket.Application", Extension: ".xlsx", Format: 51),
            (Type: WorkspaceItemType.WpsPresentation, ProgId: "kwpp.Application", Extension: ".pptx", Format: 24) };
        var adapters = cases.Select(entry => new WpsAdapter(entry.Type)).ToArray();
        var completed = false;
        var mixedItems = new List<WorkspaceItem>();
        try
        {
            foreach (var entry in cases)
            {
                var adapter = adapters.Single(a => a.ItemType == entry.Type);
                var paths = Enumerable.Range(1, 2).Select(i => Path.Combine(root, $"标签 {i}-{Path.GetFileName(root)[^6..]}{entry.Extension}")).ToArray();
                await StaRunner.RunAsync(() =>
                {
                    var app = Activator.CreateInstance(Type.GetTypeFromProgID(entry.ProgId)!)!;
                    object? documents = null;
                    try
                    {
                        ((dynamic)app).Visible = true;
                        documents = adapter.GetDocuments(app);
                        foreach (var path in paths)
                        {
                            object document = ((dynamic)documents).Add();
                            try
                            {
                                if (entry.Type == WorkspaceItemType.WpsPresentation)
                                {
                                    object slides = ((dynamic)document).Slides;
                                    object slide = ((dynamic)slides).Add(1, 12);
                                    WpsAdapter.Release(slide); WpsAdapter.Release(slides);
                                }
                                ((dynamic)document).SaveAs(path, entry.Format);
                            }
                            finally { CloseDocument(document, entry.Type); WpsAdapter.Release(document); }
                        }
                    }
                    finally { WpsAdapter.Release(documents); WpsAdapter.Release(app); }
                    return true;
                });
                var now = DateTimeOffset.Now;
                var items = paths.Select(path => new WorkspaceItem(entry.Type, path, Path.GetFileName(path), "wps-test-" + entry.Type)).ToArray();
                mixedItems.AddRange(items.Select(item => item with { GroupKey = "wps-mixed" }));
                var service = new WorkspaceRestoreService(new[] { adapter });
                var record = new WorkspaceRecord(Guid.NewGuid(), "WPS tabs", now, now, items);
                var restored = await service.RestoreAsync(record);
                foreach (var result in restored.Results) Console.WriteLine($"{entry.Type}: {result.Status} {result.Message}");
                Assert.All(restored.Results, result => Assert.True(result.Status == RestoreItemStatus.Opened, result.Message));
                var captured = (await adapter.ScanAsync(default)).Where(candidate => paths.Contains(candidate.Item.Path)).ToArray();
                Console.WriteLine($"Captured {entry.Type}: {captured.Length}; {string.Join(';', captured.Select(c => c.WindowHandle + " " + c.Item.Path))}");
                Assert.Equal(2, captured.Length);
                Assert.Single(captured.Select(candidate => candidate.WindowHandle).Distinct());
                Assert.All(captured, candidate => Assert.True(NativeMethods.IsWindow(candidate.WindowHandle)));
                var store = new PersistenceService(Path.Combine(root, entry.Type.ToString()));
                await store.SaveAsync(new[] { record with { Items = captured.Select(c => c.Item).ToArray() } });
                var loaded = Assert.Single((await store.LoadAsync()).Records);
                var repeated = await service.RestoreAsync(loaded);
                Assert.All(repeated.Results, result => Assert.Equal(RestoreItemStatus.AlreadyOpen, result.Status));
                await CloseTestDocuments(adapter, paths);
                var reopened = await service.RestoreAsync(loaded);
                foreach (var result in reopened.Results) Console.WriteLine($"Reopened {entry.Type}: {result.Status} {result.Message}");
                Assert.All(reopened.Results, result => Assert.True(result.Status == RestoreItemStatus.Opened, result.Message));
                Assert.Single((await adapter.ScanAsync(default)).Where(c => paths.Contains(c.Item.Path)).Select(c => c.WindowHandle).Distinct());
                await CloseTestDocuments(adapter, paths);
            }
            var mixedService = new WorkspaceRestoreService(adapters);
            var mixedNow = DateTimeOffset.Now;
            var mixedRecord = new WorkspaceRecord(Guid.NewGuid(), "WPS mixed tabs", mixedNow, mixedNow, mixedItems.ToArray());
            var mixedRestored = await mixedService.RestoreAsync(mixedRecord);
            Assert.All(mixedRestored.Results, result => Assert.True(result.Status == RestoreItemStatus.Opened, result.Message));
            var mixedCaptured = new List<CaptureCandidate>();
            foreach (var adapter in adapters)
                mixedCaptured.AddRange((await adapter.ScanAsync(default)).Where(c => mixedItems.Any(item => item.Path == c.Item.Path)));
            Assert.Equal(6, mixedCaptured.Count);
            Assert.Single(mixedCaptured.Select(c => c.WindowHandle).Distinct());
            var mixedRepeated = await mixedService.RestoreAsync(mixedRecord);
            Assert.All(mixedRepeated.Results, result => Assert.Equal(RestoreItemStatus.AlreadyOpen, result.Status));
            Console.WriteLine("Mixed WPS window: six Writer/Spreadsheet/Presentation tabs verified; repeated restore skipped all six.");
            completed = true;
        }
        catch (Exception ex) { Console.WriteLine("Primary failure: " + ex); throw; }
        finally
        {
            foreach (var adapter in adapters)
                await CloseTestDocuments(adapter, Directory.GetFiles(root));
            try { if (completed) Directory.Delete(root, true); else Console.WriteLine("Retained failed fixture: " + root); }
            catch (IOException ex) { Console.WriteLine("Test cleanup pending: " + root + " " + ex.Message); }
        }
    }

    private static Task<bool> CloseTestDocuments(WpsAdapter adapter, string[] paths) => StaRunner.RunAsync(() =>
    {
        var applications = adapter.GetApplications();
        try
        {
            foreach (var app in applications)
            {
                object? documents = null;
                try
                {
                    documents = adapter.GetDocuments(app.Application);
                    for (var i = (int)((dynamic)documents).Count; i >= 1; i--)
                    {
                        object document = ((dynamic)documents).Item(i);
                        try { if (paths.Contains((string)((dynamic)document).FullName)) CloseDocument(document, adapter.ItemType); }
                        finally { WpsAdapter.Release(document); }
                    }
                }
                finally { WpsAdapter.Release(documents); }
            }
        }
        finally { foreach (var app in applications) WpsAdapter.Release(app.Application); }
        return true;
    });

    private static void CloseDocument(dynamic document, WorkspaceItemType type)
    {
        if (type == WorkspaceItemType.WpsPresentation) document.Close();
        else if (type == WorkspaceItemType.WpsSpreadsheet) document.Close(false);
        else document.Close(0);
    }
}
