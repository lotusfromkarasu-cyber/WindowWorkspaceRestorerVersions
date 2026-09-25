namespace WindowWorkspaceRestorer.Models;

public enum WorkspaceItemType
{
    WordDocument,
    ExcelWorkbook,
    PowerPointPresentation,
    ExplorerFolder,
    ComsolModel,
    WpsWriterDocument,
    WpsSpreadsheet,
    WpsPresentation
}

public sealed record WorkspaceItem(
    WorkspaceItemType Type,
    string Path,
    string DisplayName,
    string? GroupKey = null);

public sealed record WorkspaceRecord(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<WorkspaceItem> Items);
