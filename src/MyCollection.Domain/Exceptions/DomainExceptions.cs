namespace MyCollection.Domain.Exceptions;

/// <summary>找不到資源。對應 HTTP 404。</summary>
public sealed class NotFoundException(string resource, object key)
    : Exception($"{resource} '{key}' was not found.")
{
    public string Resource { get; } = resource;
    public object Key { get; } = key;
}

/// <summary>ownerId 不符或無權限。對應 HTTP 403。</summary>
public sealed class ForbiddenException(string message = "Access to the requested resource is denied.")
    : Exception(message);

/// <summary>唯一性衝突（email、share slug）。對應 HTTP 409。</summary>
public sealed class ConflictException(string message) : Exception(message);

/// <summary>外部 Provider 呼叫失敗。對應 HTTP 502。</summary>
public sealed class ProviderException(string providerKey, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string ProviderKey { get; } = providerKey;
}
