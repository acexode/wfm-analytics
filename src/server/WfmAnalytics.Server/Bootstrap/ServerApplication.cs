using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using WfmAnalytics.Server.Database;
using WfmAnalytics.Server.Identity;
using WfmAnalytics.Server.Modules.Analytics;
using WfmAnalytics.Server.Modules.Demo;
using WfmAnalytics.Server.Modules.Ingestion;

namespace WfmAnalytics.Server.Bootstrap;

public static class ServerApplication
{
    public const string DevelopmentAuthenticationScheme = "DevelopmentHeaders";
    public const string DemoReadPolicy = "DemoRead";

    public static WebApplication Build(
        string[]? args = null,
        string? environmentName = null,
        string? urls = null)
    {
        var options = new WebApplicationOptions
        {
            Args = args ?? [],
            EnvironmentName = environmentName,
            ApplicationName = typeof(ServerApplication).Assembly.GetName().Name
        };

        var builder = WebApplication.CreateBuilder(options);
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        if (!string.IsNullOrWhiteSpace(urls))
        {
            builder.WebHost.UseUrls(urls);
        }

        builder.Services
            .AddOptions<DatabaseOptions>()
            .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddSingleton<IMigrationCatalog, EmbeddedMigrationCatalog>();
        builder.Services.AddSingleton<PostgresDatabase>();
        builder.Services.AddSingleton<ActivityDailyAggregator>();

        builder.Services
            .AddAuthentication(DevelopmentAuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, DevelopmentHeaderAuthenticationHandler>(
                DevelopmentAuthenticationScheme,
                _ => { });

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(DemoReadPolicy, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(DevelopmentIdentity.ScopeClaimType, DemoEndpoints.RequiredScope);
            });

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();

        MapHealthEndpoints(app);
        app.MapDemoEndpoints();
        app.MapDailyReportEndpoints();
        app.MapActivityIngestionEndpoints();

        return app;
    }

    private static void MapHealthEndpoints(WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new
        {
            status = "healthy",
            service = "wfm-analytics-server"
        })).AllowAnonymous();

        app.MapGet("/health/ready", async (PostgresDatabase database, CancellationToken token) =>
        {
            var ready = await database.IsReadyAsync(token);
            return Results.Json(new { status = ready ? "ready" : "not_ready" }, statusCode: ready ? 200 : 503);
        }).AllowAnonymous();
    }
}
