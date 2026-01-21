/*
  TILSOFTAI Atomic - SQL Schema V2
  Purpose:
    - Align SQL Server metadata schema with Mode B orchestrator (manual tool loop)
    - Provide catalog context (ParamsJson/ExampleJson/SchemaHintsJson) to avoid C# heuristics
    - Provide optional TableKindSignals for fallback table classification
    - Keep changes backward-compatible (ALTER when objects already exist)

  Notes:
    - This script is SAFE to run multiple times (idempotent guards).
    - Adjust NVARCHAR sizes as needed for your environment.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
BEGIN TRAN;

--------------------------------------------------------------------------------
-- 1) dbo.TILSOFTAI_SPCatalog
--------------------------------------------------------------------------------

IF OBJECT_ID(N'dbo.TILSOFTAI_SPCatalog', N'U') IS NULL
BEGIN
    PRINT 'Creating dbo.TILSOFTAI_SPCatalog...';

    CREATE TABLE dbo.TILSOFTAI_SPCatalog
    (
        CatalogId            INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_TILSOFTAI_SPCatalog PRIMARY KEY,

        SpName               NVARCHAR(256) NOT NULL,

        IsEnabled            BIT NOT NULL
            CONSTRAINT DF_TILSOFTAI_SPCatalog_IsEnabled DEFAULT (1),

        IsReadOnly           BIT NOT NULL
            CONSTRAINT DF_TILSOFTAI_SPCatalog_IsReadOnly DEFAULT (1),

        IsAtomicCompatible   BIT NOT NULL
            CONSTRAINT DF_TILSOFTAI_SPCatalog_IsAtomicCompatible DEFAULT (1),

        Domain               NVARCHAR(100) NULL,
        Entity               NVARCHAR(100) NULL,
        Tags                 NVARCHAR(400) NULL,

        IntentVi             NVARCHAR(1000) NULL,
        IntentEn             NVARCHAR(1000) NULL,

        -- Source-of-truth tool param spec (JSON array)
        ParamsJson           NVARCHAR(MAX) NULL,

        -- Example questions / usage (JSON array)
        ExampleJson          NVARCHAR(MAX) NULL,

        -- Schema hints for join graph + semantic roles (JSON object)
        SchemaHintsJson      NVARCHAR(MAX) NULL,

        UpdatedAtUtc         DATETIME2(0) NOT NULL
            CONSTRAINT DF_TILSOFTAI_SPCatalog_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),

        CreatedAtUtc         DATETIME2(0) NOT NULL
            CONSTRAINT DF_TILSOFTAI_SPCatalog_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );

    -- Uniqueness on SP name
    CREATE UNIQUE INDEX UX_TILSOFTAI_SPCatalog_SpName
        ON dbo.TILSOFTAI_SPCatalog(SpName);

    -- Common lookup index
    CREATE INDEX IX_TILSOFTAI_SPCatalog_Enabled
        ON dbo.TILSOFTAI_SPCatalog(IsEnabled, IsAtomicCompatible)
        INCLUDE (Domain, Entity, Tags, UpdatedAtUtc);

END
ELSE
BEGIN
    PRINT 'dbo.TILSOFTAI_SPCatalog exists; applying ALTERs (if needed)...';

    -- Add missing columns (backward compatible)
    IF COL_LENGTH('dbo.TILSOFTAI_SPCatalog', 'IsAtomicCompatible') IS NULL
        ALTER TABLE dbo.TILSOFTAI_SPCatalog ADD IsAtomicCompatible BIT NOT NULL
            CONSTRAINT DF_TILSOFTAI_SPCatalog_IsAtomicCompatible DEFAULT (1);

    IF COL_LENGTH('dbo.TILSOFTAI_SPCatalog', 'Domain') IS NULL
        ALTER TABLE dbo.TILSOFTAI_SPCatalog ADD Domain NVARCHAR(100) NULL;

    IF COL_LENGTH('dbo.TILSOFTAI_SPCatalog', 'Entity') IS NULL
        ALTER TABLE dbo.TILSOFTAI_SPCatalog ADD Entity NVARCHAR(100) NULL;

    IF COL_LENGTH('dbo.TILSOFTAI_SPCatalog', 'Tags') IS NULL
        ALTER TABLE dbo.TILSOFTAI_SPCatalog ADD Tags NVARCHAR(400) NULL;

    IF COL_LENGTH('dbo.TILSOFTAI_SPCatalog', 'IntentVi') IS NULL
        ALTER TABLE dbo.TILSOFTAI_SPCatalog ADD IntentVi NVARCHAR(1000) NULL;

    IF COL_LENGTH('dbo.TILSOFTAI_SPCatalog', 'IntentEn') IS NULL
        ALTER TABLE dbo.TILSOFTAI_SPCatalog ADD IntentEn NVARCHAR(1000) NULL;

    IF COL_LENGTH('dbo.TILSOFTAI_SPCatalog', 'ParamsJson') IS NULL
        ALTER TABLE dbo.TILSOFTAI_SPCatalog ADD ParamsJson NVARCHAR(MAX) NULL;

    IF COL_LENGTH('dbo.TILSOFTAI_SPCatalog', 'ExampleJson') IS NULL
        ALTER TABLE dbo.TILSOFTAI_SPCatalog ADD ExampleJson NVARCHAR(MAX) NULL;

    IF COL_LENGTH('dbo.TILSOFTAI_SPCatalog', 'SchemaHintsJson') IS NULL
        ALTER TABLE dbo.TILSOFTAI_SPCatalog ADD SchemaHintsJson NVARCHAR(MAX) NULL;

    IF COL_LENGTH('dbo.TILSOFTAI_SPCatalog', 'UpdatedAtUtc') IS NULL
        ALTER TABLE dbo.TILSOFTAI_SPCatalog ADD UpdatedAtUtc DATETIME2(0) NOT NULL
            CONSTRAINT DF_TILSOFTAI_SPCatalog_UpdatedAtUtc DEFAULT (SYSUTCDATETIME());

    IF COL_LENGTH('dbo.TILSOFTAI_SPCatalog', 'CreatedAtUtc') IS NULL
        ALTER TABLE dbo.TILSOFTAI_SPCatalog ADD CreatedAtUtc DATETIME2(0) NOT NULL
            CONSTRAINT DF_TILSOFTAI_SPCatalog_CreatedAtUtc DEFAULT (SYSUTCDATETIME());

    -- Ensure unique index exists on SpName
    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE name = 'UX_TILSOFTAI_SPCatalog_SpName'
          AND object_id = OBJECT_ID(N'dbo.TILSOFTAI_SPCatalog')
    )
    BEGIN
        PRINT 'Creating unique index UX_TILSOFTAI_SPCatalog_SpName...';
        CREATE UNIQUE INDEX UX_TILSOFTAI_SPCatalog_SpName
            ON dbo.TILSOFTAI_SPCatalog(SpName);
    END

    -- Ensure enabled index exists
    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE name = 'IX_TILSOFTAI_SPCatalog_Enabled'
          AND object_id = OBJECT_ID(N'dbo.TILSOFTAI_SPCatalog')
    )
    BEGIN
        PRINT 'Creating index IX_TILSOFTAI_SPCatalog_Enabled...';
        CREATE INDEX IX_TILSOFTAI_SPCatalog_Enabled
            ON dbo.TILSOFTAI_SPCatalog(IsEnabled, IsAtomicCompatible)
            INCLUDE (Domain, Entity, Tags, UpdatedAtUtc);
    END
END

-- JSON check constraints (idempotent)
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_TILSOFTAI_SPCatalog_ParamsJson_IsJson')
BEGIN
    ALTER TABLE dbo.TILSOFTAI_SPCatalog
    ADD CONSTRAINT CK_TILSOFTAI_SPCatalog_ParamsJson_IsJson
    CHECK (ParamsJson IS NULL OR ISJSON(ParamsJson) = 1);
END

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_TILSOFTAI_SPCatalog_ExampleJson_IsJson')
BEGIN
    ALTER TABLE dbo.TILSOFTAI_SPCatalog
    ADD CONSTRAINT CK_TILSOFTAI_SPCatalog_ExampleJson_IsJson
    CHECK (ExampleJson IS NULL OR ISJSON(ExampleJson) = 1);
END

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_TILSOFTAI_SPCatalog_SchemaHintsJson_IsJson')
BEGIN
    ALTER TABLE dbo.TILSOFTAI_SPCatalog
    ADD CONSTRAINT CK_TILSOFTAI_SPCatalog_SchemaHintsJson_IsJson
    CHECK (SchemaHintsJson IS NULL OR ISJSON(SchemaHintsJson) = 1);
END

--------------------------------------------------------------------------------
-- 2) dbo.TILSOFTAI_TableKindSignals (optional but recommended)
--------------------------------------------------------------------------------

IF OBJECT_ID(N'dbo.TILSOFTAI_TableKindSignals', N'U') IS NULL
BEGIN
    PRINT 'Creating dbo.TILSOFTAI_TableKindSignals...';

    CREATE TABLE dbo.TILSOFTAI_TableKindSignals
    (
        Id          INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_TILSOFTAI_TableKindSignals PRIMARY KEY,

        TableKind   VARCHAR(30) NOT NULL,     -- e.g., 'summary', 'fact', 'dim'
        Pattern     NVARCHAR(200) NOT NULL,   -- column name or pattern
        Weight      INT NOT NULL CONSTRAINT DF_TK_Weight DEFAULT (1),
        IsRegex     BIT NOT NULL CONSTRAINT DF_TK_IsRegex DEFAULT (0),
        Priority    INT NOT NULL CONSTRAINT DF_TK_Priority DEFAULT (0),
        IsEnabled   BIT NOT NULL CONSTRAINT DF_TK_IsEnabled DEFAULT (1),

        UpdatedAtUtc DATETIME2(0) NOT NULL CONSTRAINT DF_TK_UpdatedAtUtc DEFAULT (SYSUTCDATETIME())
    );

    CREATE INDEX IX_TILSOFTAI_TableKindSignals_Enabled
        ON dbo.TILSOFTAI_TableKindSignals(IsEnabled, Priority);

END
ELSE
BEGIN
    PRINT 'dbo.TILSOFTAI_TableKindSignals exists; applying ALTERs (if needed)...';

    IF COL_LENGTH('dbo.TILSOFTAI_TableKindSignals', 'UpdatedAtUtc') IS NULL
        ALTER TABLE dbo.TILSOFTAI_TableKindSignals ADD UpdatedAtUtc DATETIME2(0) NOT NULL
            CONSTRAINT DF_TK_UpdatedAtUtc DEFAULT (SYSUTCDATETIME());

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE name = 'IX_TILSOFTAI_TableKindSignals_Enabled'
          AND object_id = OBJECT_ID(N'dbo.TILSOFTAI_TableKindSignals')
    )
    BEGIN
        CREATE INDEX IX_TILSOFTAI_TableKindSignals_Enabled
            ON dbo.TILSOFTAI_TableKindSignals(IsEnabled, Priority);
    END
END

--------------------------------------------------------------------------------
-- 3) Optional: Catalog Search SP (simple LIKE-based)
--    If you already have a better/FTS search, you can skip this.
--------------------------------------------------------------------------------
IF OBJECT_ID(N'dbo.TILSOFTAI_sp_catalog_search', N'P') IS NULL
BEGIN
    PRINT 'Creating dbo.TILSOFTAI_sp_catalog_search...';

    EXEC(N'
CREATE PROC dbo.TILSOFTAI_sp_catalog_search
    @query NVARCHAR(4000),
    @topK  INT = 5
AS
BEGIN
    SET NOCOUNT ON;

    SET @topK = CASE
        WHEN @topK IS NULL OR @topK < 1 THEN 5
        WHEN @topK > 25 THEN 25
        ELSE @topK
    END;

    SET @query = NULLIF(LTRIM(RTRIM(@query)), N'''');

    SELECT TOP (@topK)
        CatalogId,
        SpName,
        Domain,
        Entity,
        Tags,
        IntentVi,
        IntentEn,
        ParamsJson,
        ExampleJson,
        SchemaHintsJson,
        IsEnabled,
        IsReadOnly,
        IsAtomicCompatible,
        UpdatedAtUtc
    FROM dbo.TILSOFTAI_SPCatalog WITH (NOLOCK)
    WHERE IsEnabled = 1
      AND IsReadOnly = 1
      AND IsAtomicCompatible = 1
      AND (
            @query IS NULL
            OR SpName LIKE N''%'' + @query + N''%''
            OR Tags LIKE N''%'' + @query + N''%''
            OR IntentVi LIKE N''%'' + @query + N''%''
            OR IntentEn LIKE N''%'' + @query + N''%''
            OR Domain LIKE N''%'' + @query + N''%''
            OR Entity LIKE N''%'' + @query + N''%''
          )
    ORDER BY UpdatedAtUtc DESC;
END
');
END
ELSE
BEGIN
    PRINT 'dbo.TILSOFTAI_sp_catalog_search already exists; leaving as-is.';
END

--------------------------------------------------------------------------------
-- 4) Optional: Seed examples (commented out)
--------------------------------------------------------------------------------
/*
-- Example seed row (adjust SpName + JSON to your SPs)
INSERT INTO dbo.TILSOFTAI_SPCatalog
(
    SpName, Domain, Entity, Tags, IntentEn, ParamsJson, ExampleJson, SchemaHintsJson
)
VALUES
(
    N''dbo.TILSOFTAI_sp_sales_collections'',
    N''Sales'',
    N''Collection'',
    N''sales,collection,join'',
    N''Analyze sales by collection, season, and range'',
    N''[
        { "name":"@Page", "sqlType":"int", "required":false, "default":0, "description_en":"@Page=0 means dataset mode" },
        { "name":"@Size", "sqlType":"int", "required":false, "default":20000, "description_en":"max rows for bounded engine dataset" }
    ]'',
    N''[
        { "q":"Sales by collection", "notes":"Join sales_engine to collections_engine then group and sum amount" },
        { "q":"Top collections by revenue", "notes":"Group by collectionName and sort desc sum(amount)" }
    ]'',
    N''{
        "tables":[
            {
                "tableName":"sales_engine",
                "tableKind":"fact",
                "delivery":"engine",
                "primaryKey":["saleId"],
                "foreignKeys":[{"column":"collectionId","refTable":"collections_engine","refColumn":"collectionId"}],
                "measureHints":["amount","qty"],
                "dimensionHints":["season","date","collectionId"]
            },
            {
                "tableName":"collections_engine",
                "tableKind":"dim",
                "delivery":"engine",
                "primaryKey":["collectionId"],
                "dimensionHints":["collectionName","rangeName"]
            }
        ]
    }''
);
*/

COMMIT TRAN;
PRINT 'TILSOFTAI Atomic Schema V2 applied successfully.';

END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRAN;

    DECLARE @ErrMsg NVARCHAR(4000) = ERROR_MESSAGE();
    DECLARE @ErrNum INT = ERROR_NUMBER();
    DECLARE @ErrState INT = ERROR_STATE();
    DECLARE @ErrLine INT = ERROR_LINE();
    DECLARE @ErrProc NVARCHAR(200) = COALESCE(ERROR_PROCEDURE(), N'(n/a)');

    RAISERROR(
        N'[TILSOFTAI Atomic Schema V2] Failed. Number=%d State=%d Procedure=%s Line=%d Message=%s',
        16, 1,
        @ErrNum, @ErrState, @ErrProc, @ErrLine, @ErrMsg
    );
END CATCH;
