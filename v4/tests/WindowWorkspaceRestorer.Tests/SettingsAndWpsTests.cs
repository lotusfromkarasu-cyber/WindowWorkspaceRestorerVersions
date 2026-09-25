using WindowWorkspaceRestorer.Models;
using WindowWorkspaceRestorer.Services;

namespace WindowWorkspaceRestorer.Tests;

public sealed class SettingsAndWpsTests
{
    [Fact]
    public void TrayPreferenceIsPersistedInBothDirections()
    {
        var root = Path.Combine(Path.GetTempPath(), "WWR-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new SettingsService(root);
            Assert.True(service.Load().CloseToTray);
            service.Save(new AppSettings(false));
            Assert.False(new SettingsService(root).Load().CloseToTray);
            service.Save(new AppSettings(true));
            Assert.True(new SettingsService(root).Load().CloseToTray);
            Assert.Single(Directory.GetFiles(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task WpsTypesAndMixedTabGroupRoundTripWithoutChangingOfficeTypes()
    {
        var root = Path.Combine(Path.GetTempPath(), "WWR-records-" + Guid.NewGuid().ToString("N"));
        try
        {
            var now = DateTimeOffset.Now;
            var items = new[] {
                new WorkspaceItem(WorkspaceItemType.WpsWriterDocument, @"C:\Work\one.docx", "one", "wps:saved"),
                new WorkspaceItem(WorkspaceItemType.WpsSpreadsheet, @"C:\Work\two.xlsx", "two", "wps:saved"),
                new WorkspaceItem(WorkspaceItemType.WpsPresentation, @"C:\Work\three.pptx", "three", "wps:saved"),
                new WorkspaceItem(WorkspaceItemType.PowerPointPresentation, @"C:\Work\office.pptx", "office") };
            var service = new PersistenceService(root);
            await service.SaveAsync(new[] { new WorkspaceRecord(Guid.NewGuid(), "WPS", now, now, items) });
            Assert.Equal(items, Assert.Single((await service.LoadAsync()).Records).Items);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("", "未保存.docx")]
    [InlineData(@"C:\Work", "relative.xlsx")]
    [InlineData("https://example.com", "https://example.com/demo.pptx")]
    public void WpsRejectsUnsavedAndNonLocalPaths(string directory, string name) => Assert.Null(WpsAdapter.NormalizePath(directory, name));
}
