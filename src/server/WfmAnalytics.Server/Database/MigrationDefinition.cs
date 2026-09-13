namespace WfmAnalytics.Server.Database;

public sealed record MigrationDefinition(
    long Version,
    string Name,
    string Sql,
    string Sha256);
