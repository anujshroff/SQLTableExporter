-- =============================================================================
-- Type-coverage / edge-case fixture for SQLTableExporter integration tests.
-- Loaded into the test container's database by DatabaseFixture as an embedded
-- resource. Three tables exercise:
--   * TypeAudit_Standard      (6 rows) — every common scalar type, one row per
--                                        edge axis (mins, maxes, NULLs, CR/NUL/
--                                        comma/quote in strings, leap day, etc.)
--   * TypeAudit_HighPrecision (2 rows) — DECIMAL(38,0) at and beyond .NET
--                                        System.Decimal range.
--   * TypeAudit_UDT           (1 row)  — hierarchyid, geography, geometry.
-- The TestCase column on each row describes what it's testing so failures are
-- easy to localize cell-by-cell.
-- =============================================================================

IF OBJECT_ID('dbo.TypeAudit_Standard', 'U')      IS NOT NULL DROP TABLE dbo.TypeAudit_Standard;
IF OBJECT_ID('dbo.TypeAudit_HighPrecision', 'U') IS NOT NULL DROP TABLE dbo.TypeAudit_HighPrecision;
IF OBJECT_ID('dbo.TypeAudit_UDT', 'U')           IS NOT NULL DROP TABLE dbo.TypeAudit_UDT;
GO

CREATE TABLE dbo.TypeAudit_Standard
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

INSERT INTO dbo.TypeAudit_Standard (
    TestCase, BitCol, TinyIntCol, SmallIntCol, IntCol, BigIntCol,
    DecimalCol, NumericCol, MoneyCol, SmallMoneyCol, RealCol, FloatCol,
    CharCol, VarCharCol, TextCol, NCharCol, NVarCharCol, NTextCol,
    VarCharMaxCol, NVarCharMaxCol,
    BinaryCol, VarBinaryCol, VarBinaryMaxCol, ImageCol,
    UniqueIdCol, DateCol, TimeCol, SmallDateTimeCol, DateTimeCol,
    DateTime2Col, DateTimeOffsetCol, XmlCol, SqlVariantCol
) VALUES (
    N'Min values; strings contain comma',
    0, 0, -32768, -2147483648, -9223372036854775808,
    -999999999999999999.9999999999, -99999999999999.9999,
    -922337203685477.5808, -214748.3648,
    CAST(-3.40E+38 AS REAL), -1.79E+308,
    'a,b,c     ', 'has,a,comma', 'text,with,commas',
    N'a,b,c     ', N'has,a,comma', N'ntext,with,commas',
    'varcharmax,with,commas', N'nvarcharmax,with,commas',
    0x0011223344556677, 0x0102, 0x0102030405060708, 0x09080706,
    '00000000-0000-0000-0000-000000000000',
    '0001-01-01', '00:00:00.0000000',
    '1900-01-01 00:00:00', '1753-01-01 00:00:00.000',
    '0001-01-01 00:00:00.0000000', '0001-01-01 00:00:00.0000000 -14:00',
    CAST(N'<root attr="min,value"/>' AS XML), CAST(0 AS INT)
);

INSERT INTO dbo.TypeAudit_Standard (
    TestCase, BitCol, TinyIntCol, SmallIntCol, IntCol, BigIntCol,
    DecimalCol, NumericCol, MoneyCol, SmallMoneyCol, RealCol, FloatCol,
    CharCol, VarCharCol, TextCol, NCharCol, NVarCharCol, NTextCol,
    VarCharMaxCol, NVarCharMaxCol,
    BinaryCol, VarBinaryCol, VarBinaryMaxCol, ImageCol,
    UniqueIdCol, DateCol, TimeCol, SmallDateTimeCol, DateTimeCol,
    DateTime2Col, DateTimeOffsetCol, XmlCol, SqlVariantCol
) VALUES (
    N'Max values; strings contain double quote',
    1, 255, 32767, 2147483647, 9223372036854775807,
    999999999999999999.9999999999, 99999999999999.9999,
    922337203685477.5807, 214748.3647,
    CAST(3.40E+38 AS REAL), 1.79E+308,
    'a"b"c     ', 'has"a"quote', 'text"with"quotes',
    N'a"b"c     ', N'has"a"quote', N'ntext"with"quotes',
    'varcharmax"with"quotes', N'nvarcharmax"with"quotes',
    0xFFEEDDCCBBAA9988, 0xFFFE, 0xFFFEFDFCFBFAF9F8, 0xF7F6F5F4,
    'FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF',
    '9999-12-31', '23:59:59.9999999',
    '2079-06-06 23:59:00', '9999-12-31 23:59:59.997',
    '9999-12-31 23:59:59.9999999', '9999-12-31 23:59:59.9999999 +14:00',
    CAST(N'<root attr="has &quot;quote&quot;"/>' AS XML),
    CAST(N'a string in a variant' AS NVARCHAR(100))
);

INSERT INTO dbo.TypeAudit_Standard (
    TestCase, BitCol, TinyIntCol, SmallIntCol, IntCol, BigIntCol,
    DecimalCol, NumericCol, MoneyCol, SmallMoneyCol, RealCol, FloatCol,
    CharCol, VarCharCol, TextCol, NCharCol, NVarCharCol, NTextCol,
    VarCharMaxCol, NVarCharMaxCol,
    BinaryCol, VarBinaryCol, VarBinaryMaxCol, ImageCol,
    UniqueIdCol, DateCol, TimeCol, SmallDateTimeCol, DateTimeCol,
    DateTime2Col, DateTimeOffsetCol, XmlCol, SqlVariantCol
) VALUES (
    N'Typical values; strings contain LF',
    1, 42, 1234, 1234567, 1234567890123,
    123.4500000000, 1234.5600, 1234.5600, 1234.5600,
    CAST(3.1415927 AS REAL), 3.141592653589793,
    'a' + CHAR(10) + 'b      ', 'first' + CHAR(10) + 'second', 'line1' + CHAR(10) + 'line2',
    N'a' + NCHAR(10) + N'b      ', N'first' + NCHAR(10) + N'second', N'line1' + NCHAR(10) + N'line2',
    'a' + CHAR(10) + 'b', N'a' + NCHAR(10) + N'b',
    0xDEADBEEFCAFEBABE, 0xCAFEBABE, 0xCAFEBABEDEADBEEF, 0xFEEDFACE,
    '550E8400-E29B-41D4-A716-446655440000',
    '2024-06-15', '14:30:45.1234567',
    '2024-06-15 14:30:00', '2024-06-15 14:30:45.123',
    '2024-06-15 14:30:45.1234567', '2024-06-15 14:30:45.1234567 -05:00',
    CAST(N'<root>line1' + NCHAR(10) + N'line2</root>' AS XML),
    CAST('2024-06-15T14:30:45' AS DATETIME)
);

INSERT INTO dbo.TypeAudit_Standard (
    TestCase, BitCol, TinyIntCol, SmallIntCol, IntCol, BigIntCol,
    DecimalCol, NumericCol, MoneyCol, SmallMoneyCol, RealCol, FloatCol,
    CharCol, VarCharCol, TextCol, NCharCol, NVarCharCol, NTextCol,
    VarCharMaxCol, NVarCharMaxCol,
    BinaryCol, VarBinaryCol, VarBinaryMaxCol, ImageCol,
    UniqueIdCol, DateCol, TimeCol, SmallDateTimeCol, DateTimeCol,
    DateTime2Col, DateTimeOffsetCol, XmlCol, SqlVariantCol
) VALUES (
    N'Precision edges; strings contain bare CR (bug #3)',
    NULL, 1, -1, -1, -1,
    0.0000000001, 0.0001, 0.0001, 0.0001,
    CAST(1.18E-38 AS REAL), 2.23E-308,
    'a' + CHAR(13) + 'b      ', 'split' + CHAR(13) + 'here', 'cr' + CHAR(13) + 'in text',
    N'a' + NCHAR(13) + N'b      ', N'split' + NCHAR(13) + N'here', N'cr' + NCHAR(13) + N'in ntext',
    'a' + CHAR(13) + 'b', N'a' + NCHAR(13) + N'b',
    0x0D0D0D0D0D0D0D0D, 0x0D, 0x0D0A, 0x0A0D,
    '11111111-2222-3333-4444-555555555555',
    '2024-12-31', '23:00:00.0000001',
    '2024-12-31 23:00:00', '2024-12-31 23:59:59.997',
    '2024-12-31 23:59:59.9999999', '2024-12-31 23:59:59.9999999 +00:00',
    CAST(N'<root>cr' + NCHAR(13) + N'inside</root>' AS XML),
    CAST('99999999-8888-7777-6666-555555555555' AS UNIQUEIDENTIFIER)
);

INSERT INTO dbo.TypeAudit_Standard (TestCase) VALUES (N'All NULLs');

INSERT INTO dbo.TypeAudit_Standard (
    TestCase, BitCol, TinyIntCol, SmallIntCol, IntCol, BigIntCol,
    DecimalCol, NumericCol, MoneyCol, SmallMoneyCol, RealCol, FloatCol,
    CharCol, VarCharCol, TextCol, NCharCol, NVarCharCol, NTextCol,
    VarCharMaxCol, NVarCharMaxCol,
    BinaryCol, VarBinaryCol, VarBinaryMaxCol, ImageCol,
    UniqueIdCol, DateCol, TimeCol, SmallDateTimeCol, DateTimeCol,
    DateTime2Col, DateTimeOffsetCol, XmlCol, SqlVariantCol
) VALUES (
    N'Unicode (BMP, CJK, surrogate pair) + embedded NUL (bug #5)',
    1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    'a' + CHAR(0) + 'b      ',
    'pre' + CHAR(0) + 'post',
    'pre' + CHAR(0) + 'post',
    N'Omega' + NCHAR(0) + N'CJK   ',
    N'Greek O' + NCHAR(937) + N' CJK ' + NCHAR(22909) + N' surrogate ' + NCHAR(0xD834) + NCHAR(0xDD1E),
    N'Greek O' + NCHAR(937) + N' NUL' + NCHAR(0) + N' CJK ' + NCHAR(22909),
    'a' + CHAR(0) + 'b',
    N'O' + NCHAR(937) + NCHAR(0) + NCHAR(22909) + NCHAR(0xD834) + NCHAR(0xDD1E),
    0x0000000000000000, 0x00, 0x00FF00FF, 0xFF00FF00,
    'A1B2C3D4-E5F6-7890-ABCD-EF1234567890',
    '2000-02-29', '12:00:00',
    '2000-02-29 12:00:00', '2000-02-29 12:00:00.000',
    '2000-02-29 12:00:00.0000000', '2000-02-29 12:00:00.0000000 +05:30',
    CAST(N'<root>Greek=' + NCHAR(937) + N' CJK=' + NCHAR(22909) + N'</root>' AS XML),
    CAST(0x0102030405060708 AS VARBINARY(8))
);
GO

CREATE TABLE dbo.TypeAudit_HighPrecision
(
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    TestCase    NVARCHAR(200)   NOT NULL,
    HighPrecCol DECIMAL(38, 0)  NULL
);
GO

INSERT INTO dbo.TypeAudit_HighPrecision (TestCase, HighPrecCol)
VALUES (N'In-range: decimal.MaxValue (29 digits, 7.92e28)',
        CAST('79228162514264337593543950335' AS DECIMAL(38, 0)));

INSERT INTO dbo.TypeAudit_HighPrecision (TestCase, HighPrecCol)
VALUES (N'Out-of-range: 29 nines (~1e29) - bug #1 (export throws)',
        CAST('99999999999999999999999999999' AS DECIMAL(38, 0)));
GO

CREATE TABLE dbo.TypeAudit_UDT
(
    Id             INT IDENTITY(1,1) PRIMARY KEY,
    TestCase       NVARCHAR(200) NOT NULL,
    HierarchyIdCol HIERARCHYID   NULL,
    GeographyCol   GEOGRAPHY     NULL,
    GeometryCol    GEOMETRY      NULL
);
GO

INSERT INTO dbo.TypeAudit_UDT (TestCase, HierarchyIdCol, GeographyCol, GeometryCol)
VALUES (N'Sample UDT row - bug #7 (export throws)',
        hierarchyid::Parse('/1/2/3/'),
        geography::STGeomFromText('POINT(-122.34900 47.65100)', 4326),
        geometry::STGeomFromText('POLYGON((0 0, 10 0, 10 10, 0 10, 0 0))', 0));
GO
