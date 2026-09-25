using WindowWorkspaceRestorer.Models;

namespace WindowWorkspaceRestorer.Services;

public interface IWorkspaceAdapter
{
    string AdapterId { get; }

    WorkspaceItemType ItemType { get; }

    Task<IReadOnlyList<CaptureCandidate>> ScanAsync(CancellationToken cancellationToken);

    Task<RestoreItemResult> RestoreAsync(
        WorkspaceItem item,
        RestoreContext context,
        CancellationToken cancellationToken);
}
