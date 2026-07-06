using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using RestaurantCRM.Infrastructure.Services;
using Shouldly;

namespace RestaurantCRM.UnitTests.Auth;

public class TenantContextTests
{
    [Fact]
    public void Reads_restaurant_and_user_ids_from_jwt_claims()
    {
        var restaurantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var tenant = new TenantContext(AccessorWith(new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("restaurantId", restaurantId.ToString()),
            new Claim("userId", userId.ToString()),
        ], "test"))));

        tenant.RestaurantId.ShouldBe(restaurantId);
        tenant.UserId.ShouldBe(userId);
        tenant.HasTenant.ShouldBeTrue();
    }

    [Fact]
    public void No_http_context_yields_an_empty_tenant()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);

        var tenant = new TenantContext(accessor);

        tenant.RestaurantId.ShouldBe(Guid.Empty);
        tenant.HasTenant.ShouldBeFalse();
    }

    [Fact]
    public void Unauthenticated_request_without_claims_yields_an_empty_tenant()
    {
        var tenant = new TenantContext(AccessorWith(new ClaimsPrincipal(new ClaimsIdentity())));

        tenant.RestaurantId.ShouldBe(Guid.Empty);
        tenant.UserId.ShouldBe(Guid.Empty);
        tenant.HasTenant.ShouldBeFalse();
    }

    private static IHttpContextAccessor AccessorWith(ClaimsPrincipal user)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(new DefaultHttpContext { User = user });
        return accessor;
    }
}
