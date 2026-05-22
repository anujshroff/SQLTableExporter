using Xunit;

namespace SQLTableExporter.Tests.Unit;

public class ExportProgressDataTests : IDisposable
{
    private readonly string _tempDir;

    public ExportProgressDataTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"sqlexporter-progress-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void GetProgressFilePath_uses_schema_and_table_in_filename()
    {
        var path = ExportProgressData.GetProgressFilePath("C:\\out", "dbo", "Users");

        Assert.Equal(Path.Combine("C:\\out", "dbo_Users_export_progress.json"), path);
    }

    [Fact]
    public void SaveToDisk_then_LoadFromDisk_round_trips_basic_fields()
    {
        var saved = new ExportProgressData
        {
            Schema = "dbo",
            Table = "Users",
            OrderByColumns = "Id",
            FilePrefix = "Users",
            LastFileNumber = 3,
            TotalRows = 5000,
            ProcessedRows = 3000,
            MaxRowsPerFile = 1000,
            ExportCompleted = false
        };

        saved.SaveToDisk(_tempDir);

        var loaded = ExportProgressData.LoadFromDisk(_tempDir, "dbo", "Users", "Id", "Users");

        Assert.NotNull(loaded);
        Assert.Equal("dbo", loaded!.Schema);
        Assert.Equal("Users", loaded.Table);
        Assert.Equal("Id", loaded.OrderByColumns);
        Assert.Equal("Users", loaded.FilePrefix);
        Assert.Equal(3, loaded.LastFileNumber);
        Assert.Equal(5000, loaded.TotalRows);
        Assert.Equal(3000, loaded.ProcessedRows);
        Assert.Equal(1000, loaded.MaxRowsPerFile);
        Assert.False(loaded.ExportCompleted);
    }

    [Fact]
    public void LoadFromDisk_returns_null_when_no_progress_file()
    {
        var loaded = ExportProgressData.LoadFromDisk(_tempDir, "dbo", "Users", "Id");

        Assert.Null(loaded);
    }

    [Fact]
    public void LoadFromDisk_returns_null_when_schema_mismatch()
    {
        new ExportProgressData
        {
            Schema = "dbo",
            Table = "Users",
            OrderByColumns = "Id"
        }.SaveToDisk(_tempDir);

        var loaded = ExportProgressData.LoadFromDisk(_tempDir, "otherSchema", "Users", "Id");

        Assert.Null(loaded);
    }

    [Fact]
    public void LoadFromDisk_returns_null_when_order_columns_mismatch_and_validation_on()
    {
        new ExportProgressData
        {
            Schema = "dbo",
            Table = "Users",
            OrderByColumns = "Id"
        }.SaveToDisk(_tempDir);

        var loaded = ExportProgressData.LoadFromDisk(_tempDir, "dbo", "Users", "Name", validateOrderColumns: true);

        Assert.Null(loaded);
    }

    [Fact]
    public void LoadFromDisk_loads_when_order_columns_mismatch_but_validation_off()
    {
        new ExportProgressData
        {
            Schema = "dbo",
            Table = "Users",
            OrderByColumns = "Id"
        }.SaveToDisk(_tempDir);

        var loaded = ExportProgressData.LoadFromDisk(_tempDir, "dbo", "Users", "Name", validateOrderColumns: false);

        Assert.NotNull(loaded);
    }

    [Fact]
    public void UpdateOrderColumnValues_captures_each_column_and_marks_nulls()
    {
        var data = new ExportProgressData();
        var orderColumns = new List<KeysetPagination.OrderColumn>
        {
            new() { Name = "TenantId", SqlType = "int", Direction = "ASC", Value = 7 },
            new() { Name = "Id",       SqlType = "int", Direction = "ASC", Value = null },
            new() { Name = "Marker",   SqlType = "nvarchar", Direction = "DESC", Value = DBNull.Value }
        };

        data.UpdateOrderColumnValues(orderColumns);

        Assert.Equal(3, data.LastOrderColumnValues.Count);

        Assert.Equal("TenantId", data.LastOrderColumnValues[0].Name);
        Assert.Equal("ASC", data.LastOrderColumnValues[0].Direction);
        Assert.False(data.LastOrderColumnValues[0].IsNull);
        Assert.Equal("7", data.LastOrderColumnValues[0].ValueString);

        Assert.True(data.LastOrderColumnValues[1].IsNull);
        Assert.True(data.LastOrderColumnValues[2].IsNull);
        Assert.Equal("DESC", data.LastOrderColumnValues[2].Direction);
    }

    [Fact]
    public void UpdateOrderColumnValues_clears_previous_entries()
    {
        var data = new ExportProgressData
        {
            LastOrderColumnValues =
            [
                new() { Name = "Stale", SqlType = "int", Direction = "ASC", ValueString = "1" }
            ]
        };

        data.UpdateOrderColumnValues(
        [
            new() { Name = "Id", SqlType = "int", Direction = "ASC", Value = 99 }
        ]);

        Assert.Single(data.LastOrderColumnValues);
        Assert.Equal("Id", data.LastOrderColumnValues[0].Name);
    }

    [Fact]
    public void LastOrderColumnValues_survive_json_round_trip()
    {
        var saved = new ExportProgressData
        {
            Schema = "dbo",
            Table = "Users",
            OrderByColumns = "Id"
        };
        saved.UpdateOrderColumnValues(
        [
            new() { Name = "Id", SqlType = "int", Direction = "ASC", Value = 12345 }
        ]);

        saved.SaveToDisk(_tempDir);
        var loaded = ExportProgressData.LoadFromDisk(_tempDir, "dbo", "Users", "Id");

        Assert.NotNull(loaded);
        Assert.Single(loaded!.LastOrderColumnValues);
        Assert.Equal("12345", loaded.LastOrderColumnValues[0].ValueString);
        Assert.False(loaded.LastOrderColumnValues[0].IsNull);
    }
}
