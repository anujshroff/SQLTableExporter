-- =============================================================================
-- Bulk pagination test data for SQLTableExporter (500 rows per table)
--
-- Purpose:
--   Verify that batched exports (e.g. -b 100) produce correct CSVs with no
--   missing, duplicated, or reordered rows. The originals at
--   docs/type_audit_test_data.sql cover type-specific edge cases; this file
--   covers pagination correctness at scale.
--
-- Schemas mirror the originals so the same code paths are exercised.
-- All non-Id cell values are deterministic functions of Id, so a verifier
-- can recompute expected values for any row.
--
-- Edge-case interaction:
--   - Every 50th row sets BitCol = NULL (verifies NULL handling across batches).
--   - Every 100th row appends CR + NUL + comma + quote to string columns
--     (lands precisely on the -b 100 batch boundary; verifies Bug #3 / #5
--     fixes survive pagination).
--
-- The script is idempotent: it drops and recreates the bulk tables each run.
-- =============================================================================

USE TypeAuditDB;
GO

IF OBJECT_ID('dbo.TypeAudit_Bulk_Standard', 'U')      IS NOT NULL DROP TABLE dbo.TypeAudit_Bulk_Standard;
IF OBJECT_ID('dbo.TypeAudit_Bulk_HighPrecision', 'U') IS NOT NULL DROP TABLE dbo.TypeAudit_Bulk_HighPrecision;
IF OBJECT_ID('dbo.TypeAudit_Bulk_UDT', 'U')           IS NOT NULL DROP TABLE dbo.TypeAudit_Bulk_UDT;
GO

-- =============================================================================
-- TypeAudit_Bulk_Standard: 500 rows, full type coverage
-- =============================================================================
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
    N'Row ' + CAST(Row AS NVARCHAR(10)),                                            -- TestCase
    CASE WHEN Row % 50 = 0 THEN NULL ELSE CAST(Row % 2 AS BIT) END,                 -- BitCol (NULL every 50th)
    CAST(Row % 256 AS TINYINT),                                                     -- TinyIntCol
    CAST(Row AS SMALLINT),                                                          -- SmallIntCol
    Row,                                                                            -- IntCol
    CAST(Row AS BIGINT) * 1000000,                                                  -- BigIntCol
    CAST(Row AS DECIMAL(28,10)) + 0.1234567890,                                     -- DecimalCol
    CAST(Row AS NUMERIC(18,4)) + 0.5678,                                            -- NumericCol
    CAST(Row AS MONEY) + 0.0001,                                                    -- MoneyCol
    CAST(Row AS SMALLMONEY),                                                        -- SmallMoneyCol
    CAST(Row AS REAL),                                                              -- RealCol
    CAST(Row AS FLOAT),                                                             -- FloatCol
    CAST('Row ' + CAST(Row AS VARCHAR(6)) AS CHAR(10)),                             -- CharCol (right-padded)
    'Row ' + CAST(Row AS VARCHAR(10)) +
        CASE WHEN Row % 100 = 0 THEN CHAR(13) + CHAR(0) + ',' + '"' ELSE '' END,    -- VarCharCol (edge chars every 100th)
    'Row ' + CAST(Row AS VARCHAR(10)),                                              -- TextCol
    CAST(N'Row ' + CAST(Row AS NVARCHAR(6)) AS NCHAR(10)),                          -- NCharCol
    N'Row ' + CAST(Row AS NVARCHAR(10)) +
        CASE WHEN Row % 100 = 0 THEN NCHAR(13) + NCHAR(0) + N',' + N'"' ELSE N'' END, -- NVarCharCol
    N'Row ' + CAST(Row AS NVARCHAR(10)),                                            -- NTextCol
    'Row ' + CAST(Row AS VARCHAR(10)),                                              -- VarCharMaxCol
    N'Row ' + CAST(Row AS NVARCHAR(10)),                                            -- NVarCharMaxCol
    CAST(Row AS BINARY(8)),                                                         -- BinaryCol
    CAST(Row AS VARBINARY(8)),                                                      -- VarBinaryCol
    CAST(Row AS VARBINARY(8)),                                                      -- VarBinaryMaxCol
    CAST(Row AS VARBINARY(8)),                                                      -- ImageCol
    CONVERT(UNIQUEIDENTIFIER, CONVERT(BINARY(16), Row)),                            -- UniqueIdCol (deterministic from Row)
    DATEADD(DAY, Row, CAST('2024-01-01' AS DATE)),                                  -- DateCol
    DATEADD(SECOND, Row, CAST('00:00:00' AS TIME(7))),                              -- TimeCol
    DATEADD(MINUTE, Row, CAST('2000-01-01 00:00:00' AS SMALLDATETIME)),             -- SmallDateTimeCol
    DATEADD(SECOND, Row, CAST('2000-01-01 00:00:00' AS DATETIME)),                  -- DateTimeCol
    DATEADD(SECOND, Row, CAST('2000-01-01 00:00:00.0000000' AS DATETIME2(7))),      -- DateTime2Col
    TODATETIMEOFFSET(
        DATEADD(SECOND, Row, CAST('2000-01-01 00:00:00.0000000' AS DATETIME2(7))),
        '+00:00'
    ),                                                                              -- DateTimeOffsetCol
    CAST('<row id="' + CAST(Row AS NVARCHAR(10)) + '"/>' AS XML),                   -- XmlCol
    CAST(Row AS INT)                                                                -- SqlVariantCol holds INT
FROM N;
GO

-- =============================================================================
-- TypeAudit_Bulk_HighPrecision: 500 rows, all out of .NET decimal range
-- (every row is past 7.92e28, so every row exercises the SqlDecimal path)
-- =============================================================================
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
    -- 9.99e28 - Row gives 500 distinct 29-digit values, all > .NET decimal max
    CAST('99999999999999999999999999999' AS DECIMAL(38, 0)) - CAST(Row AS DECIMAL(38, 0))
FROM N;
GO

-- =============================================================================
-- TypeAudit_Bulk_UDT: 500 rows of hierarchyid + geography + geometry,
-- each unique per row.
-- =============================================================================
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

PRINT 'Bulk test data created. 500 rows in each of:';
PRINT '  - dbo.TypeAudit_Bulk_Standard';
PRINT '  - dbo.TypeAudit_Bulk_HighPrecision';
PRINT '  - dbo.TypeAudit_Bulk_UDT';
PRINT '';
PRINT 'Run the exporter against each with -b 100 to test pagination correctness.';
GO
