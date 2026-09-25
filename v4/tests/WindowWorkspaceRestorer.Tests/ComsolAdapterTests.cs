using WindowWorkspaceRestorer.Models;
using WindowWorkspaceRestorer.Services;

namespace WindowWorkspaceRestorer.Tests;

public sealed class ComsolAdapterTests
{
    private static readonly WindowSnapshot ComsolWindow =
        new(1, 100, "ComsolUI.exe", "model.mph - COMSOL Multiphysics");

    [Fact]
    public async Task QuotedPathWithSpacesIsCaptured()
    {
        var adapter = CreateAdapter(@"""D:\Program Files\COMSOL\ComsolUI.exe"" -open ""D:\My Models\模型 no.1.mph""");

        var results = await adapter.ScanAsync(CancellationToken.None);

        var candidate = Assert.Single(results);
        Assert.True(candidate.CanPersist);
        Assert.Equal(@"D:\My Models\模型 no.1.mph", candidate.Item.Path);
        Assert.Equal("模型 no.1.mph", candidate.Item.DisplayName);
    }

    [Fact]
    public async Task UnquotedPathIsCaptured()
    {
        var adapter = CreateAdapter(@"""C:\COMSOL\ComsolUI.exe"" -open D:\Work\02BIC_final.mph");

        var results = await adapter.ScanAsync(CancellationToken.None);

        var candidate = Assert.Single(results);
        Assert.True(candidate.CanPersist);
        Assert.Equal(@"D:\Work\02BIC_final.mph", candidate.Item.Path);
    }

    [Fact]
    public async Task MultipleOpenFlagsProduceMultipleCandidates()
    {
        var adapter = CreateAdapter(@"""C:\COMSOL\ComsolUI.exe"" -open ""D:\a.mph"" -open D:\b.mph");

        var results = await adapter.ScanAsync(CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, x => x.Item.Path == @"D:\a.mph" && x.CanPersist);
        Assert.Contains(results, x => x.Item.Path == @"D:\b.mph" && x.CanPersist);
    }

    [Fact]
    public async Task ProcessWithoutOpenFlagProducesUnsavedWarning()
    {
        var adapter = CreateAdapter(@"""C:\COMSOL\ComsolUI.exe""");

        var results = await adapter.ScanAsync(CancellationToken.None);

        var candidate = Assert.Single(results);
        Assert.False(candidate.CanPersist);
        Assert.Contains("未保存", candidate.Warning);
    }

    [Fact]
    public async Task FailedCommandLineReadProducesWarning()
    {
        var adapter = new ComsolAdapter(
            windowProvider: () => new[] { ComsolWindow },
            commandLineProvider: _ => null);

        var results = await adapter.ScanAsync(CancellationToken.None);

        var candidate = Assert.Single(results);
        Assert.False(candidate.CanPersist);
        Assert.Contains("无法读取", candidate.Warning);
    }

    [Fact]
    public async Task ComsolExeWindowsAreCaptured()
    {
        var adapter = new ComsolAdapter(
            windowProvider: () => new[]
            {
                new WindowSnapshot(1, 300, "comsol.exe", "model.mph - COMSOL Multiphysics")
            },
            commandLineProvider: _ => @"""D:\COMSOL63\comsol.exe"" -open D:\Work\old-version.mph");

        var results = await adapter.ScanAsync(CancellationToken.None);

        var candidate = Assert.Single(results);
        Assert.True(candidate.CanPersist);
        Assert.Equal(@"D:\Work\old-version.mph", candidate.Item.Path);
    }

    [Fact]
    public async Task NonComsolWindowsAreIgnored()
    {
        var adapter = new ComsolAdapter(
            windowProvider: () => new[]
            {
                new WindowSnapshot(1, 200, "WINWORD.EXE", "document - Word")
            },
            commandLineProvider: _ => @"""C:\COMSOL\ComsolUI.exe"" -open D:\x.mph");

        var results = await adapter.ScanAsync(CancellationToken.None);

        Assert.Empty(results);
    }

    private static ComsolAdapter CreateAdapter(string commandLine)
    {
        return new ComsolAdapter(
            windowProvider: () => new[] { ComsolWindow },
            commandLineProvider: _ => commandLine);
    }
}