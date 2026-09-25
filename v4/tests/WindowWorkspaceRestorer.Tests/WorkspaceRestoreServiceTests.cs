using WindowWorkspaceRestorer.Models;
using WindowWorkspaceRestorer.Services;

namespace WindowWorkspaceRestorer.Tests;

public sealed class WorkspaceRestoreServiceTests
{
    [Fact]
    public async Task AllExistingTabsInMatchedWindowAreSkipped()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var first = new WorkspaceItem(WorkspaceItemType.ExplorerFolder, root, "root", "saved");
            var secondPath = Directory.CreateDirectory(Path.Combine(root, "second")).FullName;
            var second = first with { Path = secondPath, DisplayName = "second" };
            var adapter = new FakeAdapter(WorkspaceItemType.ExplorerFolder,
                new[] { first with { GroupKey = "current" }, second with { GroupKey = "current" } }, 1234);
            var summary = await new WorkspaceRestoreService(new[] { adapter }).RestoreAsync(CreateRecord(first, second));
            Assert.All(summary.Results, result => Assert.Equal(RestoreItemStatus.AlreadyOpen, result.Status));
            Assert.Equal(0, adapter.RestoreCalls);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ExistingItemIsSkippedWithoutCallingRestore()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(root, "report.docx");
            await File.WriteAllTextAsync(path, string.Empty);
            var item = new WorkspaceItem(WorkspaceItemType.WordDocument, path, "report.docx");
            var adapter = new FakeAdapter(WorkspaceItemType.WordDocument, item);
            var service = new WorkspaceRestoreService(new[] { adapter });

            var summary = await service.RestoreAsync(CreateRecord(item));

            var result = Assert.Single(summary.Results);
            Assert.Equal(RestoreItemStatus.AlreadyOpen, result.Status);
            Assert.Equal(0, adapter.RestoreCalls);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MissingItemDoesNotStopOtherItems()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var existingPath = Path.Combine(root, "book.xlsx");
            await File.WriteAllTextAsync(existingPath, string.Empty);
            var missingPath = Path.Combine(root, "missing.xlsx");
            var existing = new WorkspaceItem(WorkspaceItemType.ExcelWorkbook, existingPath, "book.xlsx");
            var missing = new WorkspaceItem(WorkspaceItemType.ExcelWorkbook, missingPath, "missing.xlsx");
            var adapter = new FakeAdapter(WorkspaceItemType.ExcelWorkbook);
            var service = new WorkspaceRestoreService(new[] { adapter });

            var summary = await service.RestoreAsync(CreateRecord(existing, missing));

            Assert.Equal(2, summary.Results.Count);
            Assert.Contains(summary.Results, x => x.Item.Path == missingPath && x.Status == RestoreItemStatus.Missing);
            Assert.Contains(summary.Results, x => x.Item.Path == existingPath && x.Status == RestoreItemStatus.Opened);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExistingExplorerTabMapsSavedGroupToCurrentWindow()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var existingPath = Path.Combine(root, "existing");
            var missingPath = Path.Combine(root, "missing");
            Directory.CreateDirectory(existingPath);
            Directory.CreateDirectory(missingPath);

            var currentWindowGroup = "explorer:current-window";
            var savedGroup = "explorer:saved-window";
            var existing = new WorkspaceItem(
                WorkspaceItemType.ExplorerFolder,
                existingPath,
                "existing",
                savedGroup);
            var missing = new WorkspaceItem(
                WorkspaceItemType.ExplorerFolder,
                missingPath,
                "missing",
                savedGroup);
            var currentOpen = existing with { GroupKey = currentWindowGroup };
            var adapter = new FakeAdapter(
                WorkspaceItemType.ExplorerFolder,
                new[] { currentOpen },
                windowHandle: 1234);
            var service = new WorkspaceRestoreService(new[] { adapter });

            var summary = await service.RestoreAsync(CreateRecord(existing, missing));

            var result = Assert.Single(summary.Results, x => x.Item.Path == missingPath);
            Assert.Equal(RestoreItemStatus.Opened, result.Status);
            Assert.Equal(1, adapter.RestoreCalls);
            Assert.Equal((nint)1234, adapter.LastContext?.ExplorerGroupHandles[savedGroup]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SharedExistingFolderDoesNotMergeTwoSavedExplorerWindows()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var sharedPath = Path.Combine(root, "shared");
            var firstOnlyPath = Path.Combine(root, "first-only");
            var secondOnlyPath = Path.Combine(root, "second-only");
            Directory.CreateDirectory(sharedPath);
            Directory.CreateDirectory(firstOnlyPath);
            Directory.CreateDirectory(secondOnlyPath);

            var firstGroup = "explorer:first";
            var secondGroup = "explorer:second";
            var shared = new WorkspaceItem(
                WorkspaceItemType.ExplorerFolder,
                sharedPath,
                "shared",
                firstGroup);
            var firstOnly = new WorkspaceItem(
                WorkspaceItemType.ExplorerFolder,
                firstOnlyPath,
                "first-only",
                firstGroup);
            var secondShared = shared with { GroupKey = secondGroup };
            var secondOnly = new WorkspaceItem(
                WorkspaceItemType.ExplorerFolder,
                secondOnlyPath,
                "second-only",
                secondGroup);
            var currentOpen = shared with { GroupKey = "explorer:current" };
            var adapter = new FakeAdapter(
                WorkspaceItemType.ExplorerFolder,
                new[] { currentOpen },
                windowHandle: 1234);
            var service = new WorkspaceRestoreService(new[] { adapter });

            var summary = await service.RestoreAsync(CreateRecord(shared, firstOnly, secondShared, secondOnly));

            Assert.Equal(3, adapter.RestoreCalls);
            Assert.Equal(RestoreItemStatus.AlreadyOpen, summary.Results[0].Status);
            Assert.Equal(RestoreItemStatus.Opened, summary.Results[1].Status);
            Assert.Equal(RestoreItemStatus.Opened, summary.Results[2].Status);
            Assert.Equal(RestoreItemStatus.Opened, summary.Results[3].Status);
            Assert.Equal((nint)1234, adapter.LastContext?.ExplorerGroupHandles[firstGroup]);
            Assert.False(adapter.LastContext?.ExplorerGroupHandles.ContainsKey(secondGroup));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExistingPowerPointPresentationIsSkippedWithoutCallingRestore()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(root, "slides.pptx");
            await File.WriteAllTextAsync(path, string.Empty);
            var item = new WorkspaceItem(WorkspaceItemType.PowerPointPresentation, path, "slides.pptx");
            var adapter = new FakeAdapter(WorkspaceItemType.PowerPointPresentation, item);
            var service = new WorkspaceRestoreService(new[] { adapter });

            var summary = await service.RestoreAsync(CreateRecord(item));

            var result = Assert.Single(summary.Results);
            Assert.Equal(RestoreItemStatus.AlreadyOpen, result.Status);
            Assert.Equal(0, adapter.RestoreCalls);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MissingPowerPointPresentationIsReportedWithoutCallingRestore()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(root, "missing.pptx");
            var item = new WorkspaceItem(WorkspaceItemType.PowerPointPresentation, path, "missing.pptx");
            var adapter = new FakeAdapter(WorkspaceItemType.PowerPointPresentation);
            var service = new WorkspaceRestoreService(new[] { adapter });

            var summary = await service.RestoreAsync(CreateRecord(item));

            var result = Assert.Single(summary.Results);
            Assert.Equal(RestoreItemStatus.Missing, result.Status);
            Assert.Equal(0, adapter.RestoreCalls);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExistingPowerPointPresentationRestoresWhenNotInCurrentWindows()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(root, "slides.pptx");
            await File.WriteAllTextAsync(path, string.Empty);
            var item = new WorkspaceItem(WorkspaceItemType.PowerPointPresentation, path, "slides.pptx");
            var adapter = new FakeAdapter(WorkspaceItemType.PowerPointPresentation);
            var service = new WorkspaceRestoreService(new[] { adapter });

            var summary = await service.RestoreAsync(CreateRecord(item));

            var result = Assert.Single(summary.Results);
            Assert.Equal(RestoreItemStatus.Opened, result.Status);
            Assert.Equal(1, adapter.RestoreCalls);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static WorkspaceRecord CreateRecord(params WorkspaceItem[] items)
    {
        var now = DateTimeOffset.UtcNow;
        return new WorkspaceRecord(Guid.NewGuid(), "test", now, now, items);
    }

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "WindowWorkspaceRestorerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class FakeAdapter : IWorkspaceAdapter
    {
        private readonly IReadOnlyList<WorkspaceItem> _openItems;
        private readonly nint _windowHandle;

        public FakeAdapter(
            WorkspaceItemType itemType,
            WorkspaceItem[] openItems,
            nint windowHandle = default)
        {
            ItemType = itemType;
            _openItems = openItems;
            _windowHandle = windowHandle;
        }

        public FakeAdapter(WorkspaceItemType itemType, params WorkspaceItem[] openItems)
            : this(itemType, openItems, default)
        {
        }

        public string AdapterId => "fake";

        public WorkspaceItemType ItemType { get; }

        public int RestoreCalls { get; private set; }

        public RestoreContext? LastContext { get; private set; }

        public Task<IReadOnlyList<CaptureCandidate>> ScanAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<CaptureCandidate> result = _openItems
                .Select(item => new CaptureCandidate(item, windowHandle: _windowHandle))
                .ToArray();
            return Task.FromResult(result);
        }

        public Task<RestoreItemResult> RestoreAsync(
            WorkspaceItem item,
            RestoreContext context,
            CancellationToken cancellationToken)
        {
            RestoreCalls++;
            LastContext = context;
            return Task.FromResult(new RestoreItemResult(item, RestoreItemStatus.Opened, "fake"));
        }
    }

    [Fact]
    public async Task ExistingComsolModelIsSkippedWithoutCallingRestore()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(root, "model.mph");
            await File.WriteAllTextAsync(path, string.Empty);
            var item = new WorkspaceItem(WorkspaceItemType.ComsolModel, path, "model.mph");
            var adapter = new FakeAdapter(WorkspaceItemType.ComsolModel, item);
            var service = new WorkspaceRestoreService(new[] { adapter });

            var summary = await service.RestoreAsync(CreateRecord(item));

            var result = Assert.Single(summary.Results);
            Assert.Equal(RestoreItemStatus.AlreadyOpen, result.Status);
            Assert.Equal(0, adapter.RestoreCalls);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MissingComsolModelIsReportedWithoutCallingRestore()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(root, "missing.mph");
            var item = new WorkspaceItem(WorkspaceItemType.ComsolModel, path, "missing.mph");
            var adapter = new FakeAdapter(WorkspaceItemType.ComsolModel);
            var service = new WorkspaceRestoreService(new[] { adapter });

            var summary = await service.RestoreAsync(CreateRecord(item));

            var result = Assert.Single(summary.Results);
            Assert.Equal(RestoreItemStatus.Missing, result.Status);
            Assert.Equal(0, adapter.RestoreCalls);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExistingComsolModelRestoresWhenNotInCurrentWindows()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(root, "model.mph");
            await File.WriteAllTextAsync(path, string.Empty);
            var item = new WorkspaceItem(WorkspaceItemType.ComsolModel, path, "model.mph");
            var adapter = new FakeAdapter(WorkspaceItemType.ComsolModel);
            var service = new WorkspaceRestoreService(new[] { adapter });

            var summary = await service.RestoreAsync(CreateRecord(item));

            var result = Assert.Single(summary.Results);
            Assert.Equal(RestoreItemStatus.Opened, result.Status);
            Assert.Equal(1, adapter.RestoreCalls);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
