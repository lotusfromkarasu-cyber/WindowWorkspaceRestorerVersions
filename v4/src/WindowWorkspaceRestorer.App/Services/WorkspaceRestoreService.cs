using System.IO;
using WindowWorkspaceRestorer.Models;

namespace WindowWorkspaceRestorer.Services;

public sealed class WorkspaceRestoreService
{
    private readonly IReadOnlyDictionary<WorkspaceItemType, IWorkspaceAdapter> _adapters;

    public WorkspaceRestoreService(IEnumerable<IWorkspaceAdapter> adapters)
    {
        _adapters = adapters.ToDictionary(x => x.ItemType);
    }

    public async Task<RestoreSummary> RestoreAsync(
        WorkspaceRecord record,
        CancellationToken cancellationToken = default)
    {
        var currentlyOpen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var explorerPathsByHandle = new Dictionary<nint, HashSet<string>>();
        var wpsHandlesByPath = new Dictionary<string, nint>(StringComparer.OrdinalIgnoreCase);

        foreach (var adapter in _adapters.Values)
        {
            try
            {
                var candidates = await adapter.ScanAsync(cancellationToken);
                foreach (var candidate in candidates.Where(x => x.CanPersist))
                {
                    currentlyOpen.Add(CreateKey(candidate.Item.Type, candidate.Item.Path));
                    if (WpsAdapter.IsWps(candidate.Item.Type) && candidate.WindowHandle != 0)
                        wpsHandlesByPath.TryAdd(CreateKey(candidate.Item.Type, candidate.Item.Path), candidate.WindowHandle);
                    if (candidate.Item.Type == WorkspaceItemType.ExplorerFolder &&
                        !string.IsNullOrWhiteSpace(candidate.Item.GroupKey) &&
                        candidate.WindowHandle != nint.Zero)
                    {
                        if (!explorerPathsByHandle.TryGetValue(candidate.WindowHandle, out var paths))
                        {
                            paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            explorerPathsByHandle[candidate.WindowHandle] = paths;
                        }
                        paths.Add(CreateKey(candidate.Item.Type, candidate.Item.Path));
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // A single unavailable adapter must not block other applications.
            }
        }

        var context = new RestoreContext(currentlyOpen, TimeSpan.FromSeconds(20));
        foreach (var item in record.Items.Where(item => WpsAdapter.IsWps(item.Type) && item.GroupKey is not null))
            if (wpsHandlesByPath.TryGetValue(CreateKey(item.Type, item.Path), out var wpsHandle))
                context.WpsGroupHandles.TryAdd(item.GroupKey!, wpsHandle);
        // Saved HWNDs are session-local and may be reused by Windows. Match each
        // saved group once, by its paths; never overwrite its assignment per item.
        var assignedExplorerHandles = new HashSet<nint>();
        foreach (var group in record.Items
                     .Where(item => item.Type == WorkspaceItemType.ExplorerFolder && !string.IsNullOrWhiteSpace(item.GroupKey))
                     .GroupBy(item => item.GroupKey!, StringComparer.OrdinalIgnoreCase))
        {
            var keys = group.Select(item => CreateKey(item.Type, item.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var match = explorerPathsByHandle
                .Where(entry => !assignedExplorerHandles.Contains(entry.Key))
                .Select(entry => new { Handle = entry.Key, Paths = entry.Value, Count = entry.Value.Count(keys.Contains) })
                .Where(entry => entry.Count > 0)
                .OrderByDescending(entry => entry.Count)
                .FirstOrDefault();
            if (match is null) continue;
            assignedExplorerHandles.Add(match.Handle);
            context.ExplorerGroupHandles[group.Key] = match.Handle;
            foreach (var key in match.Paths) AddExplorerOpenKey(context, group.Key, key);
        }
        var results = new List<RestoreItemResult>();
        foreach (var item in record.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = CreateKey(item.Type, item.Path);
            if (IsAlreadyOpen(item, key, context))
            {
                results.Add(new RestoreItemResult(item, RestoreItemStatus.AlreadyOpen, "已打开，跳过重复操作。"));
                continue;
            }

            if (!_adapters.TryGetValue(item.Type, out var adapter))
            {
                results.Add(new RestoreItemResult(item, RestoreItemStatus.Unsupported, "没有可用的恢复适配器。"));
                continue;
            }

            if (!PathExists(item))
            {
                results.Add(new RestoreItemResult(item, RestoreItemStatus.Missing, "文件或文件夹不存在。"));
                continue;
            }

            RestoreItemResult result;
            try
            {
                result = await adapter.RestoreAsync(item, context, cancellationToken);
            }
            catch (UnauthorizedAccessException ex)
            {
                result = new RestoreItemResult(item, RestoreItemStatus.RequiresUserAction, $"权限不足：{ex.Message}");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                result = new RestoreItemResult(item, RestoreItemStatus.Failed, ex.Message);
            }

            results.Add(result);
            if (result.Status == RestoreItemStatus.Opened)
            {
                context.AlreadyOpenKeys.Add(key);
                if (item.Type == WorkspaceItemType.ExplorerFolder &&
                    !string.IsNullOrWhiteSpace(item.GroupKey))
                {
                    AddExplorerOpenKey(context, item.GroupKey, key);
                }
            }
        }

        return new RestoreSummary(results);
    }

    private static bool PathExists(WorkspaceItem item)
    {
        return item.Type == WorkspaceItemType.ExplorerFolder
            ? Directory.Exists(item.Path)
            : File.Exists(item.Path);
    }

    private static string CreateKey(WorkspaceItemType type, string path)
    {
        return $"{type}:{PathNormalizer.Normalize(path)}";
    }

    private static bool IsAlreadyOpen(WorkspaceItem item, string key, RestoreContext context)
    {
        if (item.Type != WorkspaceItemType.ExplorerFolder)
        {
            return context.AlreadyOpenKeys.Contains(key);
        }

        if (string.IsNullOrWhiteSpace(item.GroupKey))
        {
            return context.AlreadyOpenKeys.Contains(key);
        }

        return context.ExplorerGroupOpenKeys.TryGetValue(item.GroupKey, out var keys) &&
            keys.Contains(key);
    }

    private static void AddExplorerOpenKey(RestoreContext context, string groupKey, string key)
    {
        if (!context.ExplorerGroupOpenKeys.TryGetValue(groupKey, out var keys))
        {
            keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            context.ExplorerGroupOpenKeys[groupKey] = keys;
        }

        keys.Add(key);
    }
}
