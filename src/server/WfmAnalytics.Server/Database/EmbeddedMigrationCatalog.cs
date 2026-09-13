using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace WfmAnalytics.Server.Database;

public sealed partial class EmbeddedMigrationCatalog : IMigrationCatalog
{
    public EmbeddedMigrationCatalog()
        : this(typeof(EmbeddedMigrationCatalog).Assembly)
    {
    }

    internal EmbeddedMigrationCatalog(Assembly assembly)
    {
        Migrations = Load(assembly);
    }

    public IReadOnlyList<MigrationDefinition> Migrations { get; }

    private static IReadOnlyList<MigrationDefinition> Load(Assembly assembly)
    {
        var migrations = assembly.GetManifestResourceNames()
            .Select(resourceName => (ResourceName: resourceName, Match: MigrationResourcePattern().Match(resourceName)))
            .Where(item => item.Match.Success)
            .Select(item => Read(assembly, item.ResourceName, item.Match))
            .OrderBy(migration => migration.Version)
            .ToArray();

        if (migrations.Length == 0)
        {
            throw new InvalidOperationException("No embedded PostgreSQL migrations were found.");
        }

        var duplicateVersion = migrations
            .GroupBy(migration => migration.Version)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateVersion is not null)
        {
            throw new InvalidOperationException($"Duplicate migration version {duplicateVersion.Key}.");
        }

        return migrations;
    }

    private static MigrationDefinition Read(Assembly assembly, string resourceName, Match match)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Migration resource '{resourceName}' could not be read.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var sql = reader.ReadToEnd();
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new InvalidOperationException($"Migration resource '{resourceName}' is empty.");
        }

        var version = long.Parse(match.Groups["version"].Value, CultureInfo.InvariantCulture);
        var name = match.Groups["name"].Value;
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql))).ToLowerInvariant();
        return new MigrationDefinition(version, name, sql, checksum);
    }

    [GeneratedRegex(@"\.Database\.Migrations\.(?<version>\d{4})_(?<name>[A-Za-z0-9_]+)\.sql$", RegexOptions.CultureInvariant)]
    private static partial Regex MigrationResourcePattern();
}
