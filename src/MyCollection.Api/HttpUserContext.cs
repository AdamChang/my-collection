using System.IdentityModel.Tokens.Jwt;
using MongoDB.Bson;
using MyCollection.Application.Common;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Api;

public sealed class HttpUserContext(IHttpContextAccessor httpContextAccessor) : IUserContext
{
    public bool IsAuthenticated =>
        httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public ObjectId UserId
    {
        get
        {
            var sub = httpContextAccessor.HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

            if (string.IsNullOrEmpty(sub) || !ObjectId.TryParse(sub, out var id))
            {
                throw new ForbiddenException("Request is not associated with an authenticated user.");
            }

            return id;
        }
    }
}
