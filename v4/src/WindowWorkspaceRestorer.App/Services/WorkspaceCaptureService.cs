using System.Runtime.InteropServices;
using WindowWorkspaceRestorer.Models;

namespace WindowWorkspaceRestorer.Services;

public sealed class WorkspaceCaptureService
{
    private readonly IReadOnlyList<IWorkspaceAdapter> _adapters;

    public WorkspaceCaptureService(IEnumerable<IWorkspaceAdapter> adapters)
    {
        _adapters = adapters.ToArray();
    }

    public async Task<IReadOnlyList<CaptureCandidate>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var candidates = new List<CaptureCandidate>();
        foreach (var adapter in _adapters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                candidates.AddRange(await adapter.ScanAsync(cancellationToken));
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
            {
                candidates.Add(new CaptureCandidate(
                    new WorkspaceItem(adapter.ItemType, string.Empty, adapter.AdapterId),
                    canPersist: false,
                    warning: $"无法读取 {adapter.AdapterId}：{ex.Message}"));
            }
        }

        return candidates
            .GroupBy(
                x => x.CanPersist
                    ? CreateCandidateKey(x.Item)
                    : $"{x.Item.Type}:warning:{x.Item.DisplayName}:{x.Warning}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(x => x.Item.Type)
            .ThenBy(x => x.Item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static string CreateCandidateKey(WorkspaceItem item)
    {
        var group = item.Type == WorkspaceItemType.ExplorerFolder
            ? $":{item.GroupKey ?? "legacy"}"
            : string.Empty;
        return $"{item.Type}:{PathNormalizer.Normalize(item.Path)}{group}";
    }
}
