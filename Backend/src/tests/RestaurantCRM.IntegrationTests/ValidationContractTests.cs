using System.Net;
using System.Net.Http.Json;
using RestaurantCRM.Application.Auth;
using RestaurantCRM.Application.Orders;
using RestaurantCRM.IntegrationTests.Infrastructure;
using Shouldly;

namespace RestaurantCRM.IntegrationTests;

/// <summary>
/// Verifies the API-wide error contract the frontend relies on:
/// invalid input → 400 with a `{ "error": "..." }` body (never a raw
/// ProblemDetails or a 500), produced by FluentValidation auto-validation
/// plus the custom InvalidModelStateResponseFactory.
/// </summary>
[Collection("Api")]
public class ValidationContractTests(ApiFixture fx)
{
    [Fact]
    public async Task Invalid_payload_returns_400_with_the_error_envelope()
    {
        var response = await fx.CreateClient().PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("Aro", "Owner", "Test", "not-an-email", "secret123", "Rest"), ApiFixture.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ApiError>(ApiFixture.Json))!
            .Error.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Order_without_items_is_rejected_before_reaching_the_service()
    {
        var session = await fx.RegisterTenantAsync();

        var response = await session.Client.PostAsJsonAsync("/api/orders",
            new CreateOrderRequest(Guid.NewGuid(), []), ApiFixture.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ApiError>(ApiFixture.Json))!
            .Error.ShouldContain("at least one item");
    }

    [Fact]
    public async Task Unknown_payment_method_is_rejected_with_400()
    {
        var session = await fx.RegisterTenantAsync();

        var response = await session.Client.PatchAsJsonAsync($"/api/orders/{Guid.NewGuid()}/status",
            new UpdateOrderStatusRequest("Paid", "Bitcoin"), ApiFixture.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
