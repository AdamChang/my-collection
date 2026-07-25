namespace MyCollection.Application.Auth;

public record UserDto(string Id, string Email, string DisplayName);

public record AuthResponse(string AccessToken, string RefreshToken, DateTime ExpiresAt, UserDto User);
