using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using RestaurantCRM.API.Auth;
using RestaurantCRM.Domain.Enums;
using Shouldly;

namespace RestaurantCRM.UnitTests.Auth;

public class RequirePermissionAttributeTests
{
    [Fact]
    public void Allows_request_when_permission_is_present()
    {
        var context = FilterContext("ViewOrders,CreateOrder");

        new RequirePermissionAttribute(PermissionType.CreateOrder).OnAuthorization(context);

        context.Result.ShouldBeNull();
    }

    [Fact]
    public void Rejects_with_403_when_permission_is_missing()
    {
        var context = FilterContext("ViewOrders");

        new RequirePermissionAttribute(PermissionType.ManageStaff).OnAuthorization(context);

        var result = context.Result.ShouldBeOfType<ObjectResult>();
        result.StatusCode.ShouldBe(403);
    }

    [Fact]
    public void Rejects_when_the_permissions_claim_is_absent()
    {
        var context = FilterContext(permissionsClaim: null);

        new RequirePermissionAttribute(PermissionType.ViewOrders).OnAuthorization(context);

        context.Result.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe(403);
    }

    [Fact]
    public void Permission_match_is_exact_not_substring()
    {
        // "ViewOrdersArchive" must not satisfy a ViewOrders requirement.
        var context = FilterContext("ViewOrdersArchive");

        new RequirePermissionAttribute(PermissionType.ViewOrders).OnAuthorization(context);

        context.Result.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe(403);
    }

    private static AuthorizationFilterContext FilterContext(string? permissionsClaim)
    {
        var identity = new ClaimsIdentity("test");
        if (permissionsClaim is not null)
            identity.AddClaim(new Claim("permissions", permissionsClaim));

        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        return new AuthorizationFilterContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()), []);
    }
}
