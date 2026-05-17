# SQL Table Exporter

A high-performance .NET CLI tool for exporting large SQL Server tables to CSV files with built-in pagination, restartability, schema script generation, and ultra-compressed archiving.

## Features

- **Efficient Pagination**: Uses keyset pagination for optimal performance with large tables
- **Chunked Export**: Breaks exports into multiple files with configurable row limits
- **Progress Tracking**: Monitors export progress with ETA and performance metrics
- **Restart Capability**: Resume interrupted exports from the last saved position
- **Schema Script Generation**: Automatically creates table schema scripts
- **Primary Key Detection**: Automatically detects and uses primary keys for ordering
- **Ultra Compression**: Optionally archives output using ZIP format with LZMA compression
- **Azure Blob Storage**: Upload export results directly to Azure Blob Storage

## Requirements

- .NET 10.0 runtime (Windows, Linux, or macOS)
- SQL Server database access
- Appropriate permissions to read from the tables you want to export

## Installation

Install as a global .NET tool:

```bash
dotnet tool install --global AnujShroff.SQLTableExporter
```

## Usage

```bash
sqltableexporter [OPTIONS] [OUTPUT_DIRECTORY]
```

### Basic Examples

Minimum required parameters:
```bash
sqltableexporter -c "Server=myserver.database.windows.net;Database=mydb;Authentication=Active Directory Default;" -s dbo -t Products
```

Export to a specific directory:
```bash
sqltableexporter -c "connection-string" -s dbo -t Products -o ./exports
```

Use snapshot isolation for a consistent view of data during export:
```bash
sqltableexporter -c "connection-string" -s dbo -t Orders --snapshot-isolation
```

Use parameterized WHERE conditions for safer queries:
```bash
sqltableexporter -c "connection-string" -s dbo -t Orders -w "OrderDate > :minDate AND Total > :minTotal" --param minDate=2023-01-01 --param minTotal=1000
```

Customize performance settings:
```bash
sqltableexporter -c "connection-string" -s dbo -t LargeTable -b 100000 -d 100 --timeout 7200
```

Customize file chunking:
```bash
sqltableexporter -c "connection-string" -s dbo -t Orders --rows 500000
```

Export without progress tracking or schema script generation:
```bash
sqltableexporter -c "connection-string" -s dbo -t Products --no-progress-tracking --no-schema-script
```

Resume a previously interrupted export:
```bash
sqltableexporter -c "connection-string" -s dbo -t LargeTable --restart
```

Archive output:
```bash
sqltableexporter -c "connection-string" -s dbo -t Orders --archive
```

Upload export results to Azure Blob Storage:
```bash
sqltableexporter -c "connection-string" -s dbo -t Products --azure-blob-storage "https://mystorageaccount.blob.core.windows.net/mycontainer"
```

Upload archived export results to Azure Blob Storage:
```bash
sqltableexporter -c "connection-string" -s dbo -t Products --archive --azure-blob-storage "https://mystorageaccount.blob.core.windows.net/mycontainer"
```

## Command-Line Options

| Option                   | Description                                                      | Default        |
|--------------------------|------------------------------------------------------------------|----------------|
| `-c, --connection`       | Connection string to SQL database                                | Required       |
| `-o, --output`           | Output directory for CSV files                                   | schema_table_export |
| `-s, --schema`           | Database schema name                                             | Required       |
| `-t, --table`            | Table name to export                                             | Required       |
| `--order-by`             | Column(s) to order by during export                              | Primary key    |
| `-p, --prefix`           | Prefix for output CSV files                                      | Table name     |
| `-r, --rows`             | Maximum rows per CSV file                                        | 1,000,000      |
| `-b, --batch-size`       | Number of rows to fetch per query                                | 5,000          |
| `-d, --delay`            | Delay in milliseconds between query batches                      | 10             |
| `--timeout`              | Command timeout in seconds                                       | 3,600          |
| `--restart`              | Restart from previous export progress                            | False          |
| `--no-progress-tracking` | Disable progress tracking                                        | False          |
| `--no-schema-script`     | Disable schema script generation                                 | False          |
| `--snapshot-isolation`   | Use snapshot isolation for consistent view of data during export | False |
| `--archive`              | Archive the output directory after export                        | False          |
| `--archive-path`         | Custom path for the archive file                                 | schema_table_export.zip |
| `--azure-blob-storage`   | Azure Blob Storage URL for uploading export results              | None           |
| `-w, --where`            | Optional WHERE condition to filter data                          | None           |
| `--param`                | Parameter value for WHERE condition                              | None           |

## Updating

On every run, the tool checks NuGet for a newer published version. If one is available, it prints a banner at the end of execution telling you to run:

```bash
dotnet tool update --global AnujShroff.SQLTableExporter
```

The check is non-blocking, fails silently on network errors, and skips prereleases. There is no opt-out flag — the check is cheap and runs in the background while the export proceeds.

To see the installed version, use the standard dotnet tools command:

```bash
dotnet tool list --global
```

To uninstall:

```bash
dotnet tool uninstall --global AnujShroff.SQLTableExporter
```

## Output Files

The tool produces the following files:

- **CSV Files**: `{prefix}_{number}.csv` - Each file contains up to the specified maximum rows
- **Schema Script**: `{prefix}_schema.sql` - SQL script to recreate the table structure
- **Progress File**: `{schema}_{table}_export_progress.json` - Used for tracking and restarting exports
- **Archive File**: `{schema}_{table}_export.zip` - If archiving is enabled

## Advanced Features

### Keyset Pagination

The exporter uses keyset pagination for optimal performance, which is significantly more efficient than OFFSET/FETCH for large tables. It works by tracking the values of the ordering columns in the last row of each batch and using them to fetch the next batch.

### Progress Tracking

During export, the tool provides detailed progress information:
- Percentage complete
- Current file number
- Elapsed and estimated remaining time
- Export throughput (rows/second)

### Restart Capability

The `--restart` option allows you to resume an interrupted export. The tool saves progress after each file is completed, including the values of the ordering columns from the last processed row. This allows for precise resumption without duplicates or gaps.

### Schema Script Generation

The tool can generate a comprehensive SQL script that recreates the table structure, including:
- Column definitions with correct data types and nullability
- Primary keys
- Foreign keys with referential actions
- Indexes
- Check constraints

### Archive Compression

When the `--archive` option is enabled, the tool will:
1. Export the data to CSV files in the specified output directory
2. Create a highly compressed archive of all output files using ZIP format with LZMA compression
3. Generate detailed compression statistics showing space savings

The archiving feature ensures maximum space efficiency with the following approach:
- Uses ZIP file format, which is compatible with most standard archiving tools
- Applies LZMA compression for maximum compression ratio
- Provides progress reporting during the archiving process
- Optimized for best compression ratio rather than speed

**Note**: The archiving operation is considered a critical part of the process. If archiving fails, the entire export operation will report failure. This ensures data integrity for archival purposes.

### Azure Blob Storage Integration

The `--azure-blob-storage` option allows you to upload export results directly to Azure Blob Storage:

- **Intelligent URL Parsing**: Automatically detects and handles regular URLs (for managed identity) or SAS URLs
- **Flexible Authentication**: Supports managed identity or SAS token authentication
- **Directory Structure Preservation**: Maintains the folder structure when uploading files
- **Configurable Upload Behavior**: 
  - With `--archive` option: Only uploads the archive file
  - Without `--archive`: Uploads all CSV files preserving directory structure
- **Detailed Progress Reporting**: Shows upload speed, estimated time, and total progress

The Azure Blob Storage URL must follow one of these formats:
- Regular URL (managed identity): `https://{account}.blob.core.windows.net/{container}/{optional-folder-path}`
- SAS URL: `https://{account}.blob.core.windows.net/{container}/{optional-folder-path}?{sas-token}`

**Note**: When using the managed identity option, the application must run in an environment with access to Azure Active Directory, such as an Azure VM, App Service, or local environment with Azure CLI authentication.

## Performance Considerations

- **Batch Size**: Adjust the `-b, --batch-size` parameter based on your table structure. Smaller batches reduce memory usage but increase the number of queries.
- **Delay**: The `-d, --delay` parameter adds a configurable delay between query batches to reduce server load.
- **Order By**: Select columns with high cardinality for ordering to optimize pagination performance.

## Error Handling

The tool has built-in error handling for common issues:
- Connection problems
- Permission issues
- Timeout errors
- Data type conversion challenges
- Archiving failures

If an error occurs, the tool will display the error message and stack trace. When using the restart feature, it will resume from the last successfully exported file.

## Default Directory Structure

By default, the tool will:
1. Create a directory named `{schema}_{table}_export` if no output directory is specified
2. Ensure the output directory is empty before starting (unless using `--restart`)
3. If archiving is enabled, create an archive file named `{schema}_{table}_export.zip` in the current working directory (i.e. the shell directory from which `sqltableexporter` was invoked)
