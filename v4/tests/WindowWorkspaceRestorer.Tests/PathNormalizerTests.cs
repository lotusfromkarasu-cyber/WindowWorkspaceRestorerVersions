using WindowWorkspaceRestorer.Services;

namespace WindowWorkspaceRestorer.Tests;

public sealed class PathNormalizerTests
{
    [Fact]
    public void SamePathIsCaseInsensitive()
    {
        Assert.True(PathNormalizer.Same(@"C:\Work\Report.docx", @"c:\work\REPORT.docx"));
    }

    [Fact]
    public void TrailingSeparatorsAreIgnoredExceptForRoot()
    {
        Assert.True(PathNormalizer.Same(@"C:\Work\", @"C:\Work"));
        Assert.Equal(@"C:\", PathNormalizer.Normalize(@"C:\"));
    }

    [Fact]
    public void RelativePathsAreResolved()
    {
        var normalized = PathNormalizer.Normalize(@".\data\file.xlsx");
        Assert.True(Path.IsPathFullyQualified(normalized));
    }
}
