using Microsoft.Data.SqlClient;
using System.Reflection;
using System.Text.RegularExpressions;
using Testcontainers.MsSql;
using Xunit;

namespace SQLTableExporter.Tests;

public partial class DatabaseFixture : IAsyncLifetime
{
    private MsSqlContainer _container = null!;

    public string ConnectionString { get; private set; } = null!;

    private const string DatabaseName = "SQLTableExporterTestDb";

    public async ValueTask InitializeAsync()
    {
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest").Build();
        await _container.StartAsync();

        await CreateDatabaseAsync(_container.GetConnectionString());
        await EnableSnapshotIsolationAsync(_container.GetConnectionString());
        ConnectionString = SetDatabase(_container.GetConnectionString(), DatabaseName);

        await ExecuteScriptAsync(ConnectionString, "pk_detector_fixtures.sql");
        await ExecuteScriptAsync(ConnectionString, "schema_script_fixtures.sql");
        await ExecuteScriptAsync(ConnectionString, "type_audit_test_data.sql");
        await ExecuteScriptAsync(ConnectionString, "type_audit_bulk_test_data.sql");
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private static async Task CreateDatabaseAsync(string masterConnectionString)
    {
        await using var conn = new SqlConnection(masterConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand($"CREATE DATABASE [{DatabaseName}]", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task EnableSnapshotIsolationAsync(string masterConnectionString)
    {
        // Required so tests exercising --snapshot-isolation can BEGIN TRANSACTION
        // ISOLATION LEVEL SNAPSHOT against the test DB.
        await using var conn = new SqlConnection(masterConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(
            $"ALTER DATABASE [{DatabaseName}] SET ALLOW_SNAPSHOT_ISOLATION ON", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static string SetDatabase(string connectionString, string database)
    {
        var builder = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = database };
        return builder.ConnectionString;
    }

    private static async Task ExecuteScriptAsync(string connectionString, string scriptName)
    {
        var sql = ReadEmbeddedScript(scriptName);
        var batches = SplitOnGo(sql);

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();

        foreach (var batch in batches)
        {
            var trimmed = batch.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            await using var cmd = new SqlCommand(trimmed, conn);
            cmd.CommandTimeout = 60;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static string ReadEmbeddedScript(string scriptName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith($".Scripts.{scriptName}", StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static List<string> SplitOnGo(string sql)
    {
        return [.. GoPattern().Split(sql)];
    }

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoPattern();
}

[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture>;
