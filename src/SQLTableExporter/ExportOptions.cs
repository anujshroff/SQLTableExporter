namespace SQLTableExporter;

/// <summary>
/// Represents the command-line options for the export process
/// </summary>
public class ExportOptions
{
    /// <summary>
    /// Whether to use snapshot isolation for database operations
    /// </summary>
    public bool SnapshotIsolation { get; set; } = false;

    /// <summary>
    /// Whether to archive the output directory after export
    /// </summary>
    public bool ArchiveOutput { get; set; } = false;

    /// <summary>
    /// The file path for the archive (if not specified, will use output directory name + .zip)
    /// </summary>
    public string? ArchivePath { get; set; } = null;

    /// <summary>
    /// Azure Blob Storage URL for uploading the export results
    /// </summary>
    public string? AzureBlobStorageUrl { get; set; } = null;

    /// <summary>
    /// The connection string to the Azure SQL Hyperscale database
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// The directory where the CSV files will be created
    /// </summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Optional WHERE condition to filter data
    /// </summary>
    internal string RawWhereCondition { get; set; } = string.Empty;

    /// <summary>
    /// Parsed WHERE condition with parameters
    /// </summary>
    public WhereCondition? ParsedWhereCondition { get; set; }

    /// <summary>
    /// WHERE condition parameter values
    /// </summary>
    public Dictionary<string, string> WhereParameters { get; set; } = [];

    /// <summary>
    /// Maximum number of rows per CSV file
    /// </summary>
    public int MaxRowsPerFile { get; set; } = 1_000_000;

    /// <summary>
    /// Number of rows to fetch in each database query batch
    /// </summary>
    public int QueryBatchSize { get; set; } = 5_000;

    /// <summary>
    /// Delay in milliseconds between query batches to reduce server load
    /// </summary>
    public int BatchDelayMilliseconds { get; set; } = 10;

    /// <summary>
    /// Schema name for the table to export
    /// </summary>
    public string SchemaName { get; set; } = string.Empty;

    /// <summary>
    /// Table name to export
    /// </summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Column(s) to order by when exporting data
    /// </summary>
    public string OrderByColumns { get; set; } = string.Empty;

    /// <summary>
    /// Prefix for output CSV files (defaults to table name if not specified)
    /// </summary>
    public string FilePrefix { get; set; } = string.Empty;

    /// <summary>
    /// Command timeout in seconds for database queries
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 3600;

    /// <summary>
    /// Whether to restart from the last saved progress
    /// </summary>
    public bool Restart { get; set; } = false;

    /// <summary>
    /// Whether to track export progress
    /// </summary>
    public bool TrackProgress { get; set; } = true;

    /// <summary>
    /// Whether to generate a SQL script file for the table schema after export
    /// </summary>
    public bool GenerateSchemaScript { get; set; } = true;

    /// <summary>
    /// Parses command-line arguments into an ExportOptions object
    /// </summary>
    public static ExportOptions? ParseArgs(string[] args)
    {
        ExportOptions options = new();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i].ToLower();
            string nextArg = i + 1 < args.Length ? args[i + 1] : string.Empty;

            switch (arg)
            {
                case "-c":
                case "--connection":
                    if (!string.IsNullOrEmpty(nextArg) && !nextArg.StartsWith('-'))
                    {
                        options.ConnectionString = args[++i];
                    }

                    break;

                case "-o":
                case "--output":
                    if (!string.IsNullOrEmpty(nextArg) && !nextArg.StartsWith('-'))
                    {
                        options.OutputDirectory = args[++i];
                    }

                    break;

                case "-r":
                case "--rows":
                    if (!string.IsNullOrEmpty(nextArg) && !nextArg.StartsWith('-') &&
                        int.TryParse(nextArg, out int maxRows) && maxRows > 0)
                    {
                        options.MaxRowsPerFile = maxRows;
                        i++;
                    }

                    break;

                case "-b":
                case "--batch-size":
                    if (!string.IsNullOrEmpty(nextArg) && !nextArg.StartsWith('-') &&
                        int.TryParse(nextArg, out int batchSize) && batchSize > 0)
                    {
                        options.QueryBatchSize = batchSize;
                        i++;
                    }

                    break;

                case "-d":
                case "--delay":
                    if (!string.IsNullOrEmpty(nextArg) && !nextArg.StartsWith('-') &&
                        int.TryParse(nextArg, out int delay) && delay >= 0)
                    {
                        options.BatchDelayMilliseconds = delay;
                        i++;
                    }

                    break;

                case "-s":
                case "--schema":
                    if (!string.IsNullOrEmpty(nextArg) && !nextArg.StartsWith('-'))
                    {
                        options.SchemaName = args[++i];
                    }

                    break;

                case "-t":
                case "--table":
                    if (!string.IsNullOrEmpty(nextArg) && !nextArg.StartsWith('-'))
                    {
                        options.TableName = args[++i];
                    }

                    break;

                case "--order-by":
                    if (!string.IsNullOrEmpty(nextArg) && !nextArg.StartsWith('-'))
                    {
                        options.OrderByColumns = args[++i];
                    }

                    break;

                case "-p":
                case "--prefix":
                    if (!string.IsNullOrEmpty(nextArg) && !nextArg.StartsWith('-'))
                    {
                        options.FilePrefix = args[++i];
                    }

                    break;

                case "--timeout":
                    if (!string.IsNullOrEmpty(nextArg) && !nextArg.StartsWith('-') &&
                        int.TryParse(nextArg, out int timeout) && timeout > 0)
                    {
                        options.CommandTimeoutSeconds = timeout;
                        i++;
                    }

                    break;

                case "--restart":
                    options.Restart = true;
                    break;

                case "--no-progress-tracking":
                    options.TrackProgress = false;
                    break;

                case "--no-schema-script":
                    options.GenerateSchemaScript = false;
                    break;

                case "--snapshot-isolation":
                    options.SnapshotIsolation = true;
                    break;

                case "--archive":
                    options.ArchiveOutput = true;
                    break;

                case "--archive-path":
                    if (!string.IsNullOrEmpty(nextArg) && !nextArg.StartsWith('-'))
                    {
                        options.ArchivePath = args[++i];
                    }

                    break;

                case "--azure-blob-storage":
                    if (!string.IsNullOrEmpty(nextArg) && !nextArg.StartsWith('-'))
                    {
                        options.AzureBlobStorageUrl = args[++i];
                    }

                    break;

                case "-w":
                case "--where":
                    if (!string.IsNullOrEmpty(nextArg) && !nextArg.StartsWith('-'))
                    {
                        options.RawWhereCondition = args[++i];
                    }

                    break;

                case "--param":
                    if (!string.IsNullOrEmpty(nextArg) && !nextArg.StartsWith('-'))
                    {
                        string paramArg = args[++i];
                        int equalsPos = paramArg.IndexOf('=');

                        if (equalsPos > 0 && equalsPos < paramArg.Length - 1)
                        {
                            string paramName = paramArg[..equalsPos];
                            string paramValue = paramArg[(equalsPos + 1)..];
                            options.WhereParameters[paramName] = paramValue;
                        }
                        else
                        {
                            Console.WriteLine($"Warning: Invalid parameter format: {paramArg}. Expected format: name=value");
                        }
                    }

                    break;

                // If no option flag, treat as output directory
                default:
                    if (!arg.StartsWith('-') && string.IsNullOrEmpty(options.OutputDirectory))
                    {
                        options.OutputDirectory = args[i];
                    }

                    break;
            }
        }

        // Validate schema and table are specified since we need them for default output directory
        if (string.IsNullOrEmpty(options.SchemaName) || string.IsNullOrEmpty(options.TableName))
        {
            // These will be validated later with proper error messages
            if (string.IsNullOrEmpty(options.OutputDirectory))
            {
                options.OutputDirectory = ".";
            }
        }
        else
        {
            // Set default output directory if not specified
            if (string.IsNullOrEmpty(options.OutputDirectory) || options.OutputDirectory == ".")
            {
                options.OutputDirectory = $"{options.SchemaName}_{options.TableName}_export";
                Console.WriteLine($"Using default output directory: {options.OutputDirectory}");
            }

            // Set default archive path if archiving is enabled but no path is specified
            if (options.ArchiveOutput && options.ArchivePath == null)
            {
                options.ArchivePath = $"{options.SchemaName}_{options.TableName}_export.zip";
            }
        }

        // Parse WHERE condition if provided
        if (!string.IsNullOrEmpty(options.RawWhereCondition))
        {
            options.ParsedWhereCondition = SQLTableExporter.WhereCondition.Parse(options.RawWhereCondition);

            // Check if we failed to parse due to missing parameters
            if (options.ParsedWhereCondition == null)
            {
                Console.WriteLine("Error: WHERE conditions must use parameters to prevent SQL injection.");
                Console.WriteLine("Example: -w \"OrderDate > :minDate\" --param minDate=2023-01-01");
                return null;
            }

            // Check if we have parameters that need values
            if (options.ParsedWhereCondition.Parameters.Count > 0 && options.WhereParameters.Count == 0)
            {
                Console.WriteLine("Error: WHERE condition has parameters but no values were provided.");
                Console.WriteLine("Please use --param name=value to specify values for each parameter in your WHERE condition.");
                return null;
            }

            // Add parameter values if they were provided
            foreach (KeyValuePair<string, string> param in options.WhereParameters)
            {
                try
                {
                    // Convert string values to appropriate types based on context
                    // For now, we're keeping it simple by just using the string value
                    options.ParsedWhereCondition.AddParameter(param.Key, param.Value);
                }
                catch (ArgumentException ex)
                {
                    Console.WriteLine($"Warning: {ex.Message}");
                }
            }
        }

        return options;
    }

    /// <summary>
    /// Prints usage information
    /// </summary>
    public static void PrintUsage()
    {
        Console.WriteLine("Usage: SQLTableExporter [OPTIONS] [OUTPUT_DIRECTORY]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -c, --connection <string>   Connection string to Azure SQL database");
        Console.WriteLine("  -o, --output <path>         Output directory for CSV files");
        Console.WriteLine("  -s, --schema <string>       Database schema name");
        Console.WriteLine("  -t, --table <string>        Table name to export");
        Console.WriteLine("  --order-by <string>         Column(s) to order by when exporting data");
        Console.WriteLine("  -p, --prefix <string>       Prefix for output CSV files (defaults to table name)");
        Console.WriteLine("  -r, --rows <int>            Maximum rows per CSV file (default: 1,000,000)");
        Console.WriteLine("  -b, --batch-size <int>      Number of rows to fetch per query (default: 5,000)");
        Console.WriteLine("  -d, --delay <int>           Delay in milliseconds between query batches (default: 10)");
        Console.WriteLine("  --timeout <int>             Command timeout in seconds (default: 3600)");
        Console.WriteLine("  --restart                   Restart from previous export progress");
        Console.WriteLine("  --no-progress-tracking      Disable progress tracking");
        Console.WriteLine("  --no-schema-script          Disable schema script generation");
        Console.WriteLine("  --snapshot-isolation        Use snapshot isolation for consistent view of data during export");
        Console.WriteLine("  --archive                   Archive the output directory after export");
        Console.WriteLine("  --archive-path <path>       Custom path for the archive file");
        Console.WriteLine("  --azure-blob-storage <url>  Azure Blob Storage URL for uploading export results");
        Console.WriteLine("  -w, --where <string>        Optional WHERE condition to filter data");
        Console.WriteLine("  --param <name=value>        Parameter value for WHERE condition");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  SQLTableExporter -s dbo -t Users --order-by \"Id\"");
        Console.WriteLine("  SQLTableExporter -s Earth -t PlayerCellStatistics --order-by \"Player\" -o ./exports");
        Console.WriteLine("  SQLTableExporter -c \"Server=myserver.database.windows.net;...\" -s dbo -t Products -o ./exports");
        Console.WriteLine("  SQLTableExporter -s dbo -t Orders --order-by \"OrderDate, OrderId\" --rows 500000 ./exports");
        Console.WriteLine("  SQLTableExporter -s dbo -t Customers -w \"Country='USA'\" -o ./exports");
        Console.WriteLine("  SQLTableExporter -s dbo -t Orders -w \"OrderDate > :minDate\" --param minDate=2023-01-01");
        Console.WriteLine("  SQLTableExporter -s dbo -t Products --azure-blob-storage \"https://mystorageaccount.blob.core.windows.net/mycontainer\"");
        Console.WriteLine("  SQLTableExporter -s dbo -t Orders --archive --azure-blob-storage \"https://mystorageaccount.blob.core.windows.net/mycontainer/exports\"");
    }
}
