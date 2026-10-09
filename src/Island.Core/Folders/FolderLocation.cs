namespace Island.Core;

/// <summary>Turns the address an Explorer window reports ("file:///C:/...") into a folder path held in memory.</summary>
public static class FolderLocation
{
    /// <summary>
    /// A file-system path, a network path, or a shell parsing name ("::{GUID}"); null when the address is not a
    /// "file:" address or cannot be read.
    /// </summary>
    public static string? FromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            var afterScheme = Uri.UnescapeDataString(url["file:".Length..].TrimStart('/'));
            return afterScheme.StartsWith("::{", StringComparison.Ordinal) ? afterScheme : new Uri(url).LocalPath;
        }
        catch (UriFormatException)
        {
            return null;
        }
    }
}
