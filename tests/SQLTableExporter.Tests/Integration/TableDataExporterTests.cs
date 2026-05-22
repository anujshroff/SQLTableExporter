using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Xunit;

namespace SQLTableExporter.Tests.Integration;

[Collection("Database")]
public class TableDataExporterTests(DatabaseFixture db) : IDisposable
{
    private readonly DatabaseFixture _db = db;
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), $"sqlexporter-export-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    private ExportOptions BaseOptions(string table, string orderBy = "Id", int batchSize = 5000)
    {
        Directory.CreateDirectory(_tempDir);
        return new ExportOptions
        {
            ConnectionString = _db.ConnectionString,
            SchemaName = "dbo",
            TableName = table,
            OutputDirectory = _tempDir,
            OrderByColumns = orderBy,
            FilePrefix = table,
            QueryBatchSize = batchSize,
            MaxRowsPerFile = 1_000_000,
            BatchDelayMilliseconds = 0,
            CommandTimeoutSeconds = 60,
            GenerateSchemaScript = false,
            TrackProgress = true
        };
    }

    private static ExportProgressData ProgressFor(string table, string orderBy = "Id") => new()
    {
        Schema = "dbo",
        Table = table,
        OrderByColumns = orderBy,
        FilePrefix = table,
        MaxRowsPerFile = 1_000_000
    };

    private static List<List<string>> ReadCsvRows(string path)
    {
        // Use CsvHelper so quoted fields containing bare CR / NUL / comma /
        // quote (the bug-#3 / bug-#5 fixtures) round-trip correctly. A naive
        // File.ReadAllLines + Split(',') breaks because File.ReadAllLines
        // splits on \r inside quoted fields.
        using var reader = new StreamReader(path);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture));
        var rows = new List<List<string>>();
        while (csv.Read())
        {
            var row = new List<string>();
            for (int i = 0; csv.TryGetField<string>(i, out var v); i++)
            {
                row.Add(v ?? string.Empty);
            }
            rows.Add(row);
        }
        return rows;
    }

    private static List<int> ReadIdColumn(string outputDir, string filePrefix)
    {
        var ids = new List<int>();
        var files = Directory.GetFiles(outputDir, $"{filePrefix}_*.csv")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var file in files)
        {
            var rows = ReadCsvRows(file);
            // Skip header row.
            foreach (var row in rows.Skip(1))
            {
                if (row.Count == 0 || string.IsNullOrEmpty(row[0])) continue;
                ids.Add(int.Parse(row[0], CultureInfo.InvariantCulture));
            }
        }
        return ids;
    }

    [Fact]
    public async Task Exports_standard_table_with_correct_row_count_and_header()
    {
        var options = BaseOptions("TypeAudit_Standard");
        var progress = ProgressFor("TypeAudit_Standard");

        var exporter = new TableDataExporter(options);
        await exporter.ExportTableAsync("dbo", "TypeAudit_Standard", "Id", "TypeAudit_Standard", progress);

        string csv = Path.Combine(_tempDir, "TypeAudit_Standard_1.csv");
        Assert.True(File.Exists(csv));

        var rows = ReadCsvRows(csv);
        Assert.Equal("Id", rows[0][0]);
        Assert.Equal("TestCase", rows[0][1]);

        Assert.Equal(6, ReadIdColumn(_tempDir, "TypeAudit_Standard").Count);
        Assert.True(progress.ExportCompleted);
    }

    [Fact]
    public async Task Bulk_export_with_small_batch_paginates_correctly_with_no_gaps_or_dupes()
    {
        // 500 rows, batch 100 forces 5 keyset queries + one terminal empty fetch.
        var options = BaseOptions("TypeAudit_Bulk_Standard", batchSize: 100);
        var progress = ProgressFor("TypeAudit_Bulk_Standard");

        var exporter = new TableDataExporter(options);
        await exporter.ExportTableAsync(
            "dbo", "TypeAudit_Bulk_Standard", "Id", "TypeAudit_Bulk_Standard", progress);

        var ids = ReadIdColumn(_tempDir, "TypeAudit_Bulk_Standard");

        Assert.Equal(500, ids.Count);
        Assert.Equal(500, ids.Distinct().Count());
        Assert.Equal(1, ids.Min());
        Assert.Equal(500, ids.Max());
        Assert.True(ids.SequenceEqual(ids.OrderBy(i => i)),
            "Exported rows must be in strict ascending Id order across batches.");
    }

    [Fact]
    public async Task High_precision_decimal_table_exports_without_overflow()
    {
        // Guards against bug #1: 29-digit DECIMAL(38,0) values exceed
        // .NET decimal range. The exporter must route those columns through
        // SqlDecimal so GetValue doesn't throw.
        var options = BaseOptions("TypeAudit_HighPrecision");
        var progress = ProgressFor("TypeAudit_HighPrecision");

        var exporter = new TableDataExporter(options);
        await exporter.ExportTableAsync(
            "dbo", "TypeAudit_HighPrecision", "Id", "TypeAudit_HighPrecision", progress);

        string csv = Path.Combine(_tempDir, "TypeAudit_HighPrecision_1.csv");
        string content = await File.ReadAllTextAsync(csv, TestContext.Current.CancellationToken);

        Assert.Contains("79228162514264337593543950335", content);
        Assert.Contains("99999999999999999999999999999", content);
    }

    [Fact]
    public async Task Udt_table_exports_without_throwing()
    {
        // Guards against bug #7: hierarchyid / geography / geometry would
        // throw at row read time without the CLR-UDT-to-text projection.
        var options = BaseOptions("TypeAudit_UDT");
        var progress = ProgressFor("TypeAudit_UDT");

        var exporter = new TableDataExporter(options);
        await exporter.ExportTableAsync(
            "dbo", "TypeAudit_UDT", "Id", "TypeAudit_UDT", progress);

        string csv = Path.Combine(_tempDir, "TypeAudit_UDT_1.csv");
        Assert.True(File.Exists(csv));

        var rows = ReadCsvRows(csv);
        // Header includes the UDT column names.
        Assert.Contains("HierarchyIdCol", rows[0]);
        Assert.Contains("GeographyCol", rows[0]);
        Assert.Contains("GeometryCol", rows[0]);
        // One data row with /1/2/3/ hierarchyid.
        Assert.Equal(2, rows.Count);
        Assert.Contains("/1/2/3/", string.Join(",", rows[1]));
    }

    [Fact]
    public async Task Where_condition_filters_to_subset_of_rows()
    {
        var wc = WhereCondition.Parse("Id > :minId");
        Assert.NotNull(wc);
        wc!.AddParameter("minId", "400");

        var options = BaseOptions("TypeAudit_Bulk_Standard", batchSize: 100);
        options.ParsedWhereCondition = wc;

        var progress = ProgressFor("TypeAudit_Bulk_Standard");
        var exporter = new TableDataExporter(options);
        await exporter.ExportTableAsync(
            "dbo", "TypeAudit_Bulk_Standard", "Id", "TypeAudit_Bulk_Standard", progress);

        var ids = ReadIdColumn(_tempDir, "TypeAudit_Bulk_Standard");

        Assert.Equal(100, ids.Count);
        Assert.Equal(401, ids.Min());
        Assert.Equal(500, ids.Max());
    }

    [Fact]
    public async Task Restart_resumes_keyset_from_saved_order_column_values()
    {
        // Simulate a partial run: progress file says we already exported rows
        // 1..250 in file Bulk_1.csv. With Restart=true, the exporter should
        // resume at Id > 250 and produce 250 more rows in file Bulk_2.csv.
        Directory.CreateDirectory(_tempDir);

        var existingProgress = new ExportProgressData
        {
            Schema = "dbo",
            Table = "TypeAudit_Bulk_Standard",
            OrderByColumns = "Id",
            FilePrefix = "TypeAudit_Bulk_Standard",
            MaxRowsPerFile = 1_000_000,
            LastFileNumber = 1,
            ProcessedRows = 250,
            LastOrderColumnValues =
            [
                new() { Name = "Id", SqlType = "int", Direction = "ASC", ValueString = "250", IsNull = false }
            ]
        };
        existingProgress.SaveToDisk(_tempDir);

        var options = BaseOptions("TypeAudit_Bulk_Standard", batchSize: 100);
        options.Restart = true;

        // LoadFromDisk would normally be done by Main; replicate that.
        var loaded = ExportProgressData.LoadFromDisk(
            _tempDir, "dbo", "TypeAudit_Bulk_Standard", "Id", "TypeAudit_Bulk_Standard")!;

        var exporter = new TableDataExporter(options);
        await exporter.ExportTableAsync(
            "dbo", "TypeAudit_Bulk_Standard", "Id", "TypeAudit_Bulk_Standard", loaded);

        // The resumed file should be Bulk_2.csv onward.
        var resumed = Directory.GetFiles(_tempDir, "TypeAudit_Bulk_Standard_*.csv")
            .Where(f => !f.EndsWith("_1.csv"))
            .ToList();
        Assert.NotEmpty(resumed);

        // Count only the rows produced by this run (skip Bulk_1.csv which never existed).
        var ids = new List<int>();
        foreach (var file in resumed.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var rows = ReadCsvRows(file);
            foreach (var row in rows.Skip(1))
            {
                if (row.Count > 0 && !string.IsNullOrEmpty(row[0]))
                    ids.Add(int.Parse(row[0], CultureInfo.InvariantCulture));
            }
        }

        Assert.Equal(250, ids.Count);
        Assert.Equal(251, ids.Min());
        Assert.Equal(500, ids.Max());
    }

    [Fact]
    public async Task Snapshot_isolation_export_produces_same_data_as_default()
    {
        var options = BaseOptions("TypeAudit_Standard");
        options.SnapshotIsolation = true;

        var progress = ProgressFor("TypeAudit_Standard");
        var exporter = new TableDataExporter(options);
        await exporter.ExportTableAsync("dbo", "TypeAudit_Standard", "Id", "TypeAudit_Standard", progress);

        Assert.Equal(6, ReadIdColumn(_tempDir, "TypeAudit_Standard").Count);
    }

    [Fact]
    public async Task MaxRowsPerFile_splits_export_across_multiple_files()
    {
        var options = BaseOptions("TypeAudit_Bulk_Standard", batchSize: 100);
        options.MaxRowsPerFile = 200; // 500 rows → 3 files (200 + 200 + 100)

        var progress = ProgressFor("TypeAudit_Bulk_Standard");
        var exporter = new TableDataExporter(options);
        await exporter.ExportTableAsync(
            "dbo", "TypeAudit_Bulk_Standard", "Id", "TypeAudit_Bulk_Standard", progress);

        var files = Directory.GetFiles(_tempDir, "TypeAudit_Bulk_Standard_*.csv");
        Assert.Equal(3, files.Length);

        var ids = ReadIdColumn(_tempDir, "TypeAudit_Bulk_Standard");
        Assert.Equal(500, ids.Count);
        Assert.True(ids.SequenceEqual(Enumerable.Range(1, 500)));
    }
}
