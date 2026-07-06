using System.Text.Json.Serialization;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using RestaurantCRM.API;
using RestaurantCRM.Application.Auth;
using Microsoft.EntityFrameworkCore;
using RestaurantCRM.Infrastructure;
using RestaurantCRM.Infrastructure.Persistence;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting RestaurantCRM API");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, services, config) =>
        config
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7));

    const string CorsPolicy = "Frontend";

    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddProblemDetails();

    builder.Services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();
    builder.Services.AddFluentValidationAutoValidation();

    builder.Services.AddOpenApi();
    builder.Services.AddControllers()
        .AddJsonOptions(o =>
            o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

    builder.Services.AddSignalR();

    builder.Services.AddHealthChecks();

    // Must come after AddControllers — it overrides the default ValidationProblemDetails factory
    builder.Services.Configure<ApiBehaviorOptions>(options =>
    {
        options.InvalidModelStateResponseFactory = ctx =>
        {
            var error = ctx.ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .FirstOrDefault() ?? "Validation failed.";
            return new BadRequestObjectResult(new { error });
        };
    });

    builder.Services.AddInfrastructure(builder.Configuration);

    builder.Services.AddCors(options =>
    {
        options.AddPolicy(CorsPolicy, policy =>
        {
            policy
                .SetIsOriginAllowed(origin =>
                {
                    if (string.IsNullOrEmpty(origin)) return false;
                    var uri = new Uri(origin);
                    return uri.Host is "localhost" or "127.0.0.1"
                           || uri.Host.EndsWith(".ngrok-free.app")
                           || uri.Host.EndsWith(".ngrok.io")
                           || uri.Host.EndsWith(".ngrok.app");
                })
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
    });

    var app = builder.Build();

    // Apply any pending EF Core migrations on startup.
    // Safe for single-replica deployments; avoids the manual `dotnet ef database update` step in Docker.
    using (var scope = app.Services.CreateScope())
    {
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        ctx.Database.Migrate();

        // Idempotent: seeds a realistic demo restaurant only if it isn't there yet.
        var seedLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DemoDataSeeder");
        RestaurantCRM.Infrastructure.Persistence.DemoDataSeeder.SeedAsync(ctx, seedLogger).GetAwaiter().GetResult();
    }

    if (app.Environment.IsDevelopment())
        app.MapOpenApi();

    app.UseExceptionHandler();
    app.UseCors(CorsPolicy);

    // TLS is terminated upstream (Caddy in prod, Vite/ngrok in dev) — the API
    // only ever speaks plain HTTP on :8080. Forcing a redirect here is a no-op
    // that just logs a warning, so only enable it for local HTTPS dev runs.
    if (app.Environment.IsDevelopment())
        app.UseHttpsRedirection();

    app.UseSerilogRequestLogging();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHub<RestaurantCRM.Infrastructure.Realtime.OrderHub>("/hubs/orders");

    // Liveness probe for Docker/compose healthchecks. Reachable only inside the
    // container network (nginx proxies just /api and /hubs), so it leaks nothing.
    // Returns 200 once Kestrel is serving — i.e. after migrations have run.
    app.MapHealthChecks("/health");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Top-level statements compile to an internal Program class; integration tests
// need it public to host the app via WebApplicationFactory<Program>.
public partial class Program;
