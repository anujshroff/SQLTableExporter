using Microsoft.Data.SqlClient;
using Xunit;

namespace SQLTableExporter.Tests.Integration;

[Collection("Database")]
public class SchemaScriptGeneratorTests(DatabaseFixture db) : IDisposable
{
    private readonly DatabaseFixture _db = db;
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), $"sqlexporter-schema-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Generated_script_for_child_table_includes_columns_pk_fk_indexes_and_check()
    {
        var generator = new SchemaScriptGenerator(_db.ConnectionString);
        string script = await generator.GenerateTableSchemaScriptAsync("dbo", "SchemaChild");

        Assert.Contains("CREATE TABLE [dbo].[SchemaChild]", script);
        Assert.Contains("[ChildId]", script);
        Assert.Contains("IDENTITY", script);
        Assert.Contains("PRIMARY KEY", script);
        Assert.Contains("FOREIGN KEY", script);
        Assert.Contains("[dbo].[SchemaParent]", script);
        Assert.Contains("ON DELETE CASCADE", script);
        Assert.Contains("UX_SchemaChild_ParentOrder", script);
        Assert.Contains("IX_SchemaChild_ChildName", script);
        Assert.Contains("[ChildName] DESC", script);
        Assert.Contains("CK_SchemaChild_Amount", script);
    }

    [Fact]
    public async Task Generated_script_for_parent_table_includes_default_constraint()
    {
        var generator = new SchemaScriptGenerator(_db.ConnectionString);
        string script = await generator.GenerateTableSchemaScriptAsync("dbo", "SchemaParent");

        Assert.Contains("CREATE TABLE [dbo].[SchemaParent]", script);
        Assert.Contains("DEFAULT", script);
        Assert.Contains("sysutcdatetime", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveSchemaScriptAsync_writes_file_with_expected_name()
    {
        Directory.CreateDirectory(_tempDir);
        var generator = new SchemaScriptGenerator(_db.ConnectionString);

        string filePath = await generator.SaveSchemaScriptAsync(
            "dbo", "SchemaParent", _tempDir, filePrefix: "myprefix");

        Assert.True(File.Exists(filePath));
        Assert.Equal(Path.Combine(_tempDir, "myprefix_schema.sql"), filePath);
        string content = await File.ReadAllTextAsync(filePath, TestContext.Current.CancellationToken);
        Assert.Contains("CREATE TABLE [dbo].[SchemaParent]", content);
    }

    [Fact]
    public async Task Generated_script_executes_against_a_fresh_database()
    {
        var ct = TestContext.Current.CancellationToken;
        // Validate the generator's output is syntactically valid T-SQL by
        // running it in a throwaway database and re-introspecting.
        var generator = new SchemaScriptGenerator(_db.ConnectionString);
        string script = await generator.GenerateTableSchemaScriptAsync("dbo", "SchemaParent");

        var builder = new SqlConnectionStringBuilder(_db.ConnectionString);
        string cloneDb = $"SchemaClone_{Guid.NewGuid():N}"[..24];

        builder.InitialCatalog = "master";
        string masterCs = builder.ConnectionString;

        try
        {
            await using (var conn = new SqlConnection(masterCs))
            {
                await conn.OpenAsync(ct);
                await using var cmd = new SqlCommand($"CREATE DATABASE [{cloneDb}]", conn);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            builder.InitialCatalog = cloneDb;
            string cloneCs = builder.ConnectionString;

            await using (var conn = new SqlConnection(cloneCs))
            {
                await conn.OpenAsync(ct);
                foreach (var batch in SplitOnGo(script))
                {
                    var trimmed = batch.Trim();
                    if (trimmed.Length == 0) continue;
                    await using var cmd = new SqlCommand(trimmed, conn);
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                // Confirm the table is now present in the clone DB.
                await using var verify = new SqlCommand(
                    "SELECT COUNT(*) FROM sys.tables WHERE name = 'SchemaParent'", conn);
                int count = Convert.ToInt32(await verify.ExecuteScalarAsync(ct));
                Assert.Equal(1, count);
            }
        }
        finally
        {
            builder.InitialCatalog = "master";
            await using var conn = new SqlConnection(builder.ConnectionString);
            await conn.OpenAsync(ct);
            // Force-close anyone using the clone and drop it.
            await using var drop = new SqlCommand(
                $"IF DB_ID('{cloneDb}') IS NOT NULL BEGIN " +
                $"ALTER DATABASE [{cloneDb}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
                $"DROP DATABASE [{cloneDb}]; END", conn);
            await drop.ExecuteNonQueryAsync(ct);
        }
    }

    [Fact]
    public async Task Generator_throws_on_unknown_table()
    {
        var generator = new SchemaScriptGenerator(_db.ConnectionString);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            generator.GenerateTableSchemaScriptAsync("dbo", "NoSuchTable"));
    }

    private static List<string> SplitOnGo(string sql)
    {
        var lines = sql.Split([Environment.NewLine, "\n"], StringSplitOptions.None);
        var batches = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                batches.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.AppendLine(line);
            }
        }
        if (current.Length > 0) batches.Add(current.ToString());
        return batches;
    }
}
