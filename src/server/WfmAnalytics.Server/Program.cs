using WfmAnalytics.Server.Bootstrap;
using WfmAnalytics.Server.Database;

await using var app = ServerApplication.Build(args.Where(a => !a.StartsWith("--migrate", StringComparison.Ordinal) && a != "--seed-development").ToArray());
if (args.Contains("--migrate", StringComparer.Ordinal))
{
    await app.Services.GetRequiredService<PostgresDatabase>().ApplyMigrationsAsync();
    Console.WriteLine("PostgreSQL migrations applied and verified.");
    return;
}
if (args.Contains("--seed-development", StringComparer.Ordinal))
{
    await DevelopmentSeeder.SeedAsync(app.Services.GetRequiredService<PostgresDatabase>(), app.Environment,
        app.Configuration["SeedFixture"] ?? "fixtures/daily-report.json");
    Console.WriteLine("Synthetic development report seeded.");
    return;
}
await app.RunAsync();

public partial class Program;
