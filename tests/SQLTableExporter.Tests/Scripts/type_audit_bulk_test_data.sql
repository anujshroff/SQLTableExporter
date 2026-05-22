-- =============================================================================
-- Copy of docs/type_audit_bulk_test_data.sql with the USE preamble stripped.
-- The test fixture connects with InitialCatalog already set.
-- =============================================================================

IF OBJECT_ID('dbo.TypeAudit_Bulk_Standard', 'U')      IS NOT NULL DROP TABLE dbo.TypeAudit_Bulk_Standard;
IF OBJECT_ID('dbo.TypeAudit_Bulk_HighPrecision', 'U') IS NOT NULL DROP TABLE dbo.TypeAudit_Bulk_HighPrecision;
IF OBJECT_ID('dbo.TypeAudit_Bulk_UDT', 'U')           IS NOT NULL DROP TABLE dbo.TypeAudit_Bulk_UDT;
GO

CREATE TABLE dbo.TypeAudit_Bulk_Standard
(
    Id                INT IDENTITY(1,1) PRIMARY KEY,
    TestCase          NVARCHAR(200)     NOT NULL,
    BitCol            BIT               NULL,
    TinyIntCol        TINYINT           NULL,
    SmallIntCol       SMALLINT          NULL,
    IntCol            INT               NULL,
    BigIntCol         BIGINT            NULL,
    DecimalCol        DECIMAL(28, 10)   NULL,
    NumericCol        NUMERIC(18, 4)    NULL,
    MoneyCol          MONEY             NULL,
    SmallMoneyCol     SMALLMONEY        NULL,
    RealCol           REAL              NULL,
    FloatCol          FLOAT             NULL,
    CharCol           CHAR(10)          NULL,
    VarCharCol        VARCHAR(100)      NULL,
    TextCol           TEXT              NULL,
    NCharCol          NCHAR(10)         NULL,
    NVarCharCol       NVARCHAR(100)     NULL,
    NTextCol          NTEXT             NULL,
    VarCharMaxCol     VARCHAR(MAX)      NULL,
    NVarCharMaxCol    NVARCHAR(MAX)     NULL,
    BinaryCol         BINARY(8)         NULL,
    VarBinaryCol      VARBINARY(100)    NULL,
    VarBinaryMaxCol   VARBINARY(MAX)    NULL,
    ImageCol          IMAGE             NULL,
    RowVersionCol     ROWVERSION        NOT NULL,
    UniqueIdCol       UNIQUEIDENTIFIER  NULL,
    DateCol           DATE              NULL,
    TimeCol           TIME(7)           NULL,
    SmallDateTimeCol  SMALLDATETIME     NULL,
    DateTimeCol       DATETIME          NULL,
    DateTime2Col      DATETIME2(7)      NULL,
    DateTimeOffsetCol DATETIMEOFFSET(7) NULL,
    XmlCol            XML               NULL,
    SqlVariantCol     SQL_VARIANT       NULL
);
GO

;WITH N(Row) AS (
    SELECT TOP 500 ROW_NUMBER() OVER (ORDER BY (SELECT NULL))
    FROM sys.all_objects a CROSS JOIN sys.all_objects b
)
INSERT INTO dbo.TypeAudit_Bulk_Standard (
    TestCase, BitCol, TinyIntCol, SmallIntCol, IntCol, BigIntCol,
    DecimalCol, NumericCol, MoneyCol, SmallMoneyCol, RealCol, FloatCol,
    CharCol, VarCharCol, TextCol, NCharCol, NVarCharCol, NTextCol,
    VarCharMaxCol, NVarCharMaxCol,
    BinaryCol, VarBinaryCol, VarBinaryMaxCol, ImageCol,
    UniqueIdCol, DateCol, TimeCol, SmallDateTimeCol, DateTimeCol,
    DateTime2Col, DateTimeOffsetCol, XmlCol, SqlVariantCol
)
SELECT
    N'Row ' + CAST(Row AS NVARCHAR(10)),
    CASE WHEN Row % 50 = 0 THEN NULL ELSE CAST(Row % 2 AS BIT) END,
    CAST(Row % 256 AS TINYINT),
    CAST(Row AS SMALLINT),
    Row,
    CAST(Row AS BIGINT) * 1000000,
    CAST(Row AS DECIMAL(28,10)) + 0.1234567890,
    CAST(Row AS NUMERIC(18,4)) + 0.5678,
    CAST(Row AS MONEY) + 0.0001,
    CAST(Row AS SMALLMONEY),
    CAST(Row AS REAL),
    CAST(Row AS FLOAT),
    CAST('Row ' + CAST(Row AS VARCHAR(6)) AS CHAR(10)),
    'Row ' + CAST(Row AS VARCHAR(10)) +
        CASE WHEN Row % 100 = 0 THEN CHAR(13) + CHAR(0) + ',' + '"' ELSE '' END,
    'Row ' + CAST(Row AS VARCHAR(10)),
    CAST(N'Row ' + CAST(Row AS NVARCHAR(6)) AS NCHAR(10)),
    N'Row ' + CAST(Row AS NVARCHAR(10)) +
        CASE WHEN Row % 100 = 0 THEN NCHAR(13) + NCHAR(0) + N',' + N'"' ELSE N'' END,
    N'Row ' + CAST(Row AS NVARCHAR(10)),
    'Row ' + CAST(Row AS VARCHAR(10)),
    N'Row ' + CAST(Row AS NVARCHAR(10)),
    CAST(Row AS BINARY(8)),
    CAST(Row AS VARBINARY(8)),
    CAST(Row AS VARBINARY(8)),
    CAST(Row AS VARBINARY(8)),
    CONVERT(UNIQUEIDENTIFIER, CONVERT(BINARY(16), Row)),
    DATEADD(DAY, Row, CAST('2024-01-01' AS DATE)),
    DATEADD(SECOND, Row, CAST('00:00:00' AS TIME(7))),
    DATEADD(MINUTE, Row, CAST('2000-01-01 00:00:00' AS SMALLDATETIME)),
    DATEADD(SECOND, Row, CAST('2000-01-01 00:00:00' AS DATETIME)),
    DATEADD(SECOND, Row, CAST('2000-01-01 00:00:00.0000000' AS DATETIME2(7))),
    TODATETIMEOFFSET(
        DATEADD(SECOND, Row, CAST('2000-01-01 00:00:00.0000000' AS DATETIME2(7))),
        '+00:00'
    ),
    CAST('<row id="' + CAST(Row AS NVARCHAR(10)) + '"/>' AS XML),
    CAST(Row AS INT)
FROM N;
GO

CREATE TABLE dbo.TypeAudit_Bulk_HighPrecision
(
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    TestCase    NVARCHAR(200)   NOT NULL,
    HighPrecCol DECIMAL(38, 0)  NULL
);
GO

;WITH N(Row) AS (
    SELECT TOP 500 ROW_NUMBER() OVER (ORDER BY (SELECT NULL))
    FROM sys.all_objects a CROSS JOIN sys.all_objects b
)
INSERT INTO dbo.TypeAudit_Bulk_HighPrecision (TestCase, HighPrecCol)
SELECT
    N'Row ' + CAST(Row AS NVARCHAR(10)),
    CAST('99999999999999999999999999999' AS DECIMAL(38, 0)) - CAST(Row AS DECIMAL(38, 0))
FROM N;
GO

CREATE TABLE dbo.TypeAudit_Bulk_UDT
(
    Id             INT IDENTITY(1,1) PRIMARY KEY,
    TestCase       NVARCHAR(200) NOT NULL,
    HierarchyIdCol HIERARCHYID   NULL,
    GeographyCol   GEOGRAPHY     NULL,
    GeometryCol    GEOMETRY      NULL
);
GO

;WITH N(Row) AS (
    SELECT TOP 500 ROW_NUMBER() OVER (ORDER BY (SELECT NULL))
    FROM sys.all_objects a CROSS JOIN sys.all_objects b
)
INSERT INTO dbo.TypeAudit_Bulk_UDT (TestCase, HierarchyIdCol, GeographyCol, GeometryCol)
SELECT
    N'Row ' + CAST(Row AS NVARCHAR(10)),
    hierarchyid::Parse('/' + CAST(Row AS NVARCHAR(10)) + '/'),
    geography::Point(40.0 + (Row * 0.001), -120.0 - (Row * 0.001), 4326),
    geometry::STGeomFromText(
        'POINT(' + CAST(Row AS NVARCHAR(10)) + ' ' + CAST(Row AS NVARCHAR(10)) + ')',
        0
    )
FROM N;
GO
