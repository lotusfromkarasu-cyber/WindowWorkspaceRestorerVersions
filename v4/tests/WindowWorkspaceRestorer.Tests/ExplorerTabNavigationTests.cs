using WindowWorkspaceRestorer.Services;

namespace WindowWorkspaceRestorer.Tests;

public sealed class ExplorerTabNavigationTests
{
    [Fact]
    public void ExistingHomeAndChangingPathsAreNeverNavigated()
    {
        var home = new Tab(1, null);
        var existing = new Tab(2, @"C:\old");
        var added = new Tab(3, @"C:\old"); // New tab can initially duplicate an old folder.
        var polls = 0;
        var result = ExplorerTabNavigation.Open(new IExplorerTab[] { home, existing }, () => true,
            () =>
            {
                polls++;
                existing.CurrentPath = polls % 2 == 0 ? null : @"C:\changed";
                if (polls > 4 && added.Navigations > 0) added.CurrentPath = @"C:\target";
                return polls < 3 ? new IExplorerTab[] { home, existing } : new IExplorerTab[] { home, existing, added };
            }, @"C:\target", TimeSpan.FromSeconds(3), default);
        Assert.True(result);
        Assert.Equal(0, home.Navigations);
        Assert.Equal(0, existing.Navigations);
        Assert.Equal(1, added.Navigations);
        Assert.True(polls >= 7); // Did not return merely because Navigate was submitted.
    }

    [Fact]
    public void TabCreationWithoutNewIdentityDoesNotOverwriteExistingTab()
    {
        var home = new Tab(1, null);
        Assert.False(ExplorerTabNavigation.Open(new[] { home }, () => true,
            () => new[] { home }, @"C:\target", TimeSpan.FromMilliseconds(220), default));
        Assert.Equal(0, home.Navigations);
    }

    [Fact]
    public void FailedNavigationIsNotReportedAsSuccess()
    {
        var existing = new Tab(1, @"C:\old");
        var added = new Tab(2, null);
        Assert.False(ExplorerTabNavigation.Open(new[] { existing }, () => true,
            () => new[] { existing, added }, @"C:\target", TimeSpan.FromMilliseconds(220), default));
        Assert.Equal(1, added.Navigations);
    }

    [Fact]
    public void AmbiguousNewTabsAreNotNavigated()
    {
        var existing = new Tab(1, @"C:\old");
        var first = new Tab(2, null);
        var second = new Tab(3, null);
        Assert.False(ExplorerTabNavigation.Open(new[] { existing }, () => true,
            () => new[] { existing, first, second }, @"C:\target", TimeSpan.FromSeconds(1), default));
        Assert.Equal(0, first.Navigations + second.Navigations);
    }

    [Fact]
    public void CancellationStopsBeforeCreatingTab()
    {
        var invoked = false;
        Assert.Throws<OperationCanceledException>(() => ExplorerTabNavigation.Open(
            new[] { new Tab(1, null) }, () => invoked = true, () => Array.Empty<IExplorerTab>(),
            @"C:\target", TimeSpan.FromSeconds(1), new CancellationToken(true)));
        Assert.False(invoked);
    }

    private sealed class Tab(nint identity, string? path) : IExplorerTab
    {
        public nint Identity => identity;
        public string? CurrentPath { get; set; } = path;
        public string? Path => CurrentPath;
        public bool Busy => false;
        public int Navigations { get; private set; }
        public void Navigate(string value) => Navigations++;
        public void Dispose() { }
    }
}
