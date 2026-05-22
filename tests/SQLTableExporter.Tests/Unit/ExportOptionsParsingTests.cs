using Xunit;

namespace SQLTableExporter.Tests.Unit;

public class ExportOptionsParsingTests
{
    [Fact]
    public void Defaults_are_applied_when_only_schema_and_table_provided()
    {
        var options = ExportOptions.ParseArgs(["-s", "dbo", "-t", "Users"]);

        Assert.NotNull(options);
        Assert.Equal("dbo", options.SchemaName);
        Assert.Equal("Users", options.TableName);
        Assert.Equal("dbo_Users_export", options.OutputDirectory);
        Assert.Equal(1_000_000, options.MaxRowsPerFile);
        Assert.Equal(5_000, options.QueryBatchSize);
        Assert.Equal(10, options.BatchDelayMilliseconds);
        Assert.Equal(3600, options.CommandTimeoutSeconds);
        Assert.False(options.SnapshotIsolation);
        Assert.False(options.ArchiveOutput);
        Assert.False(options.Restart);
        Assert.True(options.TrackProgress);
        Assert.True(options.GenerateSchemaScript);
    }

    [Fact]
    public void Connection_and_output_short_flags_round_trip()
    {
        var options = ExportOptions.ParseArgs([
            "-c", "Server=test;Database=db;",
            "-s", "dbo",
            "-t", "Orders",
            "-o", "C:\\exports\\foo"
        ]);

        Assert.NotNull(options);
        Assert.Equal("Server=test;Database=db;", options.ConnectionString);
        Assert.Equal("C:\\exports\\foo", options.OutputDirectory);
    }

    [Fact]
    public void Long_form_flags_are_accepted()
    {
        var options = ExportOptions.ParseArgs([
            "--connection", "cs",
            "--schema", "dbo",
            "--table", "T",
            "--output", "out",
            "--order-by", "Id, Name DESC",
            "--prefix", "myprefix",
            "--rows", "250000",
            "--batch-size", "2500",
            "--delay", "5",
            "--timeout", "1800"
        ]);

        Assert.NotNull(options);
        Assert.Equal("cs", options.ConnectionString);
        Assert.Equal("Id, Name DESC", options.OrderByColumns);
        Assert.Equal("myprefix", options.FilePrefix);
        Assert.Equal(250_000, options.MaxRowsPerFile);
        Assert.Equal(2500, options.QueryBatchSize);
        Assert.Equal(5, options.BatchDelayMilliseconds);
        Assert.Equal(1800, options.CommandTimeoutSeconds);
    }

    [Fact]
    public void Boolean_flags_toggle_correctly()
    {
        var options = ExportOptions.ParseArgs([
            "-s", "dbo", "-t", "T",
            "--snapshot-isolation",
            "--archive",
            "--restart",
            "--no-progress-tracking",
            "--no-schema-script"
        ]);

        Assert.NotNull(options);
        Assert.True(options.SnapshotIsolation);
        Assert.True(options.ArchiveOutput);
        Assert.True(options.Restart);
        Assert.False(options.TrackProgress);
        Assert.False(options.GenerateSchemaScript);
    }

    [Fact]
    public void Archive_sets_default_archive_path_using_schema_and_table()
    {
        var options = ExportOptions.ParseArgs([
            "-s", "dbo", "-t", "Orders",
            "--archive"
        ]);

        Assert.NotNull(options);
        Assert.Equal("dbo_Orders_export.zip", options.ArchivePath);
    }

    [Fact]
    public void Custom_archive_path_overrides_default()
    {
        var options = ExportOptions.ParseArgs([
            "-s", "dbo", "-t", "Orders",
            "--archive",
            "--archive-path", "C:\\out\\custom.zip"
        ]);

        Assert.NotNull(options);
        Assert.Equal("C:\\out\\custom.zip", options.ArchivePath);
    }

    [Fact]
    public void Azure_blob_storage_url_round_trips()
    {
        var url = "https://acct.blob.core.windows.net/container/folder";
        var options = ExportOptions.ParseArgs([
            "-s", "dbo", "-t", "T",
            "--azure-blob-storage", url
        ]);

        Assert.NotNull(options);
        Assert.Equal(url, options.AzureBlobStorageUrl);
    }

    [Fact]
    public void Where_with_param_produces_parsed_condition_and_value()
    {
        var options = ExportOptions.ParseArgs([
            "-s", "dbo", "-t", "Orders",
            "-w", "OrderDate > :minDate",
            "--param", "minDate=2023-01-01"
        ]);

        Assert.NotNull(options);
        Assert.NotNull(options.ParsedWhereCondition);
        Assert.True(options.ParsedWhereCondition!.HasCondition);
        Assert.Equal("OrderDate > @where_minDate", options.ParsedWhereCondition.Sql);
        Assert.Equal("2023-01-01", options.ParsedWhereCondition.Parameters["@where_minDate"]);
        Assert.Equal("2023-01-01", options.WhereParameters["minDate"]);
    }

    [Fact]
    public void Where_without_parameters_returns_null()
    {
        // Per the security model, raw literal WHERE clauses are rejected:
        // they must use :param placeholders.
        var options = ExportOptions.ParseArgs([
            "-s", "dbo", "-t", "Orders",
            "-w", "OrderDate > '2023-01-01'"
        ]);

        Assert.Null(options);
    }

    [Fact]
    public void Where_with_param_placeholder_but_no_param_value_returns_null()
    {
        var options = ExportOptions.ParseArgs([
            "-s", "dbo", "-t", "Orders",
            "-w", "OrderDate > :minDate"
        ]);

        Assert.Null(options);
    }

    [Fact]
    public void Multiple_params_are_all_captured()
    {
        var options = ExportOptions.ParseArgs([
            "-s", "dbo", "-t", "Orders",
            "-w", "OrderDate > :minDate AND Total > :minTotal",
            "--param", "minDate=2023-01-01",
            "--param", "minTotal=1000"
        ]);

        Assert.NotNull(options);
        Assert.Equal("2023-01-01", options.WhereParameters["minDate"]);
        Assert.Equal("1000", options.WhereParameters["minTotal"]);
        Assert.Equal(2, options.ParsedWhereCondition!.Parameters.Count);
    }

    [Fact]
    public void Positional_output_directory_is_picked_up()
    {
        var options = ExportOptions.ParseArgs([
            "-s", "dbo", "-t", "Orders",
            "C:\\exports\\positional"
        ]);

        Assert.NotNull(options);
        Assert.Equal("C:\\exports\\positional", options.OutputDirectory);
    }

    [Theory]
    [InlineData("-r", "0")]
    [InlineData("-r", "-100")]
    [InlineData("--batch-size", "notanumber")]
    [InlineData("--timeout", "0")]
    public void Invalid_numeric_values_are_ignored_and_defaults_kept(string flag, string value)
    {
        var options = ExportOptions.ParseArgs(["-s", "dbo", "-t", "T", flag, value]);

        Assert.NotNull(options);
        // Defaults preserved
        Assert.Equal(1_000_000, options.MaxRowsPerFile);
        Assert.Equal(5_000, options.QueryBatchSize);
        Assert.Equal(3600, options.CommandTimeoutSeconds);
    }

    [Fact]
    public void Missing_schema_and_table_still_returns_options_with_dot_output()
    {
        // Validation of required args happens in Main, not ParseArgs.
        var options = ExportOptions.ParseArgs([]);

        Assert.NotNull(options);
        Assert.Equal(".", options.OutputDirectory);
        Assert.Empty(options.SchemaName);
        Assert.Empty(options.TableName);
    }
}
