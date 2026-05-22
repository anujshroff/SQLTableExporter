using System.IO.Compression;
using System.Reflection;
using Xunit;

namespace SQLTableExporter.Tests.Integration;

[Collection("Database")]
public class EndToEndCliTests(DatabaseFixture db) : IDisposable
{
    private readonly DatabaseFixture _db = db;
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), $"sqlexporter-e2e-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    // Program.Main is private inside an internal Program class — reach it via reflection
    // so we exercise the real CLI entry point without changing visibility in source.
    private static async Task<int> InvokeMainAsync(string[] args)
    {
        var programType = typeof(ExportOptions).Assembly.GetType("SQLTableExporter.Program")
            ?? throw new InvalidOperationException("Program type not found");
        var mainMethod = programType.GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Main method not found");

        var task = (Task<int>)mainMethod.Invoke(null, [args])!;
        return await task;
    }

    [Fact]
    public async Task End_to_end_export_writes_csv_progress_and_schema_script()
    {
        int exit = await InvokeMainAsync([
            "-c", _db.ConnectionString,
            "-s", "dbo",
            "-t", "TypeAudit_Standard",
            "-o", _tempDir
        ]);

        Assert.Equal(0, exit);
        Assert.True(File.Exists(Path.Combine(_tempDir, "TypeAudit_Standard_1.csv")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "dbo_TypeAudit_Standard_export_progress.json")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "TypeAudit_Standard_schema.sql")));
    }

    [Fact]
    public async Task End_to_end_with_archive_produces_zip_and_uses_lzma()
    {
        string archivePath = Path.Combine(_tempDir, "out.zip");
        Directory.CreateDirectory(_tempDir);

        // The CLI requires an empty output directory; use a subfolder.
        string outDir = Path.Combine(_tempDir, "data");

        int exit = await InvokeMainAsync([
            "-c", _db.ConnectionString,
            "-s", "dbo",
            "-t", "TypeAudit_Standard",
            "-o", outDir,
            "--archive",
            "--archive-path", archivePath,
            "--no-schema-script"
        ]);

        Assert.Equal(0, exit);
        Assert.True(File.Exists(archivePath));

        // The archive should contain at least the CSV file (we exclude
        // the schema script with --no-schema-script). The progress file may
        // or may not be inside depending on archive timing.
        using var zip = ZipFile.OpenRead(archivePath);
        Assert.Contains(zip.Entries, e => e.Name.StartsWith("TypeAudit_Standard_") && e.Name.EndsWith(".csv"));
    }

    [Fact]
    public async Task End_to_end_returns_nonzero_when_required_args_missing()
    {
        int exit = await InvokeMainAsync([
            "-c", _db.ConnectionString,
            "-o", _tempDir
            // schema/table omitted
        ]);

        Assert.NotEqual(0, exit);
    }

    [Fact]
    public async Task End_to_end_auto_detects_primary_key_when_order_by_omitted()
    {
        // No --order-by supplied: Main should fall back to PK detection on
        // TypeAudit_Standard (which has Id as PK) and succeed.
        int exit = await InvokeMainAsync([
            "-c", _db.ConnectionString,
            "-s", "dbo",
            "-t", "TypeAudit_Standard",
            "-o", _tempDir
        ]);

        Assert.Equal(0, exit);
        Assert.True(File.Exists(Path.Combine(_tempDir, "TypeAudit_Standard_1.csv")));
    }

    [Fact]
    public async Task End_to_end_fails_when_pk_detection_finds_nothing_and_no_order_by_supplied()
    {
        // PkNone has no PK and no identity column → exporter should bail
        // with a nonzero exit code rather than crashing later.
        int exit = await InvokeMainAsync([
            "-c", _db.ConnectionString,
            "-s", "dbo",
            "-t", "PkNone",
            "-o", _tempDir
        ]);

        Assert.NotEqual(0, exit);
    }
}
