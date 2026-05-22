using Xunit;

namespace SQLTableExporter.Tests.Integration;

[Collection("Database")]
public class PrimaryKeyDetectorTests(DatabaseFixture db)
{
    private readonly DatabaseFixture _db = db;

    [Fact]
    public async Task Single_column_primary_key_is_returned()
    {
        var pk = await PrimaryKeyDetector.DetectPrimaryKeyColumnsAsync(
            _db.ConnectionString, "dbo", "PkSingle");

        Assert.Equal("Id", pk);
    }

    [Fact]
    public async Task Composite_primary_key_is_returned_in_key_ordinal_order()
    {
        var pk = await PrimaryKeyDetector.DetectPrimaryKeyColumnsAsync(
            _db.ConnectionString, "dbo", "PkComposite");

        Assert.Equal("TenantId, EntityId", pk);
    }

    [Fact]
    public async Task Identity_column_is_used_as_fallback_when_no_primary_key()
    {
        var pk = await PrimaryKeyDetector.DetectPrimaryKeyColumnsAsync(
            _db.ConnectionString, "dbo", "PkIdentityOnly");

        Assert.Equal("AutoId", pk);
    }

    [Fact]
    public async Task Table_with_no_primary_key_and_no_identity_returns_empty()
    {
        var pk = await PrimaryKeyDetector.DetectPrimaryKeyColumnsAsync(
            _db.ConnectionString, "dbo", "PkNone");

        Assert.Empty(pk);
    }

    [Fact]
    public async Task Missing_table_returns_empty()
    {
        var pk = await PrimaryKeyDetector.DetectPrimaryKeyColumnsAsync(
            _db.ConnectionString, "dbo", "TableThatDoesNotExist");

        Assert.Empty(pk);
    }

    [Theory]
    [InlineData(null, "dbo", "PkSingle")]
    [InlineData("", "dbo", "PkSingle")]
    [InlineData("Server=irrelevant;", null, "PkSingle")]
    [InlineData("Server=irrelevant;", "", "PkSingle")]
    [InlineData("Server=irrelevant;", "dbo", null)]
    [InlineData("Server=irrelevant;", "dbo", "")]
    public async Task Empty_inputs_short_circuit_to_empty(string? cs, string? schema, string? table)
    {
        var pk = await PrimaryKeyDetector.DetectPrimaryKeyColumnsAsync(cs!, schema!, table!);

        Assert.Empty(pk);
    }
}
