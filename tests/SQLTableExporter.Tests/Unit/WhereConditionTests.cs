using Xunit;

namespace SQLTableExporter.Tests.Unit;

public class WhereConditionTests
{
    [Fact]
    public void Empty_input_returns_condition_with_no_clause()
    {
        var wc = WhereCondition.Parse("");

        Assert.NotNull(wc);
        Assert.False(wc!.HasCondition);
        Assert.Empty(wc.Sql);
        Assert.Empty(wc.Parameters);
    }

    [Fact]
    public void Whitespace_input_returns_condition_with_no_clause()
    {
        var wc = WhereCondition.Parse("   \t  ");

        Assert.NotNull(wc);
        Assert.False(wc!.HasCondition);
    }

    [Fact]
    public void Condition_without_parameters_is_rejected()
    {
        // Raw values are not allowed: must use :param placeholders.
        var wc = WhereCondition.Parse("Col = 1");

        Assert.Null(wc);
    }

    [Fact]
    public void Single_parameter_is_rewritten_to_at_where_prefix()
    {
        var wc = WhereCondition.Parse("Col = :foo");

        Assert.NotNull(wc);
        Assert.Equal("Col = @where_foo", wc!.Sql);
        Assert.Single(wc.Parameters);
        Assert.True(wc.Parameters.ContainsKey("@where_foo"));
        Assert.Equal(DBNull.Value, wc.Parameters["@where_foo"]);
    }

    [Fact]
    public void Multiple_distinct_parameters_are_all_extracted()
    {
        var wc = WhereCondition.Parse("A = :a AND B > :b AND C < :c");

        Assert.NotNull(wc);
        Assert.Equal("A = @where_a AND B > @where_b AND C < @where_c", wc!.Sql);
        Assert.Equal(3, wc.Parameters.Count);
        Assert.True(wc.Parameters.ContainsKey("@where_a"));
        Assert.True(wc.Parameters.ContainsKey("@where_b"));
        Assert.True(wc.Parameters.ContainsKey("@where_c"));
    }

    [Fact]
    public void Duplicate_parameter_names_collapse_to_one_entry()
    {
        var wc = WhereCondition.Parse("A = :x OR B = :x");

        Assert.NotNull(wc);
        Assert.Single(wc!.Parameters);
        Assert.True(wc.Parameters.ContainsKey("@where_x"));
        // Both placeholders rewritten.
        Assert.DoesNotContain(":x", wc.Sql);
    }

    [Fact]
    public void AddParameter_replaces_dbnull_with_provided_value()
    {
        var wc = WhereCondition.Parse("Col = :foo");
        Assert.NotNull(wc);

        wc!.AddParameter("foo", "bar");

        Assert.Equal("bar", wc.Parameters["@where_foo"]);
    }

    [Fact]
    public void AddParameter_throws_for_unknown_name()
    {
        var wc = WhereCondition.Parse("Col = :foo");
        Assert.NotNull(wc);

        Assert.Throws<ArgumentException>(() => wc!.AddParameter("notFoo", "x"));
    }

    [Fact]
    public void GetSqlWithPrefix_rewrites_parameter_names()
    {
        var wc = WhereCondition.Parse("A = :a AND B = :b");
        Assert.NotNull(wc);

        string rewritten = wc!.GetSqlWithPrefix(p => $"{p}_query");

        Assert.Equal("A = @where_a_query AND B = @where_b_query", rewritten);
    }

    [Fact]
    public void GetSqlWithPrefix_returns_empty_for_empty_condition()
    {
        var wc = WhereCondition.Parse("");
        Assert.NotNull(wc);

        string rewritten = wc!.GetSqlWithPrefix(p => $"{p}_query");

        Assert.Empty(rewritten);
    }

    [Fact]
    public void GetParametersWithPrefix_returns_renamed_dictionary()
    {
        var wc = WhereCondition.Parse("Col = :foo");
        Assert.NotNull(wc);
        wc!.AddParameter("foo", 42);

        var result = wc.GetParametersWithPrefix(p => $"{p}_query");

        Assert.Single(result);
        Assert.Equal(42, result["@where_foo_query"]);
        Assert.False(result.ContainsKey("@where_foo"));
    }

    [Fact]
    public void Constructor_with_sql_and_parameters_round_trips()
    {
        var parameters = new Dictionary<string, object> { ["@p0"] = 1 };
        var wc = new WhereCondition("Col = @p0", parameters);

        Assert.Equal("Col = @p0", wc.Sql);
        Assert.Same(parameters, wc.Parameters);
        Assert.True(wc.HasCondition);
    }

    [Fact]
    public void Default_constructor_produces_empty_condition()
    {
        var wc = new WhereCondition();

        Assert.Empty(wc.Sql);
        Assert.Empty(wc.Parameters);
        Assert.False(wc.HasCondition);
    }
}
