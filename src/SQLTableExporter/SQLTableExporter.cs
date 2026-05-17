using Microsoft.Data.SqlClient;

namespace SQLTableExporter;

class Program
{
    static async Task<int> Main(string[] args)
    {
        Task<(Version Current, Version Latest)?> updateCheckTask = UpdateChecker.CheckAsync();
        try
        {
            Console.WriteLine("SQL Table Exporter");
            Console.WriteLine("------------------");

            // Parse command-line arguments
            ExportOptions? options = ExportOptions.ParseArgs(args);

            if (options == null)
            {
                // ExportOptions.ParseArgs already printed the error message
                return 1; // Return error code
            }

            // Check for connection string
            if (string.IsNullOrEmpty(options.ConnectionString))
            {
                Console.WriteLine("Warning: No connection string provided.");
                Console.WriteLine("Please set it before running the export:");
                Console.WriteLine("options.ConnectionString = \"Server=server.database.windows.net;Database=db;Authentication=Active Directory Default;\"");

                if (args.Length == 0)
                {
                    ExportOptions.PrintUsage();
                    return 1;
                }
            }

            // Ensure output directory exists
            if (!Directory.Exists(options.OutputDirectory))
            {
                Directory.CreateDirectory(options.OutputDirectory);
                Console.WriteLine($"Created output directory: {options.OutputDirectory}");
            }

            // Check if the directory is empty unless we're restarting
            else if (!options.Restart && Directory.EnumerateFileSystemEntries(options.OutputDirectory).Any())
            {
                Console.WriteLine($"Error: Output directory is not empty: {options.OutputDirectory}");
                Console.WriteLine("Please use an empty directory, or use --restart to continue a previous export.");
                return 1;
            }

            // Validate required options
            if (string.IsNullOrEmpty(options.SchemaName))
            {
                Console.WriteLine("Error: Schema name is required. Use -s or --schema to specify a schema.");
                ExportOptions.PrintUsage();
                return 1;
            }

            if (string.IsNullOrEmpty(options.TableName))
            {
                Console.WriteLine("Error: Table name is required. Use -t or --table to specify a table.");
                ExportOptions.PrintUsage();
                return 1;
            }

            // Set default file prefix if not specified
            if (string.IsNullOrEmpty(options.FilePrefix))
            {
                options.FilePrefix = options.TableName;
            }

            // If order by columns not specified, attempt to detect primary key
            bool orderByFromUser = !string.IsNullOrEmpty(options.OrderByColumns);
            if (!orderByFromUser)
            {
                Console.WriteLine("No order by columns specified. Attempting to detect primary key columns...");
                string primaryKeyColumns = await PrimaryKeyDetector.DetectPrimaryKeyColumnsAsync(
                    options.ConnectionString,
                    options.SchemaName,
                    options.TableName,
                    options.CommandTimeoutSeconds
                );

                if (!string.IsNullOrEmpty(primaryKeyColumns))
                {
                    options.OrderByColumns = primaryKeyColumns;
                    Console.WriteLine($"Using detected primary key columns for ordering: {primaryKeyColumns}");
                }
                else
                {
                    Console.WriteLine("Error: Could not detect primary key columns. Please specify order by columns using --order-by.");
                    ExportOptions.PrintUsage();
                    return 1;
                }
            }

            // Export the specified table
            string fullTableName = $"{options.SchemaName}.{options.TableName}";

            // Validate user-supplied order columns. PK auto-detection is unique+not-null by definition,
            // so only validate when the user passed --order-by themselves.
            if (orderByFromUser)
            {
                await using SqlConnection validationConnection = new(options.ConnectionString);
                await validationConnection.OpenAsync();
                List<KeysetPagination.OrderColumn> validationColumns = await KeysetPagination.GetOrderColumnsAsync(
                    validationConnection,
                    fullTableName,
                    options.OrderByColumns);
                if (validationColumns.Count > 0)
                {
                    List<string> orderByWarnings = await KeysetPagination.ValidateOrderColumnsAsync(
                        validationConnection,
                        fullTableName,
                        validationColumns);
                    foreach (string warning in orderByWarnings)
                    {
                        Console.WriteLine($"Warning: {warning}");
                    }
                }
            }

            Console.WriteLine($"\nExporting {fullTableName}...");

            // Display WHERE condition if specified
            if (!string.IsNullOrEmpty(options.RawWhereCondition))
            {
                // Show raw condition for user clarity
                Console.WriteLine($"Applying WHERE condition: {options.RawWhereCondition}");

                // Show additional info for parameterized conditions
                if (options.ParsedWhereCondition != null && options.ParsedWhereCondition.Parameters.Count > 0)
                {
                    Console.WriteLine("Using parameters:");
                    foreach (KeyValuePair<string, string> param in options.WhereParameters)
                    {
                        Console.WriteLine($"  {param.Key} = {param.Value}");
                    }
                }
            }

            // Initialize a single progress data object that will be used throughout the application
            ExportProgressData progressData;

            // Load progress from disk if restart is requested
            if (options.Restart)
            {
                progressData = ExportProgressData.LoadFromDisk(
                    options.OutputDirectory,
                    options.SchemaName,
                    options.TableName,
                    options.OrderByColumns,
                    options.FilePrefix) ?? new ExportProgressData
                    {
                        Schema = options.SchemaName,
                        Table = options.TableName,
                        OrderByColumns = options.OrderByColumns,
                        FilePrefix = options.FilePrefix,
                        MaxRowsPerFile = options.MaxRowsPerFile
                    };

                if (progressData.TotalRows > 0)
                {
                    Console.WriteLine($"Found existing progress data for {options.SchemaName}.{options.TableName}");
                    Console.WriteLine($"Last completed file: {progressData.FilePrefix}_{progressData.LastFileNumber}.csv");
                    Console.WriteLine($"Processed rows: {progressData.ProcessedRows:N0} of {progressData.TotalRows:N0}");
                    Console.WriteLine($"Export timestamp: {progressData.Timestamp:yyyy-MM-dd HH:mm:ss} UTC");
                }
            }
            else
            {
                // Create a new progress data object for a fresh export
                progressData = new ExportProgressData
                {
                    Schema = options.SchemaName,
                    Table = options.TableName,
                    OrderByColumns = options.OrderByColumns,
                    FilePrefix = options.FilePrefix,
                    MaxRowsPerFile = options.MaxRowsPerFile
                };
            }

            // Define operation flags based on progress data
            bool needsExport = !progressData.ExportCompleted;
            bool needsArchive = options.ArchiveOutput && !progressData.ArchiveCompleted;
            bool needsUpload = !string.IsNullOrEmpty(options.AzureBlobStorageUrl) && !progressData.UploadCompleted;

            // Check if we have an existing archive path
            string? existingArchivePath = progressData.ArchivePath;

            // If existing archive path is set but file doesn't exist, we need to re-archive
            if (needsArchive && !string.IsNullOrEmpty(existingArchivePath) && !File.Exists(existingArchivePath))
            {
                Console.WriteLine($"Previously archived file not found: {existingArchivePath}. Will re-create archive.");
            }

            // If resuming after successful export but output directory is empty, something is wrong
            if (!needsExport && needsArchive && options.ArchiveOutput)
            {
                if (!Directory.Exists(options.OutputDirectory) ||
                    !Directory.EnumerateFileSystemEntries(options.OutputDirectory).Any())
                {
                    Console.WriteLine("Warning: Output directory is missing or empty, but export is marked as complete.");
                    Console.WriteLine("Re-running the export phase to ensure data integrity.");
                    needsExport = true;
                    progressData.ExportCompleted = false;
                }
            }

            Console.WriteLine("Operation plan:");
            Console.WriteLine($"  Export: {(needsExport ? "Needed" : "Already completed")}");
            Console.WriteLine($"  Archive: {(needsArchive ? "Needed" : "Already completed or not requested")}");
            Console.WriteLine($"  Upload: {(needsUpload ? "Needed" : "Already completed or not requested")}");

            // Display snapshot isolation message if enabled
            if (options.SnapshotIsolation)
            {
                Console.WriteLine("\nSnapshot isolation enabled - this will provide a consistent view of the data");
                Console.WriteLine("as it exists at the start of each database operation, ensuring consistency during export.");
            }

            // Create the exporter and pass the progress data
            TableDataExporter exporter = new(options);

            // Perform export if needed
            if (needsExport)
            {
                try
                {
                    await exporter.ExportTableAsync(options.SchemaName, options.TableName, options.OrderByColumns, options.FilePrefix, progressData);
                    Console.WriteLine("\nExport completed successfully!");

                    // Mark export as completed only if we reach this point
                    progressData.ExportCompleted = true;
                    progressData.SaveToDisk(options.OutputDirectory);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\nExport phase failed: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($"Details: {ex.InnerException.Message}");
                    }

                    progressData.ExportCompleted = false;
                    progressData.SaveToDisk(options.OutputDirectory);
                    throw new InvalidOperationException("Export phase failed. See error details above.", ex);
                }
            }
            else
            {
                Console.WriteLine("\nSkipping export phase as it was already completed.");
            }

            // Handle archive and Azure Blob Storage upload options
            string? archivePath = existingArchivePath;

            // Check if archiving is needed
            if (needsArchive && options.ArchiveOutput)
            {
                try
                {
                    // Validate that the output directory exists and is not empty
                    if (options.OutputDirectory == ".")
                    {
                        throw new InvalidOperationException("Cannot archive - using current directory as output. Please specify an explicit output directory with -o or --output.");
                    }
                    else if (!Directory.Exists(options.OutputDirectory))
                    {
                        throw new InvalidOperationException($"Cannot archive - output directory does not exist: {options.OutputDirectory}");
                    }
                    else if (!Directory.EnumerateFileSystemEntries(options.OutputDirectory).Any())
                    {
                        throw new InvalidOperationException($"Cannot archive - output directory is empty: {options.OutputDirectory}");
                    }
                    else
                    {
                        Console.WriteLine("\nArchiving output directory...");

                        // Default archive path if not specified
                        string defaultArchivePath = $"{options.SchemaName}_{options.TableName}_export.zip";
                        archivePath = await Archiver.ArchiveDirectoryAsync(
                            options.OutputDirectory,
                            options.ArchivePath ?? defaultArchivePath);

                        Console.WriteLine($"Archive created successfully: {archivePath}");

                        // Update progress with archive info
                        progressData.ArchiveCompleted = true;
                        progressData.ArchivePath = archivePath;
                        progressData.SaveToDisk(options.OutputDirectory);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\nArchive phase failed: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($"Details: {ex.InnerException.Message}");
                    }

                    progressData.ArchiveCompleted = false;
                    progressData.SaveToDisk(options.OutputDirectory);
                    throw new InvalidOperationException("Archive phase failed. See error details above.", ex);
                }
            }
            else if (options.ArchiveOutput)
            {
                Console.WriteLine("\nSkipping archive phase as it was already completed.");
            }

            // Check if Azure Blob Storage upload is needed
            if (needsUpload && !string.IsNullOrEmpty(options.AzureBlobStorageUrl))
            {
                try
                {
                    Console.WriteLine("\nPreparing to upload to Azure Blob Storage...");
                    AzureBlobStorageUploader uploader = new(options.AzureBlobStorageUrl);

                    // If archive is enabled, upload only the archive file
                    if (options.ArchiveOutput && !string.IsNullOrEmpty(archivePath))
                    {
                        Console.WriteLine($"Uploading archive file to Azure Blob Storage...");
                        string blobUrl = await uploader.UploadFileAsync(archivePath, null);
                        Console.WriteLine($"Archive successfully uploaded to: {blobUrl}");
                    }
                    // Otherwise upload the entire output directory
                    else
                    {
                        Console.WriteLine($"Uploading output directory to Azure Blob Storage...");
                        int filesUploaded = await uploader.UploadDirectoryAsync(options.OutputDirectory, true);
                        Console.WriteLine($"Successfully uploaded {filesUploaded} files to Azure Blob Storage");
                    }

                    // Update progress with upload info
                    progressData.UploadCompleted = true;
                    progressData.SaveToDisk(options.OutputDirectory);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\nError uploading to Azure Blob Storage: {ex.Message}");

                    // Show the inner exception details if available for more context
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($"Details: {ex.InnerException.Message}");
                    }

                    progressData.UploadCompleted = false;
                    progressData.SaveToDisk(options.OutputDirectory);

                    // Fail the process since upload was requested but failed
                    throw new InvalidOperationException("Azure Blob Storage upload failed. See error details above.", ex);
                }
            }
            else if (!string.IsNullOrEmpty(options.AzureBlobStorageUrl))
            {
                Console.WriteLine("\nSkipping upload phase as it was already completed.");
            }

            await UpdateChecker.PrintBannerIfAvailable(updateCheckTask);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nError: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            await UpdateChecker.PrintBannerIfAvailable(updateCheckTask);
            return 1;
        }
    }
}
