using Microsoft.Data.SqlClient;

namespace SQLTableExporter;

/// <summary>
/// Utility class for detecting primary key columns in SQL Server tables
/// </summary>
public static class PrimaryKeyDetector
{
    /// <summary>
    /// Detects primary key columns for a table in SQL Server
    /// </summary>
    /// <param name="connectionString">Connection string to SQL Server</param>
    /// <param name="schema">Schema name</param>
    /// <param name="table">Table name</param>
    /// <param name="timeout">Command timeout in seconds</param>
    /// <returns>Comma-separated list of primary key columns or empty string if no primary key</returns>
    public static async Task<string> DetectPrimaryKeyColumnsAsync(
        string connectionString,
        string schema,
        string table,
        int timeout = 30)
    {
        if (string.IsNullOrEmpty(connectionString) ||
            string.IsNullOrEmpty(schema) ||
            string.IsNullOrEmpty(table))
        {
            return string.Empty;
        }

        try
        {
            await using SqlConnection connection = new(connectionString);
            await connection.OpenAsync();

            // Query to get primary key columns in order of key_ordinal
            string query = @"
                SELECT c.name AS column_name
                FROM sys.indexes i
                INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
                INNER JOIN sys.tables t ON i.object_id = t.object_id
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE i.is_primary_key = 1
                AND s.name = @schema
                AND t.name = @table
                ORDER BY ic.key_ordinal;
            ";

            await using SqlCommand command = new(query, connection)
            {
                CommandTimeout = timeout
            };

            command.Parameters.AddWithValue("@schema", schema);
            command.Parameters.AddWithValue("@table", table);

            List<string> pkColumns = [];
            await using (SqlDataReader reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    pkColumns.Add(reader.GetString(0));
                }
            }

            if (pkColumns.Count == 0)
            {
                // If no primary key, as a fallback check for a single identity column
                await using SqlCommand identityCommand = new(@"
                    SELECT c.name
                    FROM sys.columns c
                    INNER JOIN sys.tables t ON c.object_id = t.object_id
                    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                    WHERE c.is_identity = 1
                    AND s.name = @schema
                    AND t.name = @table
                ", connection)
                {
                    CommandTimeout = timeout
                };

                identityCommand.Parameters.AddWithValue("@schema", schema);
                identityCommand.Parameters.AddWithValue("@table", table);

                await using SqlDataReader identityReader = await identityCommand.ExecuteReaderAsync();

                if (await identityReader.ReadAsync())
                {
                    pkColumns.Add(identityReader.GetString(0));
                }
            }

            return string.Join(", ", pkColumns);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error detecting primary key columns: {ex.Message}");
            return string.Empty;
        }
    }
}
