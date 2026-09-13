using System.ComponentModel.DataAnnotations;

namespace WfmAnalytics.Server.Database;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    [Required]
    [RegularExpression("^PostgreSQL$", ErrorMessage = "Only PostgreSQL is approved for the first release.")]
    public string Provider { get; init; } = "PostgreSQL";

    [Required]
    public string ConnectionStringName { get; init; } = "Primary";

    public bool ApplyMigrationsOnStartup { get; init; }
}
