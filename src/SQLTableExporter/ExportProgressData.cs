using System.Text.Json;
using System.Text.Json.Serialization;

namespace SQLTableExporter;

/// <summary>
/// Represents the progress data for an export operation
/// </summary>
public class ExportProgressData
{
    /// <summary>
    /// Database schema name
    /// </summary>
    [JsonPropertyName("schema")]
    public string Schema { get; set; } = string.Empty;

    /// <summary>
    /// Table name being exported
    /// </summary>
    [JsonPropertyName("table")]
    public string Table { get; set; } = string.Empty;

    /// <summary>
    /// The column(s) used for ordering data
    /// </summary>
    [JsonPropertyName("orderByColumns")]
    public string OrderByColumns { get; set; } = string.Empty;

    /// <summary>
    /// The last file number that was completed
    /// </summary>
    [JsonPropertyName("lastFileNumber")]
    public int LastFileNumber { get; set; }

    /// <summary>
    /// The file prefix used for exported files
    /// </summary>
    [JsonPropertyName("filePrefix")]
    public string FilePrefix { get; set; } = string.Empty;

    /// <summary>
    /// Total number of rows in the table
    /// </summary>
    [JsonPropertyName("totalRows")]
    public int TotalRows { get; set; }

    /// <summary>
    /// Number of rows processed so far
    /// </summary>
    [JsonPropertyName("processedRows")]
    public int ProcessedRows { get; set; }

    /// <summary>
    /// Timestamp when this progress data was last updated
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The maximum number of rows per file
    /// </summary>
    [JsonPropertyName("maxRowsPerFile")]
    public int MaxRowsPerFile { get; set; }

    /// <summary>
    /// The values of the ordering columns from the last processed row
    /// </summary>
    [JsonPropertyName("lastOrderColumnValues")]
    public List<OrderColumnValue> LastOrderColumnValues { get; set; } = [];

    /// <summary>
    /// Whether the data export phase completed successfully
    /// </summary>
    [JsonPropertyName("exportCompleted")]
    public bool ExportCompleted { get; set; } = false;

    /// <summary>
    /// Whether the archiving phase completed successfully
    /// </summary>
    [JsonPropertyName("archiveCompleted")]
    public bool ArchiveCompleted { get; set; } = false;

    /// <summary>
    /// Path to the created archive file (null if archiving not requested or not completed)
    /// </summary>
    [JsonPropertyName("archivePath")]
    public string? ArchivePath { get; set; } = null;

    /// <summary>
    /// Whether the Azure Blob Storage upload phase completed successfully
    /// </summary>
    [JsonPropertyName("uploadCompleted")]
    public bool UploadCompleted { get; set; } = false;

    /// <summary>
    /// Static JSON serializer options used for loading and saving progress data
    /// </summary>
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Get the progress file path for the specified schema and table
    /// </summary>
    public static string GetProgressFilePath(string outputDirectory, string schema, string table) =>
        Path.Combine(outputDirectory, $"{schema}_{table}_export_progress.json");

    /// <summary>
    /// Save the current progress data to disk
    /// </summary>
    public void SaveToDisk(string outputDirectory)
    {
        try
        {
            string progressFilePath = GetProgressFilePath(outputDirectory, Schema, Table);
            string jsonContent = JsonSerializer.Serialize(this, _jsonOptions);
            File.WriteAllText(progressFilePath, jsonContent);

            // Update timestamp
            Timestamp = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving progress data: {ex.Message}");
        }
    }

    /// <summary>
    /// Update the order column values based on the current state of the keyset pagination
    /// </summary>
    public void UpdateOrderColumnValues(List<KeysetPagination.OrderColumn> orderColumns)
    {
        LastOrderColumnValues.Clear();

        foreach (KeysetPagination.OrderColumn column in orderColumns)
        {
            OrderColumnValue columnValue = new()
            {
                Name = column.Name,
                SqlType = column.SqlType,
                Direction = column.Direction,
                IsNull = column.Value == null || column.Value == DBNull.Value
            };

            if (!columnValue.IsNull)
            {
                // Store the string representation of the value
                columnValue.ValueString = column.Value?.ToString();
            }

            LastOrderColumnValues.Add(columnValue);
        }
    }

    /// <summary>
    /// Try to load progress data from disk
    /// </summary>
    /// <returns>Loaded progress data or null if not found or invalid</returns>
    public static ExportProgressData? LoadFromDisk(
        string outputDirectory,
        string schema,
        string table,
        string orderByColumns,
        string? filePrefix = null,
        bool validateOrderColumns = true)
    {
        string progressFilePath = GetProgressFilePath(outputDirectory, schema, table);

        if (!File.Exists(progressFilePath))
        {
            return null;
        }

        try
        {
            string jsonContent = File.ReadAllText(progressFilePath);
            ExportProgressData? loadedData = JsonSerializer.Deserialize<ExportProgressData>(jsonContent);

            if (loadedData == null)
            {
                return null;
            }

            // Basic validation that it's for the right schema/table
            if (loadedData.Schema != schema || loadedData.Table != table)
            {
                Console.WriteLine("Warning: Found progress file but schema or table doesn't match.");
                Console.WriteLine($"  Saved schema: {loadedData.Schema}, Current schema: {schema}");
                Console.WriteLine($"  Saved table: {loadedData.Table}, Current table: {table}");
                return null;
            }

            // Optional validation for order columns
            if (validateOrderColumns && loadedData.OrderByColumns != orderByColumns)
            {
                Console.WriteLine("Warning: Found progress file but order columns don't match.");
                Console.WriteLine($"  Saved order columns: {loadedData.OrderByColumns}, Current order columns: {orderByColumns}");
                return null;
            }

            // Optional validation for file prefix
            if (filePrefix != null && !string.IsNullOrEmpty(filePrefix) && loadedData.FilePrefix != filePrefix)
            {
                Console.WriteLine("Warning: Found progress file but file prefix doesn't match.");
                Console.WriteLine($"  Saved prefix: {loadedData.FilePrefix}, Current prefix: {filePrefix}");
                return null;
            }

            return loadedData;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading progress data: {ex.Message}");
            return null;
        }
    }
}

/// <summary>
/// Represents a value for an ordering column, used for restart
/// </summary>
public class OrderColumnValue
{
    /// <summary>
    /// The name of the column
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The SQL type of the column
    /// </summary>
    [JsonPropertyName("sqlType")]
    public string SqlType { get; set; } = string.Empty;

    /// <summary>
    /// The direction of ordering (ASC/DESC)
    /// </summary>
    [JsonPropertyName("direction")]
    public string Direction { get; set; } = "ASC";

    /// <summary>
    /// The string representation of the value
    /// </summary>
    [JsonPropertyName("valueString")]
    public string? ValueString { get; set; }

    /// <summary>
    /// Whether the value is null
    /// </summary>
    [JsonPropertyName("isNull")]
    public bool IsNull { get; set; }
}
