-- Fixtures for SchemaScriptGeneratorTests.
-- A parent/child pair with FK, indexes, and a check constraint so that
-- the generator exercises every emit branch in one pass.

IF OBJECT_ID('dbo.SchemaChild', 'U')  IS NOT NULL DROP TABLE dbo.SchemaChild;
IF OBJECT_ID('dbo.SchemaParent', 'U') IS NOT NULL DROP TABLE dbo.SchemaParent;
GO

CREATE TABLE dbo.SchemaParent
(
    ParentId   INT            IDENTITY(1,1) NOT NULL,
    ParentName NVARCHAR(100)  NOT NULL,
    CreatedAt  DATETIME2(3)   NOT NULL CONSTRAINT DF_SchemaParent_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_SchemaParent PRIMARY KEY CLUSTERED (ParentId)
);
GO

CREATE TABLE dbo.SchemaChild
(
    ChildId     INT             IDENTITY(1,1) NOT NULL,
    ParentId    INT             NOT NULL,
    ChildName   NVARCHAR(100)   NOT NULL,
    ChildOrder  INT             NOT NULL,
    Amount      DECIMAL(18, 4)  NULL,
    Notes       NVARCHAR(MAX)   NULL,
    CONSTRAINT PK_SchemaChild PRIMARY KEY CLUSTERED (ChildId),
    CONSTRAINT FK_SchemaChild_Parent FOREIGN KEY (ParentId)
        REFERENCES dbo.SchemaParent (ParentId)
        ON DELETE CASCADE ON UPDATE NO ACTION,
    CONSTRAINT CK_SchemaChild_Amount CHECK (Amount IS NULL OR Amount >= 0)
);
GO

CREATE UNIQUE NONCLUSTERED INDEX UX_SchemaChild_ParentOrder
    ON dbo.SchemaChild (ParentId, ChildOrder);
GO

CREATE NONCLUSTERED INDEX IX_SchemaChild_ChildName
    ON dbo.SchemaChild (ChildName DESC);
GO
