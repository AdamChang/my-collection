namespace MyCollection.Infrastructure.Storage;

internal static class StoragePath
{
    public static string Validate(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Path must not be empty.", nameof(relativePath));
        }

        if (relativePath.StartsWith('/') || relativePath.Contains('\0') ||
            relativePath.Contains('\\') || relativePath.Contains(':'))
        {
            throw new ArgumentException("Path must be a portable relative path.", nameof(relativePath));
        }

        var segments = relativePath.Split('/');
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
        {
            throw new ArgumentException("Path contains an invalid segment.", nameof(relativePath));
        }

        return string.Join('/', segments);
    }
}
