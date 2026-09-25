using WindowWorkspaceRestorer.Models;
using WindowWorkspaceRestorer.Services;

namespace WindowWorkspaceRestorer.Tests;

public sealed class PersistenceServiceTests
{
    [Fact]
    public async Task SaveAndLoadRoundTripsRecords()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var service = new PersistenceService(root);
            var now = DateTimeOffset.UtcNow;
            var record = new WorkspaceRecord(
                Guid.NewGuid(),
                "测试工作区",
                now,
                now,
                new[]
                {
                    new WorkspaceItem(
                        WorkspaceItemType.WordDocument,
                        @"C:\Work\Report.docx",
                        "Report.docx"),
                    new WorkspaceItem(
                        WorkspaceItemType.ExplorerFolder,
                        @"C:\Work\Reports",
                        "Reports",
                        "explorer:1234"),
                    new WorkspaceItem(
                        WorkspaceItemType.PowerPointPresentation,
                        @"C:\Work\Slides.pptx",
                        "Slides.pptx")
                });

            await service.SaveAsync(new[] { record });
            var loaded = await service.LoadAsync();

            var actual = Assert.Single(loaded.Records);
            Assert.Equal(record.Id, actual.Id);
            Assert.Equal(record.Name, actual.Name);
            Assert.Equal(record.Items, actual.Items);
            Assert.Equal("explorer:1234", actual.Items[1].GroupKey);
            Assert.Null(loaded.Warning);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CorruptFileIsBackedUpAndReturnsWarning()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var service = new PersistenceService(root);
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(service.FilePath, "{ invalid json");

            var result = await service.LoadAsync();

            Assert.Empty(result.Records);
            Assert.NotNull(result.Warning);
            Assert.Single(Directory.GetFiles(root, $"{Path.GetFileName(service.FilePath)}.corrupt-*.json"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task OwnRecordFileNameIsVersionSpecific()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var service = new PersistenceService(root);

            Assert.Equal("records-v4.json", Path.GetFileName(service.FilePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SaveWritesSchemaVersionFour()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var service = new PersistenceService(root);
            var now = DateTimeOffset.UtcNow;
            var record = new WorkspaceRecord(
                Guid.NewGuid(),
                "COMSOL 工作区",
                now,
                now,
                new[]
                {
                    new WorkspaceItem(WorkspaceItemType.ComsolModel, @"D:\Work\model.mph", "model.mph")
                });

            await service.SaveAsync(new[] { record });

            var json = await File.ReadAllTextAsync(service.FilePath);
            Assert.Contains("\"SchemaVersion\": 4", json);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SchemaOneRecordsStillLoad()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var service = new PersistenceService(root);
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(service.FilePath, SchemaOneJson);

            var result = await service.LoadAsync();

            var record = Assert.Single(result.Records);
            Assert.Equal("旧版记录", record.Name);
            Assert.Equal(WorkspaceItemType.WordDocument, record.Items[0].Type);
            Assert.Null(result.Warning);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LegacyRecordsAreImportedOnceThenVersionsAreIndependent()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(root);
            var legacyPath = Path.Combine(root, "records.json");
            await File.WriteAllTextAsync(legacyPath, SchemaOneJson);

            var service = new PersistenceService(root);
            var first = await service.LoadAsync();

            var imported = Assert.Single(first.Records);
            Assert.Equal("旧版记录", imported.Name);
            Assert.Contains("导入", first.Warning);
            Assert.True(File.Exists(service.FilePath));
            Assert.True(File.Exists(legacyPath));

            var now = DateTimeOffset.UtcNow;
            var newRecord = new WorkspaceRecord(
                Guid.NewGuid(),
                "新版本记录",
                now,
                now,
                new[]
                {
                    new WorkspaceItem(WorkspaceItemType.ComsolModel, @"D:\Work\model.mph", "model.mph")
                });
            await service.SaveAsync(new[] { imported, newRecord });

            var again = await service.LoadAsync();
            Assert.Equal(2, again.Records.Count);
            Assert.Contains(again.Records, x => x.Name == "新版本记录");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PreviousVersionRecordsAreImportedOnceThenVersionsAreIndependent()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(root);
            var previousPath = Path.Combine(root, "records-v3.json");
            var previousJson = @"{
  ""SchemaVersion"": 3,
  ""Records"": [
    {
      ""Id"": ""22222222-2222-2222-2222-222222222222"",
      ""Name"": ""上一版记录"",
      ""CreatedAt"": ""2026-08-30T00:00:00+08:00"",
      ""UpdatedAt"": ""2026-08-30T00:00:00+08:00"",
      ""Items"": [
        {
          ""Type"": ""ComsolModel"",
          ""Path"": ""D:\\Work\\model.mph"",
          ""DisplayName"": ""model.mph""
        }
      ]
    }
  ]
}";
            await File.WriteAllTextAsync(previousPath, previousJson);

            var service = new PersistenceService(root);
            var first = await service.LoadAsync();

            var imported = Assert.Single(first.Records);
            Assert.Equal("上一版记录", imported.Name);
            Assert.Contains("导入", first.Warning);
            Assert.True(File.Exists(service.FilePath));
            Assert.True(File.Exists(previousPath));

            var now = DateTimeOffset.UtcNow;
            var newRecord = new WorkspaceRecord(
                Guid.NewGuid(),
                "本版新记录",
                now,
                now,
                new[]
                {
                    new WorkspaceItem(WorkspaceItemType.PowerPointPresentation, @"D:\Work\slides.pptx", "slides.pptx")
                });
            await service.SaveAsync(new[] { imported, newRecord });

            var again = await service.LoadAsync();
            Assert.Equal(2, again.Records.Count);
            Assert.Contains(again.Records, x => x.Name == "本版新记录");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CorruptLegacyFileIsIgnoredWithoutBlockingStartup()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "records.json"), "{ invalid json");

            var service = new PersistenceService(root);
            var result = await service.LoadAsync();

            Assert.Empty(result.Records);
            Assert.Contains("损坏", result.Warning);
            Assert.False(File.Exists(service.FilePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private const string SchemaOneJson = @"{
  ""SchemaVersion"": 1,
  ""Records"": [
    {
      ""Id"": ""11111111-1111-1111-1111-111111111111"",
      ""Name"": ""旧版记录"",
      ""CreatedAt"": ""2026-08-29T00:00:00+08:00"",
      ""UpdatedAt"": ""2026-08-29T00:00:00+08:00"",
      ""Items"": [
        {
          ""Type"": ""WordDocument"",
          ""Path"": ""C:\\Work\\Report.docx"",
          ""DisplayName"": ""Report.docx""
        }
      ]
    }
  ]
}";

    private static string CreateTemporaryDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "WindowWorkspaceRestorerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
