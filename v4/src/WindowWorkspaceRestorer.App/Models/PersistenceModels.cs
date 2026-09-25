namespace WindowWorkspaceRestorer.Models;

public sealed record PersistedStore(
    int SchemaVersion,
    IReadOnlyList<WorkspaceRecord> Records);

public sealed record PersistenceLoadResult(
    IReadOnlyList<WorkspaceRecord> Records,
    string? Warning = null);

public enum RestoreItemStatus
{
    AlreadyOpen,
    Opened,
    Missing,
    Unsupported,
    ApplicationUnavailable,
    RequiresUserAction,
    Failed
}

public sealed record RestoreItemResult(
    WorkspaceItem Item,
    RestoreItemStatus Status,
    string Message);

public sealed record RestoreSummary(
    IReadOnlyList<RestoreItemResult> Results)
{
    public int SucceededCount => Results.Count(x => x.Status is RestoreItemStatus.Opened or RestoreItemStatus.AlreadyOpen);

    public int FailedCount => Results.Count(x => x.Status is RestoreItemStatus.Failed or RestoreItemStatus.Missing or RestoreItemStatus.ApplicationUnavailable);

    public int RequiresActionCount => Results.Count(x => x.Status == RestoreItemStatus.RequiresUserAction);
}
