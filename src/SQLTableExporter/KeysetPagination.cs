using Microsoft.Data.SqlClient;
using System.Text;

namespace SQLTableExporter;

/// <summary>
/// Utility class for handling keyset pagination with SQL Server
/// </summary>
public class KeysetPagination
{
    /// <summary>
    /// Column definition with name, type, and value for keyset pagination
    /// </summary>
    public class OrderColumn
    {
        /// <summary>
        /// Column name
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// SQL type name
        /// </summary>
        public string SqlType { get; set; } = string.Empty;

        /// <summary>
        /// .NET Type
        /// </summary>
        public Type? Type { get; set; }

        /// <summary>
        /// Direction (ASC or DESC)
        /// </summary>
        public string Direction { get; set; } = "ASC";

        /// <summary>
        /// Current value for the column
        /// </summary>
        public object? Value { get; set; }

        /// <summary>
        /// Whether the column allows NULL values
        /// </summary>
        public bool IsNullable { get; set; }

        /// <summary>
        /// Whether this is a standard SQL datetime type (has range limitations)
        /// </summary>
        public bool IsDateTimeType =>
            SqlType.Equals("datetime", StringComparison.OrdinalIgnoreCase) ||
            SqlType.Equals("smalldatetime", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whether this is a datetime2 type (extended range)
        /// </summary>
        public bool IsDateTime2Type =>
            SqlType.Equals("datetime2", StringComparison.OrdinalIgnoreCase) ||
            SqlType.Equals("date", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parses order by columns string into list of column names and directions
    /// </summary>
    /// <param name="orderByColumns">Order by columns string (e.g. "Col1 ASC, Col2 DESC")</param>
    /// <returns>List of column names with their directions</returns>
    public static List<(string Name, string Direction)> ParseOrderByColumns(string orderByColumns)
    {
        List<(string, string)> result = [];

        if (string.IsNullOrEmpty(orderByColumns))
        {
            return result;
        }

        string[] columns = orderByColumns.Split(',');

        foreach (string column in columns)
        {
            string trimmedColumn = column.Trim();

            if (trimmedColumn.EndsWith(" ASC", StringComparison.OrdinalIgnoreCase))
            {
                string name = trimmedColumn[..^4].Trim();
                result.Add((name, "ASC"));
            }
            else if (trimmedColumn.EndsWith(" DESC", StringComparison.OrdinalIgnoreCase))
            {
                string name = trimmedColumn[..^5].Trim();
                result.Add((name, "DESC"));
            }
            else
            {
                // Default to ASC if no direction specified
                result.Add((trimmedColumn, "ASC"));
            }
        }

        return result;
    }

    /// <summary>
    /// Gets column information for order by columns
    /// </summary>
    /// <param name="connection">SQL connection</param>
    /// <param name="tableName">Full table name (schema.table)</param>
    /// <param name="orderByColumns">Order by columns string</param>
    /// <returns>List of order columns with their SQL types and .NET types</returns>
    public static async Task<List<OrderColumn>> GetOrderColumnsAsync(
        SqlConnection connection,
        string tableName,
        string orderByColumns)
    {
        List<(string Name, string Direction)> parsedColumns = ParseOrderByColumns(orderByColumns);
        List<OrderColumn> result = [];

        if (parsedColumns.Count == 0)
        {
            return result;
        }

        // Create a set of column names for faster lookup
        HashSet<string> columnNames = new(parsedColumns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);

        // Query to get column information
        string query = $@"
            SELECT 
                c.name AS ColumnName, 
                t.name AS TypeName,
                c.is_nullable AS IsNullable
            FROM sys.columns c
            INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
            INNER JOIN sys.tables tab ON c.object_id = tab.object_id
            INNER JOIN sys.schemas s ON tab.schema_id = s.schema_id
            WHERE CONCAT(s.name, '.', tab.name) = @tableName
            AND c.name IN ({string.Join(", ", columnNames.Select((_, i) => $"@p{i}"))})
        ";

        await using SqlCommand command = new(query, connection);
        command.Parameters.AddWithValue("@tableName", tableName);

        int index = 0;
        foreach (string columnName in columnNames)
        {
            command.Parameters.AddWithValue($"@p{index}", columnName);
            index++;
        }

        await using SqlDataReader reader = await command.ExecuteReaderAsync();

        // Dictionary to store column information
        Dictionary<string, (string TypeName, bool IsNullable)> columnInfo = new(StringComparer.OrdinalIgnoreCase);

        while (await reader.ReadAsync())
        {
            string columnName = reader.GetString(0);
            string typeName = reader.GetString(1);
            bool isNullable = reader.GetBoolean(2);

            columnInfo[columnName] = (typeName, isNullable);
        }

        // Create OrderColumn objects for each column in the order specified
        foreach ((string name, string direction) in parsedColumns)
        {
            if (columnInfo.TryGetValue(name, out (string TypeName, bool IsNullable) info))
            {
                OrderColumn column = new()
                {
                    Name = name,
                    SqlType = info.TypeName,
                    Direction = direction,
                    IsNullable = info.IsNullable,
                    Type = MapSqlTypeToNetType(info.TypeName)
                };

                result.Add(column);
            }
        }

        return result;
    }

    /// <summary>
    /// Validates that the order columns are safe for keyset pagination — i.e. each
    /// is non-nullable and the composite is covered by at least one UNIQUE index
    /// or constraint. Returns a list of human-readable warnings; empty if all checks pass.
    /// </summary>
    /// <param name="connection">SQL connection</param>
    /// <param name="tableName">Full table name (schema.table)</param>
    /// <param name="orderColumns">Order columns to validate</param>
    public static async Task<List<string>> ValidateOrderColumnsAsync(
        SqlConnection connection,
        string tableName,
        List<OrderColumn> orderColumns)
    {
        List<string> warnings = [];

        foreach (OrderColumn col in orderColumns)
        {
            if (col.IsNullable)
            {
                warnings.Add($"order column '{col.Name}' is nullable; pagination may skip or duplicate rows when NULL values are present");
            }
        }

        // Find any UNIQUE index whose key columns are all contained in the order columns.
        // Such an index proves the order column composite is unique.
        const string query = @"
            SELECT i.name AS IndexName,
                   STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal) AS KeyColumns
            FROM sys.indexes i
            INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            INNER JOIN sys.tables tab ON i.object_id = tab.object_id
            INNER JOIN sys.schemas s ON tab.schema_id = s.schema_id
            WHERE i.is_unique = 1
              AND ic.is_included_column = 0
              AND CONCAT(s.name, '.', tab.name) = @tableName
            GROUP BY i.name
        ";

        await using SqlCommand command = new(query, connection);
        command.Parameters.AddWithValue("@tableName", tableName);

        HashSet<string> orderColSet = new(orderColumns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        bool foundCoveringUniqueIndex = false;

        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            string keyColumnsCsv = reader.GetString(1);
            string[] keyColumns = keyColumnsCsv.Split(',');
            if (keyColumns.All(kc => orderColSet.Contains(kc)))
            {
                foundCoveringUniqueIndex = true;
                break;
            }
        }

        if (!foundCoveringUniqueIndex)
        {
            warnings.Add(
                $"order columns ({string.Join(", ", orderColumns.Select(c => c.Name))}) " +
                "are not covered by any UNIQUE index or constraint; pagination may skip or duplicate rows if values aren't unique");
        }

        return warnings;
    }

    /// <summary>
    /// Builds a SELECT column projection for a table that wraps CLR UDT columns
    /// (hierarchyid, geography, geometry) in server-side text conversions so they
    /// can be read without referencing Microsoft.SqlServer.Types.
    /// </summary>
    /// <param name="connection">SQL connection</param>
    /// <param name="tableName">Full table name (schema.table)</param>
    /// <returns>Comma-separated SELECT projection ordered by column_id, or "*" if no columns matched.</returns>
    public static async Task<string> GetSelectColumnListAsync(
        SqlConnection connection,
        string tableName)
    {
        const string query = @"
            SELECT
                c.name AS ColumnName,
                t.name AS TypeName
            FROM sys.columns c
            INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
            INNER JOIN sys.tables tab ON c.object_id = tab.object_id
            INNER JOIN sys.schemas s ON tab.schema_id = s.schema_id
            WHERE CONCAT(s.name, '.', tab.name) = @tableName
            ORDER BY c.column_id
        ";

        await using SqlCommand command = new(query, connection);
        command.Parameters.AddWithValue("@tableName", tableName);

        List<string> projections = [];
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            string columnName = reader.GetString(0);
            string typeName = reader.GetString(1);
            projections.Add(BuildColumnProjection(columnName, typeName));
        }

        return projections.Count == 0 ? "*" : string.Join(", ", projections);
    }

    private static string BuildColumnProjection(string columnName, string typeName) =>
        typeName.ToLowerInvariant() switch
        {
            "hierarchyid" => $"CAST({Bracket(columnName)} AS NVARCHAR(4000)) AS {Bracket(columnName)}",
            "geography" or "geometry" => $"{Bracket(columnName)}.ToString() AS {Bracket(columnName)}",
            _ => Bracket(columnName)
        };

    /// <summary>
    /// Wraps a SQL identifier in T-SQL brackets, escaping any embedded ']' as ']]'.
    /// </summary>
    private static string Bracket(string identifier) => $"[{identifier.Replace("]", "]]")}]";

    /// <summary>
    /// Maps SQL Server type name to .NET Type
    /// </summary>
    private static Type? MapSqlTypeToNetType(string sqlTypeName) => sqlTypeName.ToLower() switch
    {
        "int" => typeof(int),
        "bigint" => typeof(long),
        "smallint" => typeof(short),
        "tinyint" => typeof(byte),
        "bit" => typeof(bool),
        "decimal" or "numeric" => typeof(decimal),
        "money" or "smallmoney" => typeof(decimal),
        "float" => typeof(double),
        "real" => typeof(float),
        "datetime" or "datetime2" or "smalldatetime" or "date" => typeof(DateTime),
        "datetimeoffset" => typeof(DateTimeOffset),
        "time" => typeof(TimeSpan),
        "char" or "varchar" or "text" => typeof(string),
        "nchar" or "nvarchar" or "ntext" => typeof(string),
        "binary" or "varbinary" or "image" => typeof(byte[]),
        "uniqueidentifier" => typeof(Guid),
        _ => null // For unsupported types
    };

    /// <summary>
    /// Builds a keyset pagination query for the next batch
    /// </summary>
    /// <param name="tableName">Full table name (schema.table)</param>
    /// <param name="orderColumns">Order columns with their values</param>
    /// <param name="batchSize">Batch size</param>
    /// <param name="isFirstBatch">Whether this is the first batch</param>
    /// <param name="whereCondition">Optional WHERE condition to filter data</param>
    /// <returns>SQL query and parameters</returns>
    public static (string Query, Dictionary<string, object> Parameters) BuildKeysetQuery(
        string tableName,
        List<OrderColumn> orderColumns,
        int batchSize,
        bool isFirstBatch = false,
        string whereCondition = "",
        string selectColumns = "*")
    {
        StringBuilder query = new();
        Dictionary<string, object> parameters = [];

        query.AppendLine($"SELECT TOP {batchSize} {selectColumns}");
        query.AppendLine($"FROM {tableName}");

        // Handle custom WHERE condition and keyset pagination
        bool hasWhereCondition = !string.IsNullOrWhiteSpace(whereCondition);
        bool needsKeysetWhere = !isFirstBatch && orderColumns.Count > 0;

        if (hasWhereCondition || needsKeysetWhere)
        {
            query.AppendLine("WHERE");

            // Add user-provided WHERE condition first if exists
            if (hasWhereCondition)
            {
                query.AppendLine(whereCondition);

                // Add AND for keyset pagination if needed
                if (needsKeysetWhere)
                {
                    query.AppendLine("AND (");
                }
            }

            // Skip keyset pagination for first batch
            if (!needsKeysetWhere)
            {
                // No keyset pagination needed
            }
            else
            {

                // Build the WHERE clause for keyset pagination
                for (int i = 0; i < orderColumns.Count; i++)
                {
                    OrderColumn column = orderColumns[i];

                    if (i > 0)
                    {
                        query.AppendLine("OR");
                    }

                    // Build conditions for each level of ordering columns
                    query.AppendLine("(");

                    for (int j = 0; j < i; j++)
                    {
                        OrderColumn prevColumn = orderColumns[j];
                        string paramName = $"@p{j}";

                        if (j > 0)
                        {
                            query.Append(" AND ");
                        }

                        if (prevColumn.Value == null || prevColumn.Value == DBNull.Value)
                        {
                            query.Append($"{Bracket(prevColumn.Name)} IS NULL");
                        }
                        else
                        {
                            query.Append($"{Bracket(prevColumn.Name)} = {paramName}");

                            if (prevColumn.Value is DateTime dtValue)
                            {
                                if (prevColumn.IsDateTime2Type)
                                {
                                    // Special handling for datetime2 columns
                                    // Use a string parameter to avoid SqlDateTime conversion
                                    string dateStr = dtValue.ToString("yyyy-MM-dd HH:mm:ss.fffffff");
                                    parameters[paramName] = dateStr;

                                    // Change the SQL to use CONVERT for explicit conversion
                                    query.Remove(query.Length - paramName.Length - 1, paramName.Length + 1);
                                    query.Append($"CONVERT(datetime2, {paramName}, 121)");
                                }
                                else
                                {
                                    // For any other date-related types
                                    parameters[paramName] = dtValue;
                                }
                            }
                            else
                            {
                                parameters[paramName] = prevColumn.Value;
                            }
                        }
                    }

                    if (i > 0)
                    {
                        query.Append(" AND ");
                    }

                    string currentParamName = $"@p{i}";
                    OrderColumn currentColumn = orderColumns[i];

                    if (currentColumn.Value == null || currentColumn.Value == DBNull.Value)
                    {
                        if (currentColumn.Direction.Equals("ASC", StringComparison.OrdinalIgnoreCase))
                        {
                            query.Append($"{Bracket(currentColumn.Name)} IS NOT NULL");
                        }
                        else
                        {
                            // For DESC order with NULL last value, we've reached the end
                            continue;
                        }
                    }
                    else
                    {
                        string op = currentColumn.Direction.Equals("ASC", StringComparison.OrdinalIgnoreCase) ? ">" : "<";

                        if (currentColumn.Value is DateTime dtValue && currentColumn.IsDateTime2Type)
                        {
                            // Special handling for datetime2 columns with inequality operators
                            string dateStr = dtValue.ToString("yyyy-MM-dd HH:mm:ss.fffffff");
                            parameters[currentParamName] = dateStr;

                            // Use explicit CONVERT in the SQL query
                            query.Append($"{Bracket(currentColumn.Name)} {op} CONVERT(datetime2, {currentParamName}, 121)");
                        }
                        else
                        {
                            query.Append($"{Bracket(currentColumn.Name)} {op} {currentParamName}");

                            // Handle datetime values differently based on type
                            if (currentColumn.Value is DateTime dateTimeValue)
                            {
                                if (currentColumn.IsDateTimeType)
                                {
                                    // Only clamp values for standard SQL datetime types
                                    parameters[currentParamName] = dateTimeValue;
                                }
                                else
                                {
                                    // For datetime2 and other extended types
                                    parameters[currentParamName] = dateTimeValue;
                                }
                            }
                            else
                            {
                                parameters[currentParamName] = currentColumn.Value;
                            }
                        }
                    }

                    query.AppendLine(")");
                }

                // Close the extra parenthesis if we had both WHERE condition and keyset pagination
                if (hasWhereCondition)
                {
                    query.AppendLine(")");
                }
            }
        }

        // Add ORDER BY clause
        if (orderColumns.Count > 0)
        {
            query.Append("ORDER BY ");
            query.AppendLine(string.Join(", ", orderColumns.Select(c => $"{Bracket(c.Name)} {c.Direction}")));
        }

        return (query.ToString(), parameters);
    }

    /// <summary>
    /// Extracts the last values from the SqlDataReader for order columns
    /// </summary>
    /// <param name="reader">SqlDataReader positioned at the last row</param>
    /// <param name="orderColumns">Order columns to update</param>
    public static void UpdateOrderColumnValues(SqlDataReader reader, List<OrderColumn> orderColumns)
    {
        foreach (OrderColumn column in orderColumns)
        {
            int ordinal;

            try
            {
                ordinal = reader.GetOrdinal(column.Name);
            }
            catch
            {
                // If the column doesn't exist in the result set, skip it
                continue;
            }

            if (reader.IsDBNull(ordinal))
            {
                column.Value = null;
            }
            else
            {
                try
                {
                    // SQL decimal/numeric can hold up to 38 digits — past .NET decimal range.
                    // Use SqlDecimal to avoid OverflowException when paginating those columns.
                    if (column.SqlType.Equals("decimal", StringComparison.OrdinalIgnoreCase) ||
                        column.SqlType.Equals("numeric", StringComparison.OrdinalIgnoreCase))
                    {
                        column.Value = reader.GetSqlDecimal(ordinal);
                    }
                    else
                    {
                        column.Value = reader.GetValue(ordinal);
                    }
                }
                catch (Exception ex)
                {
                    // Log the error and try a safer approach
                    Console.WriteLine($"Warning: Error getting value for column {column.Name}: {ex.Message}");
                    try
                    {
                        // Try getting as string which should be safe for most types
                        if (!reader.IsDBNull(ordinal))
                        {
                            column.Value = reader.GetString(ordinal);
                        }
                    }
                    catch
                    {
                        // Last resort: set to null to avoid breaking pagination
                        column.Value = null;
                    }
                }
            }
        }
    }
}
