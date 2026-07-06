using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using RestaurantCRM.Application.Auth;
using Testcontainers.PostgreSql;

namespace RestaurantCRM.IntegrationTests.Infrastructure;

/// <summary>
/// One PostgreSQL 17 container + one in-process API host, shared by every test
/// class in the "Api" collection (containers are expensive; booting one per test
/// would multiply runtime ~50x). Isolation comes from the domain itself: each
/// test registers its own restaurant, and the tenant query filters guarantee
/// tenants can't see each other — the exact mechanism production relies on.
/// </summary>
public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Mirrors the API's JSON contract: camelCase + enums as strings.
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Testcontainers maps a random host port, so the connection string only
        // exists at runtime — inject it over the appsettings value.
        builder.UseSetting("ConnectionStrings:DefaultConnection", _postgres.GetConnectionString());
    }

    public async ValueTask InitializeAsync() => await _postgres.StartAsync();

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>Registers a fresh restaurant + admin and returns an authenticated client.</summary>
    public async Task<TenantSession> RegisterTenantAsync()
    {
        var email = $"owner-{Guid.NewGuid():N}@test.local";
        const string password = "secret123";
        var request = new RegisterRequest(
            "Aro", "Owner", "Test", email, password, $"Rest-{Guid.NewGuid().ToString("N")[..8]}");

        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", request, Json);
        response.EnsureSuccessStatusCode();

        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return new TenantSession(client, auth, email, password);
    }
}

/// <summary>An authenticated tenant: HTTP client with bearer token + the register response.</summary>
public sealed record TenantSession(HttpClient Client, AuthResponse Auth, string Email, string Password);

[CollectionDefinition("Api")]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;
