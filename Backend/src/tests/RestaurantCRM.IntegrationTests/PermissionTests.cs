using System.Net;
using System.Net.Http.Json;
using RestaurantCRM.Application.Auth;
using RestaurantCRM.Application.Staff;
using RestaurantCRM.Application.Tables;
using RestaurantCRM.IntegrationTests.Infrastructure;
using Shouldly;

namespace RestaurantCRM.IntegrationTests;

[Collection("Api")]
public class PermissionTests(ApiFixture fx)
{
    [Fact]
    public async Task Request_without_a_token_returns_401()
    {
        var response = await fx.CreateClient().GetAsync("/api/orders");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Tampered_token_signature_returns_401()
    {
        var session = await fx.RegisterTenantAsync();
        var tampered = session.Auth.Token[..^2] + (session.Auth.Token[^1] == 'a' ? "bb" : "aa");

        var client = fx.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", tampered);

        (await client.GetAsync("/api/menu")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Role_permissions_are_enforced_server_side()
    {
        var admin = await fx.RegisterTenantAsync();

        // Hire a cook: role grants ViewOrders/EditOrder/ViewMenu — no money, no table management.
        var roles = await admin.GetAsync<List<RoleDto>>("/api/staff/roles");
        var cookRole = roles.First(r => r.Name == "Cook");
        var email = $"cook-{Guid.NewGuid():N}@test.local";
        await admin.PostAsync<StaffMemberDto>("/api/staff",
            new CreateStaffRequest("Cook", "Test", "Test", email, "temp-secret", cookRole.Id, null, null));

        var login = await fx.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequest(email, "temp-secret"), ApiFixture.Json);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cookAuth = (await login.Content.ReadFromJsonAsync<AuthResponse>(ApiFixture.Json))!;

        var cook = fx.CreateClient();
        cook.DefaultRequestHeaders.Authorization = new("Bearer", cookAuth.Token);

        (await cook.GetAsync("/api/menu")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await cook.GetAsync($"/api/cash-register/summary?{TimeWindow()}"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await cook.PostAsJsonAsync("/api/tables", new CreateTableRequest(9), ApiFixture.Json))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static string TimeWindow() =>
        $"from={Uri.EscapeDataString(DateTime.UtcNow.AddDays(-1).ToString("O"))}" +
        $"&to={Uri.EscapeDataString(DateTime.UtcNow.AddDays(1).ToString("O"))}";
}
