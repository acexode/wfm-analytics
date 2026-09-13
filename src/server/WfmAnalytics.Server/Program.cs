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
        ResolveSeedFixture(app.Configuration["SeedFixture"], app.Environment.ContentRootPath));
    Console.WriteLine("Synthetic development report seeded.");
    return;
}
await app.RunAsync();

static string ResolveSeedFixture(string? configuredPath, string contentRoot)
{
    if (!string.IsNullOrWhiteSpace(configuredPath))
    {
        return Path.GetFullPath(configuredPath);
    }

    for (var directory = new DirectoryInfo(contentRoot); directory is not null; directory = directory.Parent)
    {
        var candidate = Path.Combine(directory.FullName, "fixtures", "daily-report.json");
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    return Path.GetFullPath(Path.Combine("fixtures", "daily-report.json"));
}

public partial class Program;
