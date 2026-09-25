using WindowWorkspaceRestorer.Models;
using WindowWorkspaceRestorer.ViewModels;

namespace WindowWorkspaceRestorer.Tests;

public sealed class RestoreSelectionTests
{
    [Fact]
    public void SavedItemsAreSelectedByDefaultAndExposeFriendlyTypes()
    {
        var item = new WorkspaceItem(
            WorkspaceItemType.WpsPresentation,
            @"C:\Work\slides.pptx",
            "slides.pptx",
            "wps:window");

        var selection = new RestoreSelectionItemViewModel(item);

        Assert.True(selection.IsSelected);
        Assert.Equal("WPS 演示", selection.TypeLabel);
        Assert.Same(item, selection.Item);
    }

    [Fact]
    public void SelectedRestoreRecordContainsOnlyCheckedItemsAndKeepsWorkspaceIdentity()
    {
        var now = DateTimeOffset.UtcNow;
        var first = new WorkspaceItem(WorkspaceItemType.WordDocument, @"C:\Work\one.docx", "one.docx");
        var second = new WorkspaceItem(WorkspaceItemType.ExplorerFolder, @"C:\Work", "Work", "explorer:one");
        var source = new WorkspaceRecord(Guid.NewGuid(), "研究工作区", now, now, new[] { first, second });
        var selections = new[]
        {
            new RestoreSelectionItemViewModel(first) { IsSelected = false },
            new RestoreSelectionItemViewModel(second)
        };

        var selected = MainViewModel.CreateSelectedRestoreRecord(source, selections);

        Assert.Equal(source.Id, selected.Id);
        Assert.Equal(source.Name, selected.Name);
        Assert.Equal(source.CreatedAt, selected.CreatedAt);
        Assert.Equal(source.UpdatedAt, selected.UpdatedAt);
        Assert.Equal(new[] { second }, selected.Items);
        Assert.Equal(2, source.Items.Count);
    }
}
