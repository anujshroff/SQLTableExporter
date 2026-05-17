using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Data.SqlClient;
using System.Data.SqlTypes;
using System.Globalization;

namespace SQLTableExporter;

/// <summary>
/// Handles the data export process from SQL Server to CSV files
/// </summary>
public class TableDataExporter(ExportOptions options)
{
    private readonly string _connectionString = options.ConnectionString;
    private readonly string _outputDirectory = options.OutputDirectory;
    private readonly int _maxRowsPerFile = options.MaxRowsPerFile;
    private readonly int _queryBatchSize = Math.Min(options.QueryBatchSize, options.MaxRowsPerFile);
    private readonly int _commandTimeout = options.CommandTimeoutSeconds;
    private readonly int _batchDelay = options.BatchDelayMilliseconds;
    private readonly bool _restart = options.Restart;
    private readonly bool _trackProgress = options.TrackProgress;
    private readonly bool _generateSchemaScript = options.GenerateSchemaScript;
    private readonly bool _snapshotIsolation = options.SnapshotIsolation;
    private readonly WhereCondition? _whereCondition = options.ParsedWhereCondition;
    private readonly string _rawWhereCondition = options.RawWhereCondition;
    private readonly SchemaScriptGenerator _schemaScriptGenerator = new(options.ConnectionString, options.CommandTimeoutSeconds);

    public async Task ExportTableAsync(string schema, string table, string orderByColumns, string filePrefix, ExportProgressData progressData)
    {
        string fullTableName = $"{schema}.{table}";

        // Use progress data to determine start position
        int startOffset = _restart ? progressData.ProcessedRows : 0;
        int startFileNumber = _restart ? progressData.LastFileNumber + 1 : 1;

        // Display resume information if restarting
        if (_restart && startOffset > 0)
        {
            Console.WriteLine($"Resuming export from offset {startOffset:N0}, file number {startFileNumber}");
        }

        await ExportTableAsync(fullTableName, orderByColumns, filePrefix, startOffset, startFileNumber, progressData);

        // Generate and save schema script if enabled
        if (_generateSchemaScript)
        {
            try
            {
                Console.WriteLine("\nGenerating table schema script...");
                string scriptPath = await _schemaScriptGenerator.SaveSchemaScriptAsync(
                    schema,
                    table,
                    _outputDirectory,
                    filePrefix);

                Console.WriteLine($"Schema script saved to: {scriptPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to generate schema script: {ex.Message}");
                // Continue execution - unlike archiving, schema script generation failure should not fail the export
            }
        }
    }

    private async Task ExportTableAsync(string tableName, string orderByColumn, string filePrefix,
        int startOffset = 0, int startFileNumber = 1, ExportProgressData? progressData = null)
    {
        // Ensure we have a progress data object
        progressData ??= new ExportProgressData();

        // Extract schema and table name from the full table name
        string[] parts = tableName.Split('.');
        string schema = parts[0];
        string table = parts[1];

        // Create a single connection and transaction for snapshot isolation if enabled
        SqlConnection? sharedConnection = null;
        SqlTransaction? sharedTransaction = null;

        if (_snapshotIsolation)
        {
            Console.WriteLine("Using snapshot isolation for consistent reads");
            sharedConnection = new SqlConnection(_connectionString);
            await sharedConnection.OpenAsync();
            sharedTransaction = sharedConnection.BeginTransaction(System.Data.IsolationLevel.Snapshot);
        }

        try
        {
            // Get total row count
            int totalRows = await GetTableRowCountAsync(tableName, sharedConnection, sharedTransaction);
            Console.WriteLine($"Total rows in {tableName}: {totalRows:N0}");

            // Update progress data with total rows
            progressData.TotalRows = totalRows;

            ProgressReporter progress = new()
            {
                TotalRows = totalRows,
                ProcessedRows = startOffset,
                StartTime = DateTime.Now
            };

            int processedRowsCount = startOffset;
            int fileNumber = startFileNumber;
            int rowsInCurrentFile = 0;

            // Set up keyset pagination
            await using SqlConnection columnInfoConnection = new(_connectionString);
            await columnInfoConnection.OpenAsync();

            // Get order column information
            List<KeysetPagination.OrderColumn> orderColumns = await KeysetPagination.GetOrderColumnsAsync(
                columnInfoConnection,
                tableName,
                orderByColumn);

            if (orderColumns.Count == 0)
            {
                throw new InvalidOperationException("Could not get order column information. Check if the specified order columns exist.");
            }

            // Build the SELECT column projection so CLR UDT columns (hierarchyid,
            // geography, geometry) come back as text instead of binary that the driver
            // can't deserialize without Microsoft.SqlServer.Types.
            string selectColumns = await KeysetPagination.GetSelectColumnListAsync(
                columnInfoConnection,
                tableName);

            Console.WriteLine($"Using keyset pagination with order columns: {string.Join(", ", orderColumns.Select(c => $"{c.Name} {c.Direction}"))}");

            // Initialize for keyset pagination - non-first batch if resuming with saved column values
            bool isFirstBatch = true;

            // Use saved order column values if available for restart
            if (_restart && progressData.LastOrderColumnValues.Count > 0)
            {
                Console.WriteLine("Applying saved order column values for restart:");

                foreach (OrderColumnValue savedColumn in progressData.LastOrderColumnValues)
                {
                    // Find the matching column in our current orderColumns list
                    KeysetPagination.OrderColumn? column = orderColumns.FirstOrDefault(c =>
                        c.Name.Equals(savedColumn.Name, StringComparison.OrdinalIgnoreCase));

                    if (column != null)
                    {
                        if (savedColumn.IsNull)
                        {
                            column.Value = null;
                            Console.WriteLine($"  Column {column.Name}: NULL");
                        }
                        else if (!string.IsNullOrEmpty(savedColumn.ValueString))
                        {
                            // Convert string value back to the appropriate type
                            if (column.IsDateTimeType || column.IsDateTime2Type)
                            {
                                if (DateTime.TryParse(savedColumn.ValueString, out DateTime dateValue))
                                {
                                    column.Value = dateValue;
                                    Console.WriteLine($"  Column {column.Name}: {dateValue:yyyy-MM-dd HH:mm:ss.fffffff}");
                                }
                            }
                            else if (column.Type == typeof(int) && int.TryParse(savedColumn.ValueString, out int intValue))
                            {
                                column.Value = intValue;
                                Console.WriteLine($"  Column {column.Name}: {intValue}");
                            }
                            else if (column.Type == typeof(long) && long.TryParse(savedColumn.ValueString, out long longValue))
                            {
                                column.Value = longValue;
                                Console.WriteLine($"  Column {column.Name}: {longValue}");
                            }
                            else if ((column.SqlType.Equals("decimal", StringComparison.OrdinalIgnoreCase) ||
                                      column.SqlType.Equals("numeric", StringComparison.OrdinalIgnoreCase)) &&
                                     TryParseSqlDecimal(savedColumn.ValueString, out SqlDecimal sqlDecimalValue))
                            {
                                // High-precision decimal/numeric (up to 38 digits) — restore via SqlDecimal
                                // so values past .NET decimal range survive a restart.
                                column.Value = sqlDecimalValue;
                                Console.WriteLine($"  Column {column.Name}: {sqlDecimalValue}");
                            }
                            else if (column.Type == typeof(decimal) && decimal.TryParse(savedColumn.ValueString, out decimal decimalValue))
                            {
                                column.Value = decimalValue;
                                Console.WriteLine($"  Column {column.Name}: {decimalValue}");
                            }
                            else if (column.Type == typeof(Guid) && Guid.TryParse(savedColumn.ValueString, out Guid guidValue))
                            {
                                column.Value = guidValue;
                                Console.WriteLine($"  Column {column.Name}: {guidValue}");
                            }
                            else
                            {
                                // Use string value as-is for other types
                                column.Value = savedColumn.ValueString;
                                Console.WriteLine($"  Column {column.Name}: {savedColumn.ValueString}");
                            }
                        }
                    }
                }

                // Since we have values from a previous run, we're not on the first batch
                isFirstBatch = false;
                Console.WriteLine("Using saved order column values to resume export from last position");
            }

            StreamWriter? writer = null;
            CsvWriter? csv = null;
            string currentOutputFile = "";

            // Process through all rows
            while (processedRowsCount < totalRows)
            {
                try
                {
                    // If we need to start a new file
                    if (writer == null || rowsInCurrentFile >= _maxRowsPerFile)
                    {
                        // Close previous file if open
                        if (writer != null)
                        {
                            // Make sure to dispose of writers properly
                            try
                            {
                                await csv!.FlushAsync();
                                await writer.FlushAsync();
                            }
                            finally
                            {
                                // Always dispose even if flush fails
                                await csv!.DisposeAsync();
                                await writer.DisposeAsync();
                                csv = null;
                                writer = null;
                            }

                            Console.WriteLine($"\nCompleted file with {rowsInCurrentFile:N0} rows");

                            // Save progress data after each file
                            if (_trackProgress)
                            {
                                // Update progress with current information
                                progressData.LastFileNumber = fileNumber;
                                progressData.ProcessedRows = processedRowsCount;
                                progressData.MaxRowsPerFile = _maxRowsPerFile;
                                progressData.UpdateOrderColumnValues(orderColumns);
                                progressData.SaveToDisk(_outputDirectory);
                            }

                            fileNumber++;
                            rowsInCurrentFile = 0;
                        }

                        // Start new file
                        currentOutputFile = Path.Combine(_outputDirectory, $"{filePrefix}_{fileNumber}.csv");
                        Console.WriteLine($"Creating new file: {currentOutputFile}");

                        writer = new StreamWriter(currentOutputFile);
                        csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
                        {
                            Delimiter = ",",
                            // Use standard CSV quoting rules to handle fields containing commas
                            ShouldQuote = args =>
                                args.Field != null &&
                                (args.Field.Contains(',') || args.Field.Contains('"') ||
                                 args.Field.Contains('\n') || args.Field.Contains('\r') ||
                                 args.Field.Contains('\0'))
                        });

                        // Get schema to write headers
                        await using SqlConnection schemaConnection = new(_connectionString);
                        await schemaConnection.OpenAsync();
                        await using SqlCommand schemaCommand = new($"SELECT TOP 0 {selectColumns} FROM {tableName}", schemaConnection)
                        {
                            CommandTimeout = _commandTimeout
                        };
                        await using SqlDataReader schemaReader = await schemaCommand.ExecuteReaderAsync();

                        // Write headers
                        for (int i = 0; i < schemaReader.FieldCount; i++)
                        {
                            csv.WriteField(schemaReader.GetName(i));
                        }

                        await csv.NextRecordAsync();
                    }

                    // Generate SQL and parameters for the query
                    Dictionary<string, object> queryParameters = [];
                    string whereClauseSql = string.Empty;

                    // Handle parameterized where condition if available
                    if (_whereCondition != null && _whereCondition.HasCondition)
                    {
                        // Generate unique parameter names to avoid collision with keyset parameters
                        whereClauseSql = _whereCondition.GetSqlWithPrefix(p => $"{p}_query");

                        // Get where condition parameters with adjusted names
                        Dictionary<string, object> whereParams = _whereCondition.GetParametersWithPrefix(p => $"{p}_query");

                        // Add where parameters to the query parameters
                        foreach (KeyValuePair<string, object> param in whereParams)
                        {
                            queryParameters.Add(param.Key, param.Value);
                        }
                    }

                    // Build keyset pagination query with WHERE condition
                    (string query, Dictionary<string, object> keysetParameters) = KeysetPagination.BuildKeysetQuery(
                        tableName,
                        orderColumns,
                        _queryBatchSize,
                        isFirstBatch,
                        whereClauseSql,
                        selectColumns);

                    // Merge keyset parameters with where condition parameters
                    foreach (KeyValuePair<string, object> param in keysetParameters)
                    {
                        queryParameters.Add(param.Key, param.Value);
                    }

                    // Execute query with combined parameters
                    int batchRowsProcessed = await ProcessKeysetBatchAsync(
                        query,
                        queryParameters,
                        csv!,
                        writer!,
                        rowsInCurrentFile,
                        orderColumns,
                        sharedConnection,
                        sharedTransaction);

                    // Update first batch flag
                    isFirstBatch = false;

                    // Update counters
                    rowsInCurrentFile += batchRowsProcessed;
                    processedRowsCount += batchRowsProcessed;

                    // Update progress
                    progress.ProcessedRows = processedRowsCount;
                    progress.CurrentFileNumber = fileNumber;

                    // Also update the shared progress data
                    progressData.ProcessedRows = processedRowsCount;

                    Console.WriteLine(progress.GetProgressReport());

                    // If we got fewer rows than requested, we're at the end
                    if (batchRowsProcessed < _queryBatchSize)
                    {
                        break;
                    }

                    // Add a delay between query batches to reduce server load
                    await Task.Delay(_batchDelay);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\nError during export: {ex.Message}");
                    throw;
                }
            }

            // Make sure to close the last file
            if (csv != null && writer != null)
            {
                try
                {
                    await csv.FlushAsync();
                    await writer.FlushAsync();

                    Console.WriteLine($"\nCompleted final file with {rowsInCurrentFile:N0} rows");

                    // Save final progress data and mark export as completed
                    if (_trackProgress)
                    {
                        // Update and save final state
                        progressData.ProcessedRows = totalRows; // Set to total rows to indicate completion
                        progressData.LastFileNumber = fileNumber;
                        progressData.UpdateOrderColumnValues(orderColumns);
                        progressData.ExportCompleted = true;
                        progressData.SaveToDisk(_outputDirectory);

                        Console.WriteLine("Export completion status saved to progress file");
                    }
                }
                finally
                {
                    await csv.DisposeAsync();
                    await writer.DisposeAsync();
                }
            }
        }
        finally
        {
            // Clean up shared connection and transaction if they were created
            if (sharedTransaction != null)
            {
                // Explicitly rollback the transaction since we're only using it for reads
                try
                {
                    sharedTransaction.Rollback();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Error rolling back transaction: {ex.Message}");
                }
                finally
                {
                    sharedTransaction.Dispose();
                }
            }

            if (sharedConnection != null)
            {
                await sharedConnection.DisposeAsync();
            }
        }
    }

    private async Task<int> ProcessKeysetBatchAsync(
        string query,
        Dictionary<string, object> parameters,
        CsvWriter csv,
        StreamWriter writer,
        int currentRowCount,
        List<KeysetPagination.OrderColumn> orderColumns,
        SqlConnection? sharedConnection = null,
        SqlTransaction? sharedTransaction = null)
    {
        int batchRowCount = 0;
        int maxRowsToProcess = _maxRowsPerFile - currentRowCount;

        // If snapshot isolation is enabled, use the shared connection and transaction
        SqlConnection? connectionToUse = sharedConnection;
        bool usingSharedConnection = connectionToUse != null;
        SqlConnection? localConnection = null;

        try
        {
            // If not using a shared connection, create a new one
            if (!usingSharedConnection)
            {
                localConnection = new SqlConnection(_connectionString);
                await localConnection.OpenAsync();
                connectionToUse = localConnection;
            }

            await using SqlCommand command = new(query, connectionToUse)
            {
                CommandTimeout = _commandTimeout
            };

            // Set the transaction if provided
            if (sharedTransaction != null)
            {
                command.Transaction = sharedTransaction;
            }

            // Add parameters
            foreach (KeyValuePair<string, object> param in parameters)
            {
                command.Parameters.AddWithValue(param.Key, param.Value);
            }

            // Keep track of the last row for keyset pagination
            int rowsRead = 0;

            await using SqlDataReader reader = await command.ExecuteReaderAsync();

            // SQL decimal/numeric can carry up to 38 digits of precision, but .NET decimal
            // overflows above ~7.92e28. Route those columns through GetSqlDecimal to avoid
            // OverflowException at GetValue time.
            bool[] isSqlDecimalCol = new bool[reader.FieldCount];
            for (int i = 0; i < reader.FieldCount; i++)
            {
                string typeName = reader.GetDataTypeName(i);
                isSqlDecimalCol[i] = typeName.Equals("decimal", StringComparison.OrdinalIgnoreCase)
                                  || typeName.Equals("numeric", StringComparison.OrdinalIgnoreCase);
            }

            // Process rows until we hit the end of the reader or the file size limit
            while (await reader.ReadAsync() && batchRowCount < maxRowsToProcess)
            {
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    if (reader.IsDBNull(i))
                    {
                        csv.WriteField(string.Empty);
                    }
                    else if (isSqlDecimalCol[i])
                    {
                        csv.WriteField(reader.GetSqlDecimal(i).ToString());
                    }
                    else
                    {
                        csv.WriteField(FormatValue(reader.GetValue(i)));
                    }
                }

                await csv.NextRecordAsync();
                batchRowCount++;
                rowsRead++;

                // Update the last row values for keyset pagination
                KeysetPagination.UpdateOrderColumnValues(reader, orderColumns);

                // Provide more granular progress updates
                if (batchRowCount % 1_000 == 0)
                {
                    Console.Write($"\rRows processed in current batch: {batchRowCount:N0} ");
                    await csv.FlushAsync();
                    await writer.FlushAsync();
                }
            }

            if (batchRowCount > 0)
            {
                Console.Write($"\rRows processed in current batch: {batchRowCount:N0} ");
            }

            return batchRowCount;
        }
        finally
        {
            // Dispose local connection if we created one
            if (localConnection != null)
            {
                await localConnection.DisposeAsync();
            }
        }
    }

    private static bool TryParseSqlDecimal(string? input, out SqlDecimal value)
    {
        if (input is null)
        {
            value = SqlDecimal.Null;
            return false;
        }

        try
        {
            value = SqlDecimal.Parse(input);
            return true;
        }
        catch
        {
            value = SqlDecimal.Null;
            return false;
        }
    }

    private static string FormatValue(object value) => value switch
    {
        byte[] bytes => "0x" + Convert.ToHexString(bytes),
        bool b => b ? "1" : "0",
        DateTime dt => dt.ToString("o", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("o", CultureInfo.InvariantCulture),
        TimeSpan ts => ts.ToString("c", CultureInfo.InvariantCulture),
        float f => f.ToString("G9", CultureInfo.InvariantCulture),
        double d => d.ToString("G17", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    private async Task<int> GetTableRowCountAsync(string tableName, SqlConnection? sharedConnection = null, SqlTransaction? sharedTransaction = null)
    {
        // If snapshot isolation is enabled, use the shared connection and transaction
        SqlConnection? connectionToUse = sharedConnection;
        bool usingSharedConnection = connectionToUse != null;
        SqlConnection? localConnection = null;

        try
        {
            // If not using a shared connection, create a new one
            if (!usingSharedConnection)
            {
                localConnection = new SqlConnection(_connectionString);
                await localConnection.OpenAsync();
                connectionToUse = localConnection;
            }

            string query = $"SELECT COUNT(1) FROM {tableName}";
            Dictionary<string, object> parameters = [];

            // Apply parameterized WHERE condition if available
            if (_whereCondition != null && _whereCondition.HasCondition)
            {
                query += $" WHERE {_whereCondition.Sql}";

                // Add parameters from the where condition
                foreach (KeyValuePair<string, object> param in _whereCondition.Parameters)
                {
                    parameters.Add(param.Key, param.Value);
                }
            }

            await using SqlCommand command = new(query, connectionToUse)
            {
                CommandTimeout = _commandTimeout // Use custom timeout for count query as well
            };

            // Set the transaction if provided
            if (sharedTransaction != null)
            {
                command.Transaction = sharedTransaction;
            }

            // Add parameters if any
            foreach (KeyValuePair<string, object> param in parameters)
            {
                command.Parameters.AddWithValue(param.Key, param.Value);
            }

            object? result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
        finally
        {
            // Dispose local connection if we created one
            if (localConnection != null)
            {
                await localConnection.DisposeAsync();
            }
        }
    }
}
