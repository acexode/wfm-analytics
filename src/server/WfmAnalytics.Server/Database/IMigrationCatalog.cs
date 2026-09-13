namespace WfmAnalytics.Server.Database;

public interface IMigrationCatalog
{
    IReadOnlyList<MigrationDefinition> Migrations { get; }
}
