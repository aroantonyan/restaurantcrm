using System.Net.Http.Json;

namespace RestaurantCRM.IntegrationTests.Infrastructure;

public static class HttpHelpers
{
    public static async Task<T> GetAsync<T>(this TenantSession session, string url)
    {
        var response = await session.Client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(ApiFixture.Json))!;
    }

    public static async Task<T> PostAsync<T>(this TenantSession session, string url, object body)
    {
        var response = await session.Client.PostAsJsonAsync(url, body, ApiFixture.Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(ApiFixture.Json))!;
    }

    public static async Task<T> PutAsync<T>(this TenantSession session, string url, object body)
    {
        var response = await session.Client.PutAsJsonAsync(url, body, ApiFixture.Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(ApiFixture.Json))!;
    }

    public static async Task<T> PatchAsync<T>(this TenantSession session, string url, object body)
    {
        var response = await session.Client.PatchAsJsonAsync(url, body, ApiFixture.Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(ApiFixture.Json))!;
    }
}

/// <summary>The API's single error envelope: every non-2xx response is `{ "error": "..." }`.</summary>
public sealed record ApiError(string Error);
