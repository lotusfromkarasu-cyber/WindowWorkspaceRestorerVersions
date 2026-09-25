using WindowWorkspaceRestorer.Models;
using WindowWorkspaceRestorer.Services;

namespace WindowWorkspaceRestorer.Tests;

public sealed class WorkspaceCaptureServiceTests
{
    [Fact]
    public async Task UnsavedWarningRemainsVisibleButCannotBePersisted()
    {
        var candidate = new CaptureCandidate(
            new WorkspaceItem(WorkspaceItemType.WordDocument, string.Empty, "未保存 Word 文档"),
            canPersist: false,
            warning: "尚未保存");
        var service = new WorkspaceCaptureService(new[] { new FakeAdapter(candidate) });

        var result = await service.ScanAsync();

        var actual = Assert.Single(result);
        Assert.False(actual.CanPersist);
        Assert.Equal("尚未保存", actual.Warning);
    }

    [Fact]
    public async Task ScanKeepsTheSameExplorerPathWhenItBelongsToDifferentWindows()
    {
        var path = @"C:\Work\Shared";
        var first = new CaptureCandidate(new WorkspaceItem(
            WorkspaceItemType.ExplorerFolder,
            path,
            "Shared",
            "explorer:100"));
        var second = new CaptureCandidate(new WorkspaceItem(
            WorkspaceItemType.ExplorerFolder,
            path,
            "Shared",
            "explorer:200"));
        var service = new WorkspaceCaptureService(new[]
        {
            new FakeAdapter(first, second)
        });

        var results = await service.ScanAsync();

        Assert.Equal(2, results.Count);
        Assert.Contains(results, x => x.Item.GroupKey == "explorer:100");
        Assert.Contains(results, x => x.Item.GroupKey == "explorer:200");
    }

    private sealed class FakeAdapter : IWorkspaceAdapter
    {
        private readonly IReadOnlyList<CaptureCandidate> _candidates;

        public FakeAdapter(params CaptureCandidate[] candidates)
        {
            _candidates = candidates;
        }

        public string AdapterId => "fake";

        public WorkspaceItemType ItemType => _candidates[0].Item.Type;

        public Task<IReadOnlyList<CaptureCandidate>> ScanAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_candidates);
        }

        public Task<RestoreItemResult> RestoreAsync(
            WorkspaceItem item,
            RestoreContext context,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new RestoreItemResult(item, RestoreItemStatus.Opened, "fake"));
        }
    }
}
