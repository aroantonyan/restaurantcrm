using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RestaurantCRM.Application.Common.Settings;
using RestaurantCRM.Domain.Entities;
using RestaurantCRM.Infrastructure.Services;
using Shouldly;

namespace RestaurantCRM.UnitTests.Auth;

public class JwtServiceTests
{
    private static readonly JwtSettings Settings = new()
    {
        Secret = "unit-test-secret-key-long-enough-for-hmac-sha256-0123456789",
        Issuer = "RestaurantCRM",
        Audience = "RestaurantCRM",
        AccessTokenMinutes = 60,
    };

    private readonly JwtService _service = new(Options.Create(Settings));

    private static readonly User TestUser = new()
    {
        RestaurantId = Guid.NewGuid(),
        Email = "aro@test.am",
    };

    [Fact]
    public void Token_carries_identity_role_and_permission_claims()
    {
        var token = _service.GenerateToken(TestUser, "Admin", ["ViewOrders", "CreateOrder"]);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        jwt.Claims.First(c => c.Type == "userId").Value.ShouldBe(TestUser.Id.ToString());
        jwt.Claims.First(c => c.Type == "restaurantId").Value.ShouldBe(TestUser.RestaurantId.ToString());
        jwt.Claims.First(c => c.Type == "role").Value.ShouldBe("Admin");
        jwt.Claims.First(c => c.Type == "permissions").Value.ShouldBe("ViewOrders,CreateOrder");
        jwt.Issuer.ShouldBe(Settings.Issuer);
        jwt.Audiences.ShouldContain(Settings.Audience);
    }

    [Fact]
    public void Token_expires_after_configured_lifetime()
    {
        var token = _service.GenerateToken(TestUser, "Admin", []);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        jwt.ValidTo.ShouldBe(DateTime.UtcNow.AddMinutes(Settings.AccessTokenMinutes), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Token_validates_against_the_signing_key()
    {
        var token = _service.GenerateToken(TestUser, "Admin", ["ViewOrders"]);

        var principal = new JwtSecurityTokenHandler()
            .ValidateToken(token, ValidationParameters(Settings.Secret), out _);

        principal.FindFirst("restaurantId")!.Value.ShouldBe(TestUser.RestaurantId.ToString());
    }

    [Fact]
    public void Token_signed_with_a_different_key_is_rejected()
    {
        var token = _service.GenerateToken(TestUser, "Admin", []);

        var ex = Record.Exception(() => new JwtSecurityTokenHandler()
            .ValidateToken(token, ValidationParameters("a-completely-different-secret-key-0123456789abcdef"), out _));

        ex.ShouldNotBeNull();
        ex.ShouldBeAssignableTo<SecurityTokenException>();
    }

    private static TokenValidationParameters ValidationParameters(string secret) => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = Settings.Issuer,
        ValidAudience = Settings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
    };
}
