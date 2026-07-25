using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using MongoDB.Bson;
using MyCollection.Api;
using MyCollection.Domain.Exceptions;

namespace MyCollection.Tests.Unit;

public class HttpUserContextTests
{
    private static HttpUserContext CreateSut(ClaimsPrincipal? principal)
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = principal ?? new ClaimsPrincipal(new ClaimsIdentity()) }
        };
        return new HttpUserContext(accessor);
    }

    private static ClaimsPrincipal Authenticated(string sub) =>
        new(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, sub)], "Bearer"));

    [Fact]
    public void Resolves_UserId_from_sub_claim()
    {
        var sut = CreateSut(Authenticated("507f1f77bcf86cd799439011"));

        sut.IsAuthenticated.Should().BeTrue();
        sut.UserId.Should().Be(ObjectId.Parse("507f1f77bcf86cd799439011"));
    }

    [Fact]
    public void Anonymous_request_is_not_authenticated()
    {
        CreateSut(null).IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void Accessing_UserId_when_anonymous_throws_ForbiddenException()
    {
        var sut = CreateSut(null);

        var act = () => sut.UserId;

        act.Should().Throw<ForbiddenException>();
    }

    [Fact]
    public void Malformed_sub_claim_throws_ForbiddenException()
    {
        var sut = CreateSut(Authenticated("not-an-objectid"));

        var act = () => sut.UserId;

        act.Should().Throw<ForbiddenException>();
    }
}
