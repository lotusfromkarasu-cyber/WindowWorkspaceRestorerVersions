namespace WindowWorkspaceRestorer.Interop;

internal static class ShellInterop
{
    internal static string? TryGetFileSystemPath(string? locationUrl)
    {
        if (string.IsNullOrWhiteSpace(locationUrl))
        {
            return null;
        }

        if (Uri.TryCreate(locationUrl, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            return uri.LocalPath;
        }

        if (locationUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            var escapedPath = locationUrl[7..].Replace('/', '\\');
            return Uri.UnescapeDataString(escapedPath);
        }

        return locationUrl.IndexOf(':') >= 0
            ? locationUrl
            : null;
    }

    internal static string ToFileUri(string path)
    {
        return new Uri(path, UriKind.Absolute).AbsoluteUri;
    }
}
