using System.Net;
using System.Net.Http.Json;
using RestaurantCRM.Application.Auth;
using RestaurantCRM.IntegrationTests.Infrastructure;
using Shouldly;

namespace RestaurantCRM.IntegrationTests;

[Collection("Api")]
public class AuthFlowTests(ApiFixture fx)
{
    [Fact]
    public async Task Register_creates_a_tenant_with_full_admin_permissions()
    {
        var session = await fx.RegisterTenantAsync();

        session.Auth.Token.ShouldNotBeNullOrEmpty();
        session.Auth.RefreshToken.ShouldNotBeNullOrEmpty();
        session.Auth.RoleName.ShouldBe("Admin");
        session.Auth.Currency.ShouldBe("AMD");
        session.Auth.Permissions.ShouldContain("ManageStaff");
        session.Auth.Permissions.Count.ShouldBeGreaterThanOrEqualTo(20);
    }

    [Fact]
    public async Task Register_with_a_taken_email_returns_409()
    {
        var session = await fx.RegisterTenantAsync();

        var response = await fx.CreateClient().PostAsJsonAsync("/api/auth/register",
            new RegisterRequest("A", "B", "C", session.Email, "secret123", "Another"), ApiFixture.Json);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ApiError>(ApiFixture.Json))!
            .Error.ShouldContain("already registered");
    }

    [Fact]
    public async Task Login_failures_do_not_reveal_whether_the_email_exists()
    {
        var session = await fx.RegisterTenantAsync();
        var client = fx.CreateClient();

        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(session.Email, "wrong-pass"), ApiFixture.Json);
        var unknownEmail = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest($"ghost-{Guid.NewGuid():N}@test.local", "wrong-pass"), ApiFixture.Json);

        wrongPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unknownEmail.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        // Identical bodies — an attacker can't enumerate registered emails.
        (await wrongPassword.Content.ReadAsStringAsync())
            .ShouldBe(await unknownEmail.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Refresh_rotates_the_refresh_token()
    {
        var session = await fx.RegisterTenantAsync();

        var response = await RefreshAsync(session.Auth.RefreshToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(ApiFixture.Json))!;
        auth.Token.ShouldNotBeNullOrEmpty();
        auth.RefreshToken.ShouldNotBe(session.Auth.RefreshToken);
    }

    [Fact]
    public async Task Replaying_a_rotated_refresh_token_revokes_the_whole_family()
    {
        var session = await fx.RegisterTenantAsync();

        // Legitimate rotation: the original token is exchanged for a successor.
        var first = await RefreshAsync(session.Auth.RefreshToken);
        var rotated = (await first.Content.ReadFromJsonAsync<AuthResponse>(ApiFixture.Json))!;

        // Replaying the spent token is the theft signal → rejected...
        (await RefreshAsync(session.Auth.RefreshToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // ...and the successor must die with it, so a thief who raced the victim loses too.
        (await RefreshAsync(rotated.RefreshToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var session = await fx.RegisterTenantAsync();

        var logout = await fx.CreateClient().PostAsJsonAsync("/api/auth/logout",
            new RefreshTokenRequest(session.Auth.RefreshToken), ApiFixture.Json);

        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await RefreshAsync(session.Auth.RefreshToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Changed_password_takes_effect_immediately()
    {
        var session = await fx.RegisterTenantAsync();

        var change = await session.Client.PostAsJsonAsync("/api/auth/change-password",
            new ChangePasswordRequest(session.Password, "brand-new-secret"), ApiFixture.Json);
        change.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var client = fx.CreateClient();
        var oldLogin = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(session.Email, session.Password), ApiFixture.Json);
        var newLogin = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest(session.Email, "brand-new-secret"), ApiFixture.Json);

        oldLogin.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        newLogin.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        fx.CreateClient().PostAsJsonAsync("/api/auth/refresh",
            new RefreshTokenRequest(refreshToken), ApiFixture.Json);
}
