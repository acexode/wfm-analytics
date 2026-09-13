using Npgsql;

namespace WfmAnalytics.Server.Database;

public sealed class PostgresDatabase(IConfiguration configuration, IMigrationCatalog catalog)
{
    public NpgsqlConnection CreateConnection(string name = "Primary") => new(
        configuration.GetConnectionString(name)
        ?? throw new InvalidOperationException($"ConnectionStrings:{name} is required."));

    public async Task ApplyMigrationsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection("Migration");
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(87210402)", connection, transaction))
            await advisory.ExecuteNonQueryAsync(cancellationToken);
        await using (var bootstrap = new NpgsqlCommand(catalog.Migrations[0].Sql, connection, transaction))
            await bootstrap.ExecuteNonQueryAsync(cancellationToken);
        var applied = await ReadLedgerAsync(connection, transaction, cancellationToken);
        ValidateLedger(applied, requireComplete: false);
        foreach (var migration in catalog.Migrations.Where(m => !applied.ContainsKey(m.Version)))
        {
            // The ledger bootstrap is idempotent and part of this same transaction.
            await using var sql = new NpgsqlCommand(migration.Sql, connection, transaction);
            await sql.ExecuteNonQueryAsync(cancellationToken);
            await using var record = new NpgsqlCommand("INSERT INTO platform.schema_migrations(version,name,sha256) VALUES ($1,$2,$3)", connection, transaction);
            record.Parameters.AddWithValue(migration.Version);
            record.Parameters.AddWithValue(migration.Name);
            record.Parameters.AddWithValue(migration.Sha256);
            await record.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            await using var connection = CreateConnection();
            await connection.OpenAsync(timeout.Token);
            ValidateLedger(await ReadLedgerAsync(connection, null, timeout.Token), requireComplete: true);
            return true;
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or ArgumentException or OperationCanceledException)
        {
            return false;
        }
    }

    private static async Task<Dictionary<long, string>> ReadLedgerAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("SELECT version,sha256 FROM platform.schema_migrations ORDER BY version", connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(token);
        var applied = new Dictionary<long, string>();
        while (await reader.ReadAsync(token)) applied.Add(reader.GetInt64(0), reader.GetString(1));
        return applied;
    }

    private void ValidateLedger(Dictionary<long, string> applied, bool requireComplete)
    {
        if (applied.Any(row => !catalog.Migrations.Any(m => m.Version == row.Key && m.Sha256 == row.Value))
            || (requireComplete && applied.Count != catalog.Migrations.Count))
            throw new InvalidOperationException("Database migration ledger does not match this application.");
    }
}
