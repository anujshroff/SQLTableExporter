using Microsoft.Data.SqlClient;
using Xunit;

namespace SQLTableExporter.Tests.Integration;

[Collection("Database")]
public class KeysetPaginationIntegrationTests(DatabaseFixture db)
{
    private readonly DatabaseFixture _db = db;

    private async Task<SqlConnection> OpenAsync()
    {
        var conn = new SqlConnection(_db.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }

    [Fact]
    public async Task GetOrderColumnsAsync_populates_sql_and_net_types_for_int_id()
    {
        await using var conn = await OpenAsync();
        var columns = await KeysetPagination.GetOrderColumnsAsync(conn, "dbo.TypeAudit_Standard", "Id");

        var id = Assert.Single(columns);
        Assert.Equal("Id", id.Name);
        Assert.Equal("int", id.SqlType, ignoreCase: true);
        Assert.Equal(typeof(int), id.Type);
        Assert.False(id.IsNullable);
        Assert.Equal("ASC", id.Direction);
    }

    [Fact]
    public async Task GetOrderColumnsAsync_handles_composite_order_and_preserves_direction()
    {
        await using var conn = await OpenAsync();
        var columns = await KeysetPagination.GetOrderColumnsAsync(
            conn, "dbo.PkComposite", "TenantId, EntityId DESC");

        Assert.Equal(2, columns.Count);
        Assert.Equal("TenantId", columns[0].Name);
        Assert.Equal("ASC", columns[0].Direction);
        Assert.Equal("EntityId", columns[1].Name);
        Assert.Equal("DESC", columns[1].Direction);
    }

    [Fact]
    public async Task ValidateOrderColumnsAsync_no_warnings_for_primary_key_columns()
    {
        await using var conn = await OpenAsync();
        var columns = await KeysetPagination.GetOrderColumnsAsync(
            conn, "dbo.TypeAudit_Standard", "Id");

        var warnings = await KeysetPagination.ValidateOrderColumnsAsync(
            conn, "dbo.TypeAudit_Standard", columns);

        Assert.Empty(warnings);
    }

    [Fact]
    public async Task ValidateOrderColumnsAsync_warns_for_nullable_column()
    {
        await using var conn = await OpenAsync();
        var columns = await KeysetPagination.GetOrderColumnsAsync(
            conn, "dbo.TypeAudit_Standard", "IntCol");

        var warnings = await KeysetPagination.ValidateOrderColumnsAsync(
            conn, "dbo.TypeAudit_Standard", columns);

        Assert.Contains(warnings, w => w.Contains("IntCol") && w.Contains("nullable"));
    }

    [Fact]
    public async Task ValidateOrderColumnsAsync_warns_when_no_unique_index_covers_columns()
    {
        await using var conn = await OpenAsync();
        var columns = await KeysetPagination.GetOrderColumnsAsync(
            conn, "dbo.TypeAudit_Standard", "TestCase");

        var warnings = await KeysetPagination.ValidateOrderColumnsAsync(
            conn, "dbo.TypeAudit_Standard", columns);

        Assert.Contains(warnings, w => w.Contains("UNIQUE"));
    }

    [Fact]
    public async Task GetSelectColumnListAsync_wraps_udt_columns_in_text_conversions()
    {
        await using var conn = await OpenAsync();
        var projection = await KeysetPagination.GetSelectColumnListAsync(
            conn, "dbo.TypeAudit_UDT");

        // hierarchyid → CAST AS NVARCHAR
        Assert.Contains("CAST([HierarchyIdCol] AS NVARCHAR(4000))", projection);
        // geography / geometry → .ToString()
        Assert.Contains("[GeographyCol].ToString()", projection);
        Assert.Contains("[GeometryCol].ToString()", projection);
        // Scalar columns come through unwrapped.
        Assert.Contains("[Id]", projection);
        Assert.Contains("[TestCase]", projection);
    }

    [Fact]
    public async Task GetSelectColumnListAsync_returns_plain_columns_for_pure_scalar_table()
    {
        await using var conn = await OpenAsync();
        var projection = await KeysetPagination.GetSelectColumnListAsync(
            conn, "dbo.PkSingle");

        Assert.Equal("[Id], [Name]", projection);
    }

    [Fact]
    public async Task GetSelectColumnListAsync_returns_star_for_unknown_table()
    {
        await using var conn = await OpenAsync();
        var projection = await KeysetPagination.GetSelectColumnListAsync(
            conn, "dbo.Nope");

        Assert.Equal("*", projection);
    }
}
