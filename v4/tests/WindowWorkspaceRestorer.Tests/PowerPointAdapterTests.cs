using WindowWorkspaceRestorer.Services;

namespace WindowWorkspaceRestorer.Tests;

public sealed class PowerPointAdapterTests
{
    [Theory]
    [InlineData(null, "Presentation1")]
    [InlineData("", "Presentation1.pptx")]
    [InlineData(@"C:\Work", "Presentation1.pptx")]
    [InlineData("https://example.com", "https://example.com/slides.pptx")]
    public void UnsavedOrNonLocalNamesDoNotBecomeInventedPaths(string? directory, string? fullName)
        => Assert.Null(PowerPointAdapter.NormalizePresentationPath(directory, fullName));

    [Theory]
    [InlineData(@"C:\Work", @"C:\Work\演示 文稿.pptx")]
    [InlineData(@"\\server\share", @"\\server\share\slides.ppt")]
    public void SavedLocalAndNetworkPresentationsRetainFullPath(string directory, string fullName)
        => Assert.Equal(fullName, PowerPointAdapter.NormalizePresentationPath(directory, fullName));
}
