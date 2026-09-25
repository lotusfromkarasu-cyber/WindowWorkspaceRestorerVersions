namespace WindowWorkspaceRestorer.Models;

public sealed record RestoreContext(
    HashSet<string> AlreadyOpenKeys,
    TimeSpan OpenTimeout)
{
    public IDictionary<string, nint> WpsGroupHandles { get; } =
        new Dictionary<string, nint>(StringComparer.OrdinalIgnoreCase);

    public IDictionary<string, nint> ExplorerGroupHandles { get; } =
        new Dictionary<string, nint>(StringComparer.OrdinalIgnoreCase);

    public ISet<string> ExplorerGroupsRequiringUserAction { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IDictionary<string, ISet<string>> ExplorerGroupOpenKeys { get; } =
        new Dictionary<string, ISet<string>>(StringComparer.OrdinalIgnoreCase);
}
