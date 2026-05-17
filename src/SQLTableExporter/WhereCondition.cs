using System.Text.RegularExpressions;

namespace SQLTableExporter;

/// <summary>
/// Represents a parameterized WHERE condition for SQL queries
/// </summary>
public partial class WhereCondition
{
    /// <summary>
    /// The SQL condition with parameter placeholders
    /// </summary>
    public string Sql { get; private set; }

    /// <summary>
    /// Dictionary of parameter names and values
    /// </summary>
    public Dictionary<string, object> Parameters { get; private set; }

    /// <summary>
    /// Creates an empty WHERE condition
    /// </summary>
    public WhereCondition()
    {
        Sql = string.Empty;
        Parameters = [];
    }

    /// <summary>
    /// Creates a WHERE condition with the specified SQL and parameters
    /// </summary>
    public WhereCondition(string sql, Dictionary<string, object> parameters)
    {
        Sql = sql;
        Parameters = parameters;
    }

    /// <summary>
    /// Whether this WHERE condition has a non-empty SQL part
    /// </summary>
    public bool HasCondition => !string.IsNullOrWhiteSpace(Sql);

    /// <summary>
    /// Parses a WHERE condition from a command-line input string
    /// </summary>
    /// <param name="input">Input string in format "Column1 = :value1 AND Column2 > :value2"</param>
    /// <returns>Parsed WhereCondition object or null if parsing failed</returns>
    public static WhereCondition? Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new WhereCondition();
        }

        // Regular expression to match parameter placeholders like :value or :name123
        Regex paramRegex = WhereConditionParameterRegex();
        MatchCollection matches = paramRegex.Matches(input);

        if (matches.Count == 0)
        {
            // No parameters found - this is no longer supported
            Console.WriteLine("Error: WHERE conditions must use parameters (e.g., Column = :value)");
            Console.WriteLine("This is required to prevent SQL injection attacks.");
            return null;
        }

        // Create parameter dictionary and replace placeholders with @param names
        Dictionary<string, object> parameters = [];
        string parameterizedSql = input;

        foreach (Match match in matches)
        {
            string paramName = match.Groups[1].Value;
            string sqlParamName = $"@where_{paramName}";

            // Replace parameter in SQL (only need to add to Parameters dictionary with placeholder)
            // The user will need to provide actual values through the AddParameter method
            parameterizedSql = parameterizedSql.Replace(match.Value, sqlParamName);

            // Store parameter name with DBNull as a placeholder (to be filled later)
            if (!parameters.ContainsKey(sqlParamName))
            {
                parameters.Add(sqlParamName, DBNull.Value);
            }
        }

        return new WhereCondition(parameterizedSql, parameters);
    }

    /// <summary>
    /// Adds or updates a parameter value
    /// </summary>
    /// <param name="name">Parameter name (without the : prefix)</param>
    /// <param name="value">Parameter value</param>
    public void AddParameter(string name, object value)
    {
        string sqlParamName = $"@where_{name}";

        Parameters[sqlParamName] = Parameters.ContainsKey(sqlParamName)
            ? value
            : throw new ArgumentException($"Parameter '{name}' not found in WHERE condition");
    }

    /// <summary>
    /// Gets a copy of the parameter dictionary with a given prefix to avoid name collisions
    /// </summary>
    /// <param name="prefixChange">Function to change parameter names if needed</param>
    /// <returns>Dictionary with renamed parameters</returns>
    public Dictionary<string, object> GetParametersWithPrefix(Func<string, string> prefixChange)
    {
        Dictionary<string, object> result = [];

        foreach (KeyValuePair<string, object> param in Parameters)
        {
            // Skip null parameters (not yet assigned)
            if (param.Value == null)
            {
                throw new InvalidOperationException($"Parameter '{param.Key}' has not been assigned a value");
            }

            // Apply prefix change function to parameter name
            string newName = prefixChange(param.Key);
            result.Add(newName, param.Value);
        }

        return result;
    }

    /// <summary>
    /// Gets the SQL with parameter names adjusted using the given prefix change function
    /// </summary>
    /// <param name="prefixChange">Function to change parameter names if needed</param>
    /// <returns>SQL with updated parameter names</returns>
    public string GetSqlWithPrefix(Func<string, string> prefixChange)
    {
        if (!HasCondition)
        {
            return string.Empty;
        }

        string result = Sql;

        foreach (KeyValuePair<string, object> param in Parameters)
        {
            string newName = prefixChange(param.Key);
            result = result.Replace(param.Key, newName);
        }

        return result;
    }

    [GeneratedRegex(@":([\w\d_]+)", RegexOptions.Compiled)]
    private static partial Regex WhereConditionParameterRegex();
}