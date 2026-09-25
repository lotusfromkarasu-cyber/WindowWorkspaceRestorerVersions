namespace WindowWorkspaceRestorer.Models;

public sealed record WindowSnapshot(
    nint Handle,
    uint ProcessId,
    string ProcessName,
    string Title);
