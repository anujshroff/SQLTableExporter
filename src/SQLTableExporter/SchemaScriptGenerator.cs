using Microsoft.Data.SqlClient;
using System.Text;

namespace SQLTableExporter;

/// <summary>
/// Utility class for generating SQL scripts to recreate table schemas
/// </summary>
public class SchemaScriptGenerator(string connectionString, int commandTimeout = 30)
{
    private readonly string _connectionString = connectionString;
    private readonly int _commandTimeout = commandTimeout;

    /// <summary>
    /// Generates a SQL script to recreate the specified table schema
    /// </summary>
    /// <param name="schemaName">Schema name</param>
    /// <param name="tableName">Table name</param>
    /// <returns>SQL script as a string</returns>
    public async Task<string> GenerateTableSchemaScriptAsync(string schemaName, string tableName)
    {
        if (string.IsNullOrEmpty(_connectionString) ||
            string.IsNullOrEmpty(schemaName) ||
            string.IsNullOrEmpty(tableName))
        {
            throw new ArgumentException("Connection string, schema name, and table name are required.");
        }

        // Get column information
        List<TableColumn> columns = await GetTableColumnsAsync(schemaName, tableName);
        if (columns.Count == 0)
        {
            throw new InvalidOperationException($"No columns found for table {schemaName}.{tableName}.");
        }

        // Get primary key information
        List<PrimaryKeyColumn> primaryKeyColumns = await GetPrimaryKeyColumnsAsync(schemaName, tableName);

        // Get foreign key information
        List<ForeignKey> foreignKeys = await GetForeignKeysAsync(schemaName, tableName);

        // Get index information (excluding primary key index)
        List<TableIndex> indexes = await GetIndexesAsync(schemaName, tableName);

        // Get check constraint information
        List<CheckConstraint> checkConstraints = await GetCheckConstraintsAsync(schemaName, tableName);

        // Build the script
        StringBuilder script = new();

        // Header comment
        script.AppendLine("-- ============================================");
        script.AppendLine($"-- Script to recreate table: {schemaName}.{tableName}");
        script.AppendLine($"-- Generated on: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        script.AppendLine("-- ============================================");
        script.AppendLine();

        // Create schema if it doesn't exist
        script.AppendLine($"IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = '{schemaName}')");
        script.AppendLine("BEGIN");
        script.AppendLine($"    EXEC('CREATE SCHEMA [{schemaName}]')");
        script.AppendLine("END");
        script.AppendLine("GO");
        script.AppendLine();

        // Drop table if exists
        script.AppendLine($"IF OBJECT_ID('{schemaName}.{tableName}', 'U') IS NOT NULL");
        script.AppendLine("BEGIN");
        script.AppendLine($"    DROP TABLE [{schemaName}].[{tableName}]");
        script.AppendLine("END");
        script.AppendLine("GO");
        script.AppendLine();

        // Create table
        script.AppendLine($"CREATE TABLE [{schemaName}].[{tableName}] (");

        // Columns
        for (int i = 0; i < columns.Count; i++)
        {
            TableColumn column = columns[i];
            script.Append($"    [{column.Name}] {column.DataType}");

            if (!string.IsNullOrEmpty(column.DataTypeLength))
            {
                script.Append($"{column.DataTypeLength}");
            }

            if (column.IsIdentity)
            {
                script.Append($" IDENTITY({column.IdentitySeed},{column.IdentityIncrement})");
            }

            if (!column.IsNullable)
            {
                script.Append(" NOT NULL");
            }
            else
            {
                script.Append(" NULL");
            }

            if (!string.IsNullOrEmpty(column.DefaultValue))
            {
                script.Append($" DEFAULT {column.DefaultValue}");
            }

            // Add comma if not the last column or if there are primary key columns
            if (i < columns.Count - 1 || primaryKeyColumns.Count > 0)
            {
                script.AppendLine(",");
            }
            else
            {
                script.AppendLine();
            }
        }

        // Primary Key
        if (primaryKeyColumns.Count > 0)
        {
            script.Append($"    CONSTRAINT [PK_{tableName}] PRIMARY KEY CLUSTERED (");
            for (int i = 0; i < primaryKeyColumns.Count; i++)
            {
                script.Append($"[{primaryKeyColumns[i].Name}]");
                if (i < primaryKeyColumns.Count - 1)
                {
                    script.Append(", ");
                }
            }

            script.AppendLine(")");
        }

        script.AppendLine(")");
        script.AppendLine("GO");
        script.AppendLine();

        // Foreign Keys
        foreach (ForeignKey fk in foreignKeys)
        {
            script.AppendLine($"ALTER TABLE [{schemaName}].[{tableName}] WITH CHECK");
            script.Append($"ADD CONSTRAINT [{fk.Name}] FOREIGN KEY (");

            for (int i = 0; i < fk.Columns.Count; i++)
            {
                script.Append($"[{fk.Columns[i]}]");
                if (i < fk.Columns.Count - 1)
                {
                    script.Append(", ");
                }
            }

            script.Append($") REFERENCES [{fk.ReferencedSchema}].[{fk.ReferencedTable}] (");

            for (int i = 0; i < fk.ReferencedColumns.Count; i++)
            {
                script.Append($"[{fk.ReferencedColumns[i]}]");
                if (i < fk.ReferencedColumns.Count - 1)
                {
                    script.Append(", ");
                }
            }

            script.Append(')');

            if (fk.DeleteAction != "NO_ACTION")
            {
                script.Append($" ON DELETE {fk.DeleteAction}");
            }

            if (fk.UpdateAction != "NO_ACTION")
            {
                script.Append($" ON UPDATE {fk.UpdateAction}");
            }

            script.AppendLine();
            script.AppendLine("GO");
            script.AppendLine();
        }

        // Indexes
        foreach (TableIndex index in indexes)
        {
            if (index.IsPrimaryKey)
            {
                continue; // Skip primary key index since it's already created
            }

            script.Append($"CREATE ");

            if (index.IsUnique)
            {
                script.Append("UNIQUE ");
            }

            if (index.IsClustered)
            {
                script.Append("CLUSTERED ");
            }
            else
            {
                script.Append("NONCLUSTERED ");
            }

            script.AppendLine($"INDEX [{index.Name}] ON [{schemaName}].[{tableName}]");
            script.Append('(');

            for (int i = 0; i < index.Columns.Count; i++)
            {
                IndexColumn col = index.Columns[i];
                script.Append($"[{col.Name}]");

                if (col.IsDescending)
                {
                    script.Append(" DESC");
                }
                else
                {
                    script.Append(" ASC");
                }

                if (i < index.Columns.Count - 1)
                {
                    script.Append(", ");
                }
            }

            script.AppendLine(")");
            if (!string.IsNullOrEmpty(index.FilterDefinition))
            {
                script.AppendLine($"WHERE {index.FilterDefinition}");
            }

            script.AppendLine("GO");
            script.AppendLine();
        }

        // Check Constraints
        foreach (CheckConstraint check in checkConstraints)
        {
            script.AppendLine($"ALTER TABLE [{schemaName}].[{tableName}] WITH CHECK");
            script.AppendLine($"ADD CONSTRAINT [{check.Name}] CHECK {check.Definition}");
            script.AppendLine("GO");
            script.AppendLine();
        }

        return script.ToString();
    }

    /// <summary>
    /// Gets a list of table columns with their properties
    /// </summary>
    private async Task<List<TableColumn>> GetTableColumnsAsync(string schemaName, string tableName)
    {
        List<TableColumn> columns = [];

        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync();

            string query = @"
                SELECT 
                    c.name AS ColumnName,
                    t.name AS DataType,
                    CASE 
                        WHEN t.name IN ('nchar', 'nvarchar') AND c.max_length <> -1 THEN '(' + CAST(c.max_length/2 AS VARCHAR) + ')'
                        WHEN t.name IN ('char', 'varchar') AND c.max_length <> -1 THEN '(' + CAST(c.max_length AS VARCHAR) + ')'
                        WHEN t.name IN ('nchar', 'nvarchar', 'char', 'varchar') AND c.max_length = -1 THEN '(MAX)'
                        WHEN t.name = 'varbinary' AND c.max_length = -1 THEN '(MAX)'
                        WHEN t.name IN ('binary', 'varbinary') AND c.max_length <> -1 THEN '(' + CAST(c.max_length AS VARCHAR) + ')'
                        WHEN t.name IN ('decimal', 'numeric') THEN '(' + CAST(c.precision AS VARCHAR) + ',' + CAST(c.scale AS VARCHAR) + ')'
                        WHEN t.name IN ('datetime2', 'time', 'datetimeoffset') THEN '(' + CAST(c.scale AS VARCHAR) + ')'
                        ELSE ''
                    END AS DataTypeLength,
                    c.is_nullable AS IsNullable,
                    c.is_identity AS IsIdentity,
                    ISNULL(CAST(ic.seed_value AS VARCHAR), '') AS IdentitySeed,
                    ISNULL(CAST(ic.increment_value AS VARCHAR), '') AS IdentityIncrement,
                    ISNULL(OBJECT_NAME(dc.object_id), '') AS DefaultName,
                    ISNULL(dc.definition, '') AS DefaultValue,
                    c.column_id AS ColumnId
                FROM sys.columns c
                INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
                INNER JOIN sys.tables tb ON c.object_id = tb.object_id
                INNER JOIN sys.schemas s ON tb.schema_id = s.schema_id
                LEFT JOIN sys.default_constraints dc ON c.default_object_id = dc.object_id
                LEFT JOIN sys.identity_columns ic ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                WHERE s.name = @SchemaName AND tb.name = @TableName
                ORDER BY c.column_id;
            ";

            await using SqlCommand command = new(query, connection)
            {
                CommandTimeout = _commandTimeout
            };
            command.Parameters.AddWithValue("@SchemaName", schemaName);
            command.Parameters.AddWithValue("@TableName", tableName);

            await using SqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add(new TableColumn
                {
                    Name = reader.GetString(0),
                    DataType = reader.GetString(1),
                    DataTypeLength = reader.GetString(2),
                    IsNullable = reader.GetBoolean(3),
                    IsIdentity = reader.GetBoolean(4),
                    IdentitySeed = reader.GetString(5),
                    IdentityIncrement = reader.GetString(6),
                    DefaultName = reader.GetString(7),
                    DefaultValue = reader.GetString(8),
                    ColumnId = reader.GetInt32(9)
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting table columns: {ex.Message}");
            throw;
        }

        return columns;
    }

    /// <summary>
    /// Gets primary key columns for the specified table
    /// </summary>
    private async Task<List<PrimaryKeyColumn>> GetPrimaryKeyColumnsAsync(string schemaName, string tableName)
    {
        List<PrimaryKeyColumn> pkColumns = [];

        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync();

            string query = @"
                SELECT 
                    c.name AS ColumnName,
                    ic.key_ordinal AS KeyOrdinal
                FROM sys.indexes i
                INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
                INNER JOIN sys.tables t ON i.object_id = t.object_id
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE i.is_primary_key = 1
                AND s.name = @SchemaName
                AND t.name = @TableName
                ORDER BY ic.key_ordinal;
            ";

            await using SqlCommand command = new(query, connection)
            {
                CommandTimeout = _commandTimeout
            };
            command.Parameters.AddWithValue("@SchemaName", schemaName);
            command.Parameters.AddWithValue("@TableName", tableName);

            await using SqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                pkColumns.Add(new PrimaryKeyColumn
                {
                    Name = reader.GetString(0),
                    KeyOrdinal = Convert.ToInt32(reader.GetValue(1)) // Handle various numeric types
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting primary key columns: {ex.Message}");
            throw;
        }

        return pkColumns;
    }

    /// <summary>
    /// Gets foreign key constraints for the specified table
    /// </summary>
    private async Task<List<ForeignKey>> GetForeignKeysAsync(string schemaName, string tableName)
    {
        List<ForeignKey> foreignKeys = [];

        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync();

            string query = @"
                SELECT 
                    fk.name AS FKName,
                    OBJECT_SCHEMA_NAME(fk.referenced_object_id) AS ReferencedSchema,
                    OBJECT_NAME(fk.referenced_object_id) AS ReferencedTable,
                    delete_referential_action_desc AS DeleteAction,
                    update_referential_action_desc AS UpdateAction
                FROM sys.foreign_keys fk
                INNER JOIN sys.tables t ON fk.parent_object_id = t.object_id
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE s.name = @SchemaName
                AND t.name = @TableName;
            ";

            await using SqlCommand command = new(query, connection)
            {
                CommandTimeout = _commandTimeout
            };
            command.Parameters.AddWithValue("@SchemaName", schemaName);
            command.Parameters.AddWithValue("@TableName", tableName);

            await using SqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                ForeignKey fk = new()
                {
                    Name = reader.GetString(0),
                    ReferencedSchema = reader.GetString(1),
                    ReferencedTable = reader.GetString(2),
                    DeleteAction = reader.GetString(3),
                    UpdateAction = reader.GetString(4),
                    Columns = [],
                    ReferencedColumns = []
                };

                foreignKeys.Add(fk);
            }

            await connection.CloseAsync();

            // For each foreign key, get the column mappings in separate connections
            foreach (ForeignKey fk in foreignKeys)
            {
                await using SqlConnection columnConnection = new(_connectionString);
                await columnConnection.OpenAsync();

                string columnQuery = @"
                    SELECT 
                        pc.name AS ParentColumn,
                        rc.name AS ReferencedColumn
                    FROM sys.foreign_key_columns fkc
                    INNER JOIN sys.tables pt ON fkc.parent_object_id = pt.object_id
                    INNER JOIN sys.schemas ps ON pt.schema_id = ps.schema_id
                    INNER JOIN sys.tables rt ON fkc.referenced_object_id = rt.object_id
                    INNER JOIN sys.schemas rs ON rt.schema_id = rs.schema_id
                    INNER JOIN sys.columns pc ON fkc.parent_object_id = pc.object_id AND fkc.parent_column_id = pc.column_id
                    INNER JOIN sys.columns rc ON fkc.referenced_object_id = rc.object_id AND fkc.referenced_column_id = rc.column_id
                    INNER JOIN sys.foreign_keys fk ON fkc.constraint_object_id = fk.object_id
                    WHERE ps.name = @SchemaName
                    AND pt.name = @TableName
                    AND fk.name = @ForeignKeyName
                    ORDER BY fkc.constraint_column_id;
                ";

                await using SqlCommand columnCommand = new(columnQuery, columnConnection)
                {
                    CommandTimeout = _commandTimeout
                };
                columnCommand.Parameters.AddWithValue("@SchemaName", schemaName);
                columnCommand.Parameters.AddWithValue("@TableName", tableName);
                columnCommand.Parameters.AddWithValue("@ForeignKeyName", fk.Name);

                await using SqlDataReader columnReader = await columnCommand.ExecuteReaderAsync();
                while (await columnReader.ReadAsync())
                {
                    string parentColumn = columnReader.GetString(0);
                    string referencedColumn = columnReader.GetString(1);

                    fk.Columns.Add(parentColumn);
                    fk.ReferencedColumns.Add(referencedColumn);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting foreign keys: {ex.Message}");
            throw;
        }

        return foreignKeys;
    }

    /// <summary>
    /// Gets indexes for the specified table
    /// </summary>
    private async Task<List<TableIndex>> GetIndexesAsync(string schemaName, string tableName)
    {
        List<TableIndex> indexes = [];

        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync();

            string query = @"
                SELECT 
                    i.name AS IndexName,
                    i.is_unique AS IsUnique,
                    i.is_primary_key AS IsPrimaryKey,
                    i.type_desc AS IndexType,
                    ISNULL(i.filter_definition, '') AS FilterDefinition
                FROM sys.indexes i
                INNER JOIN sys.tables t ON i.object_id = t.object_id
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE s.name = @SchemaName
                AND t.name = @TableName
                AND i.name IS NOT NULL
                AND i.type > 0;
            ";

            await using SqlCommand command = new(query, connection)
            {
                CommandTimeout = _commandTimeout
            };
            command.Parameters.AddWithValue("@SchemaName", schemaName);
            command.Parameters.AddWithValue("@TableName", tableName);

            await using SqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                TableIndex index = new()
                {
                    Name = reader.GetString(0),
                    IsUnique = reader.GetBoolean(1),
                    IsPrimaryKey = reader.GetBoolean(2),
                    IsClustered = reader.GetString(3).Contains("CLUSTERED"),
                    FilterDefinition = reader.GetString(4),
                    Columns = []
                };

                indexes.Add(index);
            }

            await connection.CloseAsync();

            // For each index, get the columns in separate connections
            foreach (TableIndex index in indexes)
            {
                await using SqlConnection columnConnection = new(_connectionString);
                await columnConnection.OpenAsync();

                string columnQuery = @"
                    SELECT 
                        c.name AS ColumnName,
                        ic.is_descending_key AS IsDescending,
                        ic.key_ordinal AS KeyOrdinal
                    FROM sys.index_columns ic
                    INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
                    INNER JOIN sys.indexes i ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                    INNER JOIN sys.tables t ON i.object_id = t.object_id
                    INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                    WHERE s.name = @SchemaName
                    AND t.name = @TableName
                    AND i.name = @IndexName
                    AND ic.key_ordinal > 0
                    ORDER BY ic.key_ordinal;
                ";

                await using SqlCommand columnCommand = new(columnQuery, columnConnection)
                {
                    CommandTimeout = _commandTimeout
                };
                columnCommand.Parameters.AddWithValue("@SchemaName", schemaName);
                columnCommand.Parameters.AddWithValue("@TableName", tableName);
                columnCommand.Parameters.AddWithValue("@IndexName", index.Name);

                await using SqlDataReader columnReader = await columnCommand.ExecuteReaderAsync();
                while (await columnReader.ReadAsync())
                {
                    index.Columns.Add(new IndexColumn
                    {
                        Name = columnReader.GetString(0),
                        IsDescending = columnReader.GetBoolean(1),
                        KeyOrdinal = Convert.ToInt32(columnReader.GetValue(2))
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting indexes: {ex.Message}");
            throw;
        }

        return indexes;
    }

    /// <summary>
    /// Gets check constraints for the specified table
    /// </summary>
    private async Task<List<CheckConstraint>> GetCheckConstraintsAsync(string schemaName, string tableName)
    {
        List<CheckConstraint> checkConstraints = [];

        try
        {
            await using SqlConnection connection = new(_connectionString);
            await connection.OpenAsync();

            string query = @"
                SELECT 
                    cc.name AS ConstraintName,
                    cc.definition AS Definition
                FROM sys.check_constraints cc
                INNER JOIN sys.tables t ON cc.parent_object_id = t.object_id
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE s.name = @SchemaName
                AND t.name = @TableName;
            ";

            await using SqlCommand command = new(query, connection)
            {
                CommandTimeout = _commandTimeout
            };
            command.Parameters.AddWithValue("@SchemaName", schemaName);
            command.Parameters.AddWithValue("@TableName", tableName);

            await using SqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                checkConstraints.Add(new CheckConstraint
                {
                    Name = reader.GetString(0),
                    Definition = reader.GetString(1)
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting check constraints: {ex.Message}");
            throw;
        }

        return checkConstraints;
    }

    /// <summary>
    /// Saves the schema script to a file
    /// </summary>
    /// <param name="schemaName">Schema name</param>
    /// <param name="tableName">Table name</param>
    /// <param name="outputDirectory">Output directory</param>
    /// <param name="filePrefix">Optional file prefix (defaults to schemaName_tableName)</param>
    /// <returns>Path to the saved script file</returns>
    public async Task<string> SaveSchemaScriptAsync(string schemaName, string tableName, string outputDirectory, string? filePrefix = null)
    {
        // Generate the script
        string script = await GenerateTableSchemaScriptAsync(schemaName, tableName);

        // Add commas between column definitions and fix syntax issues
        string fixedScript = "";
        bool inTableDef = false;
        List<string> lines = [.. script.Split(Environment.NewLine)];

        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i].TrimEnd();

            if (line.Contains("CREATE TABLE"))
            {
                inTableDef = true;
                fixedScript += line + Environment.NewLine;
                continue;
            }

            if (inTableDef && line.StartsWith(')'))
            {
                inTableDef = false;
                fixedScript += line + Environment.NewLine;
                continue;
            }

            // Inside table definition, add commas after columns
            if (inTableDef && line.Trim().StartsWith('[') && !line.EndsWith(','))
            {
                // Check if next line starts with [ or CONSTRAINT
                if (i < lines.Count - 1 &&
                    (lines[i + 1].Trim().StartsWith('[') ||
                     lines[i + 1].Trim().StartsWith("CONSTRAINT")))
                {
                    line += ",";
                }
            }

            // Fix OBJECT_ID syntax
            if (line.Contains("IF OBJECT_ID("))
            {
                line = line.Replace("IF OBJECT_ID('", "IF OBJECT_ID(N'");
                line = line.Replace("', 'U')", "', N'U')");
            }

            fixedScript += line + Environment.NewLine;
        }

        script = fixedScript;

        // Fix multiple CLUSTERED indexes
        script = script.Replace("CREATE CLUSTERED INDEX", "CREATE NONCLUSTERED INDEX");

        // Ensure output directory exists
        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        // Determine filename
        string prefix = filePrefix ?? $"{schemaName}_{tableName}";
        string fileName = $"{prefix}_schema.sql";
        string filePath = Path.Combine(outputDirectory, fileName);

        // Write to file
        await File.WriteAllTextAsync(filePath, script);

        return filePath;
    }
}

/// <summary>
/// Represents a table column
/// </summary>
public class TableColumn
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string DataTypeLength { get; set; } = string.Empty;
    public bool IsNullable { get; set; }
    public bool IsIdentity { get; set; }
    public string IdentitySeed { get; set; } = string.Empty;
    public string IdentityIncrement { get; set; } = string.Empty;
    public string DefaultName { get; set; } = string.Empty;
    public string DefaultValue { get; set; } = string.Empty;
    public int ColumnId { get; set; }
}

/// <summary>
/// Represents a primary key column
/// </summary>
public class PrimaryKeyColumn
{
    public string Name { get; set; } = string.Empty;
    public int KeyOrdinal { get; set; }
}

/// <summary>
/// Represents a foreign key constraint
/// </summary>
public class ForeignKey
{
    public string Name { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = [];
    public string ReferencedSchema { get; set; } = string.Empty;
    public string ReferencedTable { get; set; } = string.Empty;
    public List<string> ReferencedColumns { get; set; } = [];
    public string DeleteAction { get; set; } = "NO_ACTION";
    public string UpdateAction { get; set; } = "NO_ACTION";
}

/// <summary>
/// Represents a table index
/// </summary>
public class TableIndex
{
    public string Name { get; set; } = string.Empty;
    public bool IsUnique { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsClustered { get; set; }
    public string FilterDefinition { get; set; } = string.Empty;
    public List<IndexColumn> Columns { get; set; } = [];
}

/// <summary>
/// Represents an index column
/// </summary>
public class IndexColumn
{
    public string Name { get; set; } = string.Empty;
    public bool IsDescending { get; set; }
    public int KeyOrdinal { get; set; }
}

/// <summary>
/// Represents a check constraint
/// </summary>
public class CheckConstraint
{
    public string Name { get; set; } = string.Empty;
    public string Definition { get; set; } = string.Empty;
}
