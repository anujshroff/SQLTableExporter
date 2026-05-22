using Xunit;

namespace SQLTableExporter.Tests.Unit;

public class KeysetPaginationParsingTests
{
    [Fact]
    public void ParseOrderByColumns_empty_returns_empty_list()
    {
        var result = KeysetPagination.ParseOrderByColumns("");
        Assert.Empty(result);
    }

    [Fact]
    public void ParseOrderByColumns_single_column_defaults_to_asc()
    {
        var result = KeysetPagination.ParseOrderByColumns("Id");

        Assert.Single(result);
        Assert.Equal("Id", result[0].Name);
        Assert.Equal("ASC", result[0].Direction);
    }

    [Fact]
    public void ParseOrderByColumns_explicit_asc_is_recognized()
    {
        var result = KeysetPagination.ParseOrderByColumns("Id ASC");

        Assert.Single(result);
        Assert.Equal("Id", result[0].Name);
        Assert.Equal("ASC", result[0].Direction);
    }

    [Fact]
    public void ParseOrderByColumns_explicit_desc_is_recognized()
    {
        var result = KeysetPagination.ParseOrderByColumns("CreatedAt DESC");

        Assert.Single(result);
        Assert.Equal("CreatedAt", result[0].Name);
        Assert.Equal("DESC", result[0].Direction);
    }

    [Fact]
    public void ParseOrderByColumns_mixed_directions_preserved_in_order()
    {
        var result = KeysetPagination.ParseOrderByColumns("TenantId, CreatedAt DESC, Id ASC");

        Assert.Equal(3, result.Count);
        Assert.Equal(("TenantId", "ASC"), (result[0].Name, result[0].Direction));
        Assert.Equal(("CreatedAt", "DESC"), (result[1].Name, result[1].Direction));
        Assert.Equal(("Id", "ASC"), (result[2].Name, result[2].Direction));
    }

    [Theory]
    [InlineData("Id asc", "Id", "ASC")]
    [InlineData("Id desc", "Id", "DESC")]
    [InlineData("Id Asc", "Id", "ASC")]
    [InlineData("Id Desc", "Id", "DESC")]
    public void ParseOrderByColumns_direction_match_is_case_insensitive(string input, string expectedName, string expectedDir)
    {
        var result = KeysetPagination.ParseOrderByColumns(input);

        Assert.Single(result);
        Assert.Equal(expectedName, result[0].Name);
        Assert.Equal(expectedDir, result[0].Direction);
    }

    [Fact]
    public void BuildKeysetQuery_first_batch_emits_top_with_no_keyset_predicate()
    {
        var orderColumns = new List<KeysetPagination.OrderColumn>
        {
            new() { Name = "Id", SqlType = "int", Direction = "ASC" }
        };

        var (query, parameters) = KeysetPagination.BuildKeysetQuery(
            "dbo.Users", orderColumns, batchSize: 100, isFirstBatch: true);

        Assert.Contains("SELECT TOP 100", query);
        Assert.Contains("FROM dbo.Users", query);
        Assert.Contains("ORDER BY [Id] ASC", query);
        Assert.DoesNotContain("WHERE", query);
        Assert.Empty(parameters);
    }

    [Fact]
    public void BuildKeysetQuery_subsequent_batch_emits_keyset_predicate_with_parameters()
    {
        var orderColumns = new List<KeysetPagination.OrderColumn>
        {
            new() { Name = "Id", SqlType = "int", Direction = "ASC", Value = 42 }
        };

        var (query, parameters) = KeysetPagination.BuildKeysetQuery(
            "dbo.Users", orderColumns, batchSize: 100, isFirstBatch: false);

        Assert.Contains("SELECT TOP 100", query);
        Assert.Contains("FROM dbo.Users", query);
        Assert.Contains("WHERE", query);
        Assert.Contains("[Id] > @p0", query);
        Assert.Contains("ORDER BY [Id] ASC", query);
        Assert.Equal(42, parameters["@p0"]);
    }

    [Fact]
    public void BuildKeysetQuery_descending_uses_less_than_operator()
    {
        var orderColumns = new List<KeysetPagination.OrderColumn>
        {
            new() { Name = "Id", SqlType = "int", Direction = "DESC", Value = 42 }
        };

        var (query, _) = KeysetPagination.BuildKeysetQuery(
            "dbo.Users", orderColumns, batchSize: 100, isFirstBatch: false);

        Assert.Contains("[Id] < @p0", query);
        Assert.Contains("ORDER BY [Id] DESC", query);
    }

    [Fact]
    public void BuildKeysetQuery_composite_keyset_produces_tiebreaker_branches()
    {
        var orderColumns = new List<KeysetPagination.OrderColumn>
        {
            new() { Name = "TenantId", SqlType = "int", Direction = "ASC", Value = 7 },
            new() { Name = "Id", SqlType = "int", Direction = "ASC", Value = 42 }
        };

        var (query, parameters) = KeysetPagination.BuildKeysetQuery(
            "dbo.Users", orderColumns, batchSize: 100, isFirstBatch: false);

        // Two predicate branches separated by OR:
        //   (TenantId > @p0) OR (TenantId = @p0 AND Id > @p1)
        Assert.Contains("[TenantId] > @p0", query);
        Assert.Contains("[TenantId] = @p0", query);
        Assert.Contains("[Id] > @p1", query);
        Assert.Contains("OR", query);
        Assert.Equal(7, parameters["@p0"]);
        Assert.Equal(42, parameters["@p1"]);
    }

    [Fact]
    public void BuildKeysetQuery_user_where_condition_is_prepended_with_AND()
    {
        var orderColumns = new List<KeysetPagination.OrderColumn>
        {
            new() { Name = "Id", SqlType = "int", Direction = "ASC", Value = 42 }
        };

        var (query, _) = KeysetPagination.BuildKeysetQuery(
            "dbo.Users", orderColumns, batchSize: 100, isFirstBatch: false,
            whereCondition: "Country = @where_country_query");

        Assert.Contains("Country = @where_country_query", query);
        Assert.Contains("AND (", query);
        Assert.Contains("[Id] > @p0", query);
    }

    [Fact]
    public void BuildKeysetQuery_user_where_condition_on_first_batch_emits_without_keyset()
    {
        var orderColumns = new List<KeysetPagination.OrderColumn>
        {
            new() { Name = "Id", SqlType = "int", Direction = "ASC" }
        };

        var (query, parameters) = KeysetPagination.BuildKeysetQuery(
            "dbo.Users", orderColumns, batchSize: 100, isFirstBatch: true,
            whereCondition: "Country = @where_country_query");

        Assert.Contains("WHERE", query);
        Assert.Contains("Country = @where_country_query", query);
        Assert.DoesNotContain("@p0", query);
        Assert.Empty(parameters);
    }

    [Fact]
    public void BuildKeysetQuery_select_columns_replaces_star()
    {
        var orderColumns = new List<KeysetPagination.OrderColumn>
        {
            new() { Name = "Id", SqlType = "int", Direction = "ASC" }
        };

        var (query, _) = KeysetPagination.BuildKeysetQuery(
            "dbo.Users", orderColumns, batchSize: 100, isFirstBatch: true,
            selectColumns: "[Id], [Name]");

        Assert.Contains("SELECT TOP 100 [Id], [Name]", query);
    }

    [Fact]
    public void OrderColumn_IsDateTimeType_only_matches_legacy_datetime_types()
    {
        Assert.True(new KeysetPagination.OrderColumn { SqlType = "datetime" }.IsDateTimeType);
        Assert.True(new KeysetPagination.OrderColumn { SqlType = "smalldatetime" }.IsDateTimeType);
        Assert.False(new KeysetPagination.OrderColumn { SqlType = "datetime2" }.IsDateTimeType);
        Assert.False(new KeysetPagination.OrderColumn { SqlType = "date" }.IsDateTimeType);
    }

    [Fact]
    public void OrderColumn_IsDateTime2Type_matches_datetime2_and_date()
    {
        Assert.True(new KeysetPagination.OrderColumn { SqlType = "datetime2" }.IsDateTime2Type);
        Assert.True(new KeysetPagination.OrderColumn { SqlType = "date" }.IsDateTime2Type);
        Assert.False(new KeysetPagination.OrderColumn { SqlType = "datetime" }.IsDateTime2Type);
    }
}
