using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WindowWorkspaceRestorer.Models;

namespace WindowWorkspaceRestorer.Services;

public sealed class PersistenceService
{
    private const int CurrentSchemaVersion = 4;
    private const string LegacyFileName = "records.json";
    private const string PreviousVersionFileName = "records-v3.json";
    private const string RecordsFileName = "records-v4.json";

    private readonly string _directory;
    private readonly string _filePath;
    private readonly string _previousVersionFilePath;
    private readonly string _legacyFilePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public PersistenceService(string? rootDirectory = null)
    {
        _directory = rootDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WindowWorkspaceRestorer");
        _filePath = Path.Combine(_directory, RecordsFileName);
        _previousVersionFilePath = Path.Combine(_directory, PreviousVersionFileName);
        _legacyFilePath = Path.Combine(_directory, LegacyFileName);
    }

    public string FilePath => _filePath;

    public async Task<PersistenceLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(_filePath))
        {
            return await LoadOwnFileAsync(cancellationToken);
        }

        var previousResult = await TryImportFromAsync(
            _previousVersionFilePath,
            "上一版",
            cancellationToken);
        if (previousResult is not null)
        {
            return previousResult;
        }

        var olderResult = await TryImportFromAsync(
            Path.Combine(_directory, "records-v2.json"), "v2", cancellationToken);
        if (olderResult is not null) return olderResult;

        olderResult = await TryImportFromAsync(
            Path.Combine(_directory, "records-v2-comsol.json"), "早期 v2", cancellationToken);
        if (olderResult is not null) return olderResult;

        var legacyResult = await TryImportFromAsync(_legacyFilePath, "旧版", cancellationToken);
        return legacyResult ?? new PersistenceLoadResult(Array.Empty<WorkspaceRecord>());
    }

    private async Task<PersistenceLoadResult> LoadOwnFileAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(_filePath);
            var store = await JsonSerializer.DeserializeAsync<PersistedStore>(
                stream,
                _jsonOptions,
                cancellationToken);
            if (store is null)
            {
                return new PersistenceLoadResult(Array.Empty<WorkspaceRecord>(), "记录文件为空，已使用空记录列表。");
            }

            if (store.SchemaVersion > CurrentSchemaVersion)
            {
                return new PersistenceLoadResult(
                    Array.Empty<WorkspaceRecord>(),
                    $"记录文件版本 {store.SchemaVersion} 高于当前版本，未加载旧格式数据。");
            }

            return new PersistenceLoadResult(
                store.Records ?? Array.Empty<WorkspaceRecord>());
        }
        catch (JsonException ex)
        {
            var backupPath = $"{_filePath}.corrupt-{DateTimeOffset.Now:yyyyMMddHHmmss}.json";
            try
            {
                File.Copy(_filePath, backupPath, overwrite: false);
            }
            catch
            {
                // The original file must remain untouched even when backup creation fails.
            }

            return new PersistenceLoadResult(
                Array.Empty<WorkspaceRecord>(),
                $"记录文件格式损坏，已尝试保留备份：{ex.Message}");
        }
        catch (IOException ex)
        {
            return new PersistenceLoadResult(Array.Empty<WorkspaceRecord>(), $"无法读取记录文件：{ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return new PersistenceLoadResult(Array.Empty<WorkspaceRecord>(), $"没有读取记录文件的权限：{ex.Message}");
        }
    }

    private async Task<PersistenceLoadResult?> TryImportFromAsync(
        string sourcePath,
        string sourceLabel,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(sourcePath);
            var store = await JsonSerializer.DeserializeAsync<PersistedStore>(
                stream,
                _jsonOptions,
                cancellationToken);
            if (store is null)
            {
                return new PersistenceLoadResult(Array.Empty<WorkspaceRecord>(), $"{sourceLabel}记录文件为空，未导入。");
            }

            if (store.SchemaVersion > CurrentSchemaVersion)
            {
                return new PersistenceLoadResult(
                    Array.Empty<WorkspaceRecord>(),
                    $"{sourceLabel}记录文件版本 {store.SchemaVersion} 高于当前版本，未导入。");
            }

            var records = store.Records ?? Array.Empty<WorkspaceRecord>();
            if (records.Count == 0)
            {
                return null;
            }

            await SaveAsync(records, cancellationToken);
            return new PersistenceLoadResult(
                records,
                $"已从{sourceLabel}记录文件导入 {records.Count} 条记录到本版本。");
        }
        catch (JsonException ex)
        {
            return new PersistenceLoadResult(Array.Empty<WorkspaceRecord>(), $"{sourceLabel}记录文件损坏，未导入：{ex.Message}");
        }
        catch (IOException ex)
        {
            return new PersistenceLoadResult(Array.Empty<WorkspaceRecord>(), $"无法读取{sourceLabel}记录文件：{ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return new PersistenceLoadResult(Array.Empty<WorkspaceRecord>(), $"没有读取{sourceLabel}记录文件的权限：{ex.Message}");
        }
    }

    public async Task SaveAsync(
        IEnumerable<WorkspaceRecord> records,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);

        var tempPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var store = new PersistedStore(CurrentSchemaVersion, records.ToArray());
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, store, _jsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(tempPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
