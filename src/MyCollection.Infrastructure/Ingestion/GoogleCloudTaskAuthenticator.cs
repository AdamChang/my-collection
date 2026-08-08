using Google.Apis.Auth;
using Microsoft.Extensions.Options;
using MyCollection.Application.Ingestion;

namespace MyCollection.Infrastructure.Ingestion;

public sealed class GoogleCloudTaskAuthenticator(IOptions<TasksOptions> options) : ICloudTaskAuthenticator
{
    private readonly TasksOptions _options = options.Value;

    public async Task<bool> IsAuthorizedAsync(string? authorizationHeader, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.Audience)
            || string.IsNullOrWhiteSpace(_options.ServiceAccountEmail)
            || authorizationHeader is null
            || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(
                authorizationHeader["Bearer ".Length..],
                new GoogleJsonWebSignature.ValidationSettings { Audience = [_options.Audience] });
            return payload.EmailVerified
                   && string.Equals(payload.Email, _options.ServiceAccountEmail, StringComparison.OrdinalIgnoreCase);
        }
        catch (InvalidJwtException)
        {
            return false;
        }
    }
}
