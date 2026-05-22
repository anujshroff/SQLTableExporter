-- Fixtures for PrimaryKeyDetectorTests.
-- Each table exercises a different code path in DetectPrimaryKeyColumnsAsync.

IF OBJECT_ID('dbo.PkSingle', 'U')      IS NOT NULL DROP TABLE dbo.PkSingle;
IF OBJECT_ID('dbo.PkComposite', 'U')   IS NOT NULL DROP TABLE dbo.PkComposite;
IF OBJECT_ID('dbo.PkIdentityOnly', 'U') IS NOT NULL DROP TABLE dbo.PkIdentityOnly;
IF OBJECT_ID('dbo.PkNone', 'U')        IS NOT NULL DROP TABLE dbo.PkNone;
GO

-- Single-column primary key.
CREATE TABLE dbo.PkSingle
(
    Id   INT          NOT NULL PRIMARY KEY,
    Name NVARCHAR(50) NULL
);
GO

-- Composite primary key; column order matters (TenantId first, then EntityId).
CREATE TABLE dbo.PkComposite
(
    TenantId INT          NOT NULL,
    EntityId INT          NOT NULL,
    Payload  NVARCHAR(50) NULL,
    CONSTRAINT PK_PkComposite PRIMARY KEY (TenantId, EntityId)
);
GO

-- No declared PK, but has an identity column the detector should fall back to.
CREATE TABLE dbo.PkIdentityOnly
(
    AutoId INT IDENTITY(1,1) NOT NULL,
    Name   NVARCHAR(50)      NULL
);
GO

-- No PK and no identity; detector should return empty string.
CREATE TABLE dbo.PkNone
(
    Code NVARCHAR(10) NOT NULL,
    Name NVARCHAR(50) NULL
);
GO
