using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantCRM.API;
using Shouldly;

namespace RestaurantCRM.UnitTests.Api;

public class GlobalExceptionHandlerTests
{
    public static TheoryData<Exception, int> Mappings => new()
    {
        { new KeyNotFoundException("Order not found."), StatusCodes.Status404NotFound },
        { new InvalidOperationException("Table is busy."), StatusCodes.Status409Conflict },
        { new UnauthorizedAccessException("Invalid email or password."), StatusCodes.Status401Unauthorized },
        { new ArgumentException("Invalid payment method."), StatusCodes.Status400BadRequest },
        { new Exception("boom"), StatusCodes.Status500InternalServerError },
    };

    [Theory]
    [MemberData(nameof(Mappings))]
    public async Task Maps_exception_type_to_status_code(Exception exception, int expectedStatus)
    {
        var (status, _) = await HandleAsync(exception);

        status.ShouldBe(expectedStatus);
    }

    [Fact]
    public async Task Known_exceptions_surface_their_message_in_the_error_envelope()
    {
        var (_, body) = await HandleAsync(new KeyNotFoundException("Order not found."));

        body.GetProperty("error").GetString().ShouldBe("Order not found.");
    }

    [Fact]
    public async Task Unexpected_exceptions_never_leak_internal_details()
    {
        var (_, body) = await HandleAsync(new Exception("connection string = secret"));

        var error = body.GetProperty("error").GetString()!;
        error.ShouldBe("An unexpected error occurred.");
        error.ShouldNotContain("secret");
    }

    [Fact]
    public async Task Concurrency_conflicts_map_to_409_with_a_retry_hint()
    {
        var (status, body) = await HandleAsync(new DbUpdateConcurrencyException("row version mismatch"));

        status.ShouldBe(StatusCodes.Status409Conflict);
        body.GetProperty("error").GetString().ShouldContain("refresh and try again");
    }

    private static async Task<(int Status, JsonElement Body)> HandleAsync(Exception exception)
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider(),
        };
        context.Response.Body = new MemoryStream();

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        handled.ShouldBeTrue();
        context.Response.Body.Position = 0;
        var body = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, body.RootElement);
    }
}
