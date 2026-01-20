/*
TILSOFTAI Atomic Rebuild - SQL bootstrap

Purpose:
- Create dbo.TILSOFTAI_SPCatalog (context-rich catalog for LLM SP selection)
- Create dbo.fnc_Split_String helper
- Create/Alter demo SP dbo.TILSOFTAI_sp_models_search (AtomicQuery template)
- Seed dbo.TILSOFTAI_SPCatalog for dbo.TILSOFTAI_sp_models_search

Notes:
- This script is idempotent (safe to run multiple times).
*/

SET NOCOUNT ON;
GO

/* 1) Catalog table */
IF OBJECT_ID('dbo.TILSOFTAI_SPCatalog', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[TILSOFTAI_SPCatalog](
        [CatalogId] [int] IDENTITY(1,1) NOT NULL,
        [SpName] [nvarchar](256) NOT NULL,
        [IsEnabled] [bit] NOT NULL CONSTRAINT [DF_TILSOFTAI_SPCatalog_IsEnabled] DEFAULT ((1)),
        [IsReadOnly] [bit] NOT NULL CONSTRAINT [DF_TILSOFTAI_SPCatalog_IsReadOnly] DEFAULT ((1)),
        [IsAtomicCompatible] [bit] NOT NULL CONSTRAINT [DF_TILSOFTAI_SPCatalog_IsAtomicCompatible] DEFAULT ((1)),
        [Domain] [nvarchar](100) NULL,
        [Entity] [nvarchar](100) NULL,
        [Tags] [nvarchar](400) NULL,
        [IntentVi] [nvarchar](1000) NULL,
        [IntentEn] [nvarchar](1000) NULL,
        [SearchTextVi] [nvarchar](2000) NULL,
        [SearchTextEn] [nvarchar](2000) NULL,
        [ParamsJson] [nvarchar](max) NULL,
        [ExampleJson] [nvarchar](max) NULL,
        [UpdatedAtUtc] [datetime2](0) NOT NULL CONSTRAINT [DF_TILSOFTAI_SPCatalog_UpdatedAtUtc] DEFAULT (sysutcdatetime()),
        CONSTRAINT [PK_TILSOFTAI_SPCatalog] PRIMARY KEY CLUSTERED ([CatalogId] ASC)
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY];
END
GO

/* 2) Split helper (used by the demo SP) */
IF OBJECT_ID('dbo.fnc_Split_String', 'IF') IS NULL
EXEC('CREATE FUNCTION dbo.fnc_Split_String(@List VARCHAR(MAX), @Delim VARCHAR(10))
RETURNS @T TABLE (splitdata VARCHAR(4000))
AS
BEGIN
    DECLARE @pos INT = 1;
    DECLARE @next INT;
    DECLARE @len INT;

    SET @List = ISNULL(@List, '''');
    SET @Delim = ISNULL(@Delim, '','');
    SET @len = LEN(@List);

    IF (@len = 0)
        RETURN;

    WHILE @pos <= @len + 1
    BEGIN
        SET @next = CHARINDEX(@Delim, @List, @pos);
        IF @next = 0
            SET @next = @len + 1;

        INSERT INTO @T(splitdata)
        VALUES (SUBSTRING(@List, @pos, @next - @pos));

        SET @pos = @next + LEN(@Delim);
    END

    RETURN;
END');
GO

/* 3) Demo SP: dbo.TILSOFTAI_sp_models_search (AtomicQuery template) */
CREATE OR ALTER PROC [dbo].[TILSOFTAI_sp_models_search]
    @RangeName   NVARCHAR(100) = NULL,
    @ModelCode   VARCHAR(50)   = NULL,
    @ModelName   NVARCHAR(200) = NULL,
    @Season      VARCHAR(200)  = NULL,
    @Collection  VARCHAR(200)  = NULL,
    @Page        INT           = 1,         -- @Page = 0 => dataset mode for analytics
    @Size        INT           = 20         -- list mode: page size; dataset mode: max rows for engine
AS
BEGIN
    SET NOCOUNT ON;

    SET @RangeName  = NULLIF(LTRIM(RTRIM(@RangeName)), N'');
    SET @ModelName  = NULLIF(LTRIM(RTRIM(@ModelName)), N'');
    SET @ModelCode  = NULLIF(LTRIM(RTRIM(@ModelCode)), '');
    SET @Season     = NULLIF(LTRIM(RTRIM(@Season)), '');
    SET @Collection = NULLIF(LTRIM(RTRIM(@Collection)), '');

    IF (@Page IS NULL) SET @Page = 1;
    IF (@Page < 0) SET @Page = 0;
    IF (@Size IS NULL OR @Size < 1) SET @Size = 20;

    DECLARE @MaxDatasetRows INT = 20000;
    DECLARE @MaxDisplayRows INT = 2000;
    DECLARE @PreviewRows    INT = 100;

    DECLARE @IsDatasetMode BIT = CASE WHEN @Page = 0 THEN 1 ELSE 0 END;

    DECLARE @EngineTake INT =
        CASE WHEN @IsDatasetMode = 1 THEN IIF(@Size > @MaxDatasetRows, @MaxDatasetRows, @Size) ELSE 0 END;

    DECLARE @DisplayTake INT =
        CASE
            WHEN @IsDatasetMode = 1 THEN IIF(@PreviewRows > @MaxDisplayRows, @MaxDisplayRows, @PreviewRows)
            ELSE IIF(@Size > @MaxDisplayRows, @MaxDisplayRows, @Size)
        END;

    DECLARE @Offset BIGINT =
        CASE
            WHEN @IsDatasetMode = 0 THEN (CONVERT(BIGINT, IIF(@Page < 1, 1, @Page)) - 1) * CONVERT(BIGINT, @DisplayTake)
            ELSE 0
        END;

    DECLARE @SeasonSet TABLE (Value VARCHAR(50) NOT NULL PRIMARY KEY);
    IF (@Season IS NOT NULL)
    BEGIN
        INSERT INTO @SeasonSet(Value)
        SELECT DISTINCT LTRIM(RTRIM(splitdata))
        FROM dbo.fnc_Split_String(@Season, ',')
        WHERE NULLIF(LTRIM(RTRIM(splitdata)), '') IS NOT NULL;
    END

    DECLARE @CollectionSet TABLE (Value VARCHAR(100) NOT NULL PRIMARY KEY);
    IF (@Collection IS NOT NULL)
    BEGIN
        INSERT INTO @CollectionSet(Value)
        SELECT DISTINCT LTRIM(RTRIM(splitdata))
        FROM dbo.fnc_Split_String(@Collection, ',')
        WHERE NULLIF(LTRIM(RTRIM(splitdata)), '') IS NOT NULL;
    END

    SELECT
        m.ModelID,
        m.ModelUD,
        m.ModelNM,
        m.Season,
        m.Collection,
        m.RangeName
    INTO #Modelscoped
    FROM dbo.Model m
    WHERE (@RangeName IS NULL OR m.RangeName = @RangeName)
      AND (@ModelCode IS NULL OR m.ModelUD LIKE @ModelCode + '%')
      AND (@ModelName IS NULL OR m.ModelNM LIKE N'%' + @ModelName + N'%')
      AND (
            @Season IS NULL
            OR EXISTS (SELECT 1 FROM @SeasonSet s WHERE s.Value = m.Season)
          )
      AND (
            @Collection IS NULL
            OR EXISTS (SELECT 1 FROM @CollectionSet c WHERE c.Value = m.Collection)
          );

    CREATE INDEX IX_Modelscoped_ModelNM ON #Modelscoped(ModelNM) INCLUDE(ModelID, ModelUD, Season, Collection, RangeName);

    DECLARE @TotalCount INT = (SELECT COUNT(1) FROM #Modelscoped);

    DECLARE @DisplayRows INT =
        CASE
            WHEN @IsDatasetMode = 1 THEN IIF(@TotalCount < @DisplayTake, @TotalCount, @DisplayTake)
            ELSE
                CASE
                    WHEN @Offset >= @TotalCount THEN 0
                    ELSE
                        CASE
                            WHEN (@TotalCount - @Offset) >= @DisplayTake THEN @DisplayTake
                            ELSE CONVERT(INT, @TotalCount - @Offset)
                        END
                END
        END;

    DECLARE @EngineRows INT =
        CASE
            WHEN @IsDatasetMode = 1 THEN IIF(@TotalCount < @EngineTake, @TotalCount, @EngineTake)
            ELSE 0
        END;

    /* RS0: Schema */
    DECLARE @Schema TABLE
    (
        recordType     VARCHAR(10)   NOT NULL,
        resultSetIndex INT           NOT NULL,
        tableName      SYSNAME       NULL,
        tableKind      VARCHAR(30)   NULL,
        delivery       VARCHAR(10)   NULL,
        grain          NVARCHAR(200) NULL,
        primaryKey     NVARCHAR(400) NULL,
        joinHints      NVARCHAR(800) NULL,
        description_vi NVARCHAR(400) NULL,
        description_en NVARCHAR(400) NULL,

        columnName     SYSNAME       NULL,
        ordinal        INT           NULL,
        sqlType        NVARCHAR(128) NULL,
        tabularType    VARCHAR(20)   NULL,
        role           VARCHAR(20)   NULL,
        semanticType   VARCHAR(60)   NULL,
        unit           NVARCHAR(50)  NULL,
        format         NVARCHAR(50)  NULL,
        nullable       BIT           NULL,
        example        NVARCHAR(200) NULL,
        notes          NVARCHAR(400) NULL
    );

    INSERT INTO @Schema(recordType, resultSetIndex, tableName, tableKind, delivery, grain, primaryKey, joinHints, description_vi, description_en)
    VALUES
    ('resultset', 1, 'summary',        'summary',   'display', N'1 row per query', NULL, NULL,
        N'Tổng hợp nhanh (đếm + mode hints).', N'Quick summary (count + mode hints).'),
    ('resultset', 2, 'models_engine',  'dimension', 'engine',  N'1 row per Model. Dataset mode when @Page=0.', N'modelId', NULL,
        N'Dữ liệu cho engine (phân tích).', N'Engine dataset for analytics.'),
    ('resultset', 3, 'models_display', 'dimension', 'display', N'List mode: paged; dataset mode: preview.', N'modelId', NULL,
        N'Danh sách hiển thị (phân trang/preview).', N'Display list (paged/preview).');

    INSERT INTO @Schema(recordType, resultSetIndex, tableName, columnName, ordinal, sqlType, tabularType, role, semanticType, nullable, description_vi, description_en, example, notes)
    VALUES
    ('column', 1, 'summary', 'totalCount',     1, 'int',         'Int32',  'measure',   'count',      0, N'Tổng số model khớp bộ lọc', N'Total matching models', N'1807', NULL),
    ('column', 1, 'summary', 'isDatasetMode',  2, 'bit',         'Boolean','flag',      'mode',       0, N'1 nếu @Page=0', N'1 if @Page=0', N'1', NULL),
    ('column', 1, 'summary', 'page',           3, 'int',         'Int32',  'dimension', 'paging',     0, N'Trang yêu cầu', N'Requested page', N'1', N'@Page=0 nghĩa là dataset mode'),
    ('column', 1, 'summary', 'size',           4, 'int',         'Int32',  'dimension', 'paging',     0, N'Kích thước yêu cầu', N'Requested size', N'20', NULL),
    ('column', 1, 'summary', 'engineRows',     5, 'int',         'Int32',  'measure',   'rowCount',   0, N'Số dòng engine trả về', N'Engine rows returned', N'1807', NULL),
    ('column', 1, 'summary', 'displayRows',    6, 'int',         'Int32',  'measure',   'rowCount',   0, N'Số dòng display trả về', N'Display rows returned', N'20', NULL),
    ('column', 1, 'summary', 'seasonFilter',   7, 'varchar(200)','String', 'filter',    'filter',     1, N'Bộ lọc mùa', N'Season filter', N'24/25', NULL),
    ('column', 1, 'summary', 'collectionFilter', 8, 'varchar(200)','String','filter',   'filter',     1, N'Bộ lọc collection', N'Collection filter', N'SUMMERLAND', NULL),
    ('column', 1, 'summary', 'rangeNameFilter',  9, 'nvarchar(100)','String','filter',  'filter',     1, N'Bộ lọc range', N'Range filter', N'OUTDOOR', NULL),
    ('column', 1, 'summary', 'modelCodeFilter', 10, 'varchar(50)', 'String','filter',  'filter',     1, N'Bộ lọc mã model', N'Model code filter', N'A123', NULL),
    ('column', 1, 'summary', 'modelNameFilter', 11, 'nvarchar(200)','String','filter', 'filter',     1, N'Bộ lọc tên model', N'Model name filter', N'chair', NULL);

    INSERT INTO @Schema(recordType, resultSetIndex, tableName, columnName, ordinal, sqlType, tabularType, role, semanticType, nullable, description_vi, description_en, example)
    VALUES
    ('column', 2, 'models_engine',  'modelId',    1, 'int',          'Int32',  'id',        'identifier', 0, N'ID model', N'Model identifier', N'101'),
    ('column', 2, 'models_engine',  'modelUD',    2, 'nvarchar(50)', 'String', 'dimension', 'code',       1, N'Mã model', N'Model code', N'MDL-0001'),
    ('column', 2, 'models_engine',  'modelNM',    3, 'nvarchar(200)','String', 'dimension', 'name',       1, N'Tên model', N'Model name', N'Wood chair'),
    ('column', 2, 'models_engine',  'season',     4, 'varchar(9)',   'String', 'dimension', 'season',     1, N'Mùa', N'Season', N'24/25'),
    ('column', 2, 'models_engine',  'collection', 5, 'varchar(50)',  'String', 'dimension', 'category',   1, N'Bộ sưu tập', N'Collection', N'SUMMERLAND'),
    ('column', 2, 'models_engine',  'rangeName',  6, 'nvarchar(100)','String', 'dimension', 'category',   1, N'Range', N'Range', N'OUTDOOR'),

    ('column', 3, 'models_display', 'modelId',    1, 'int',          'Int32',  'id',        'identifier', 0, N'ID model', N'Model identifier', N'101'),
    ('column', 3, 'models_display', 'modelUD',    2, 'nvarchar(50)', 'String', 'dimension', 'code',       1, N'Mã model', N'Model code', N'MDL-0001'),
    ('column', 3, 'models_display', 'modelNM',    3, 'nvarchar(200)','String', 'dimension', 'name',       1, N'Tên model', N'Model name', N'Wood chair'),
    ('column', 3, 'models_display', 'season',     4, 'varchar(9)',   'String', 'dimension', 'season',     1, N'Mùa', N'Season', N'24/25'),
    ('column', 3, 'models_display', 'collection', 5, 'varchar(50)',  'String', 'dimension', 'category',   1, N'Bộ sưu tập', N'Collection', N'SUMMERLAND'),
    ('column', 3, 'models_display', 'rangeName',  6, 'nvarchar(100)','String', 'dimension', 'category',   1, N'Range', N'Range', N'OUTDOOR');

    SELECT *
    FROM @Schema
    ORDER BY resultSetIndex, CASE recordType WHEN 'resultset' THEN 0 ELSE 1 END, ordinal;

    /* RS1: Summary */
    SELECT
        @TotalCount        AS totalCount,
        @IsDatasetMode     AS isDatasetMode,
        IIF(@IsDatasetMode = 1, 0, IIF(@Page < 1, 1, @Page)) AS page,
        @Size              AS size,
        @EngineRows        AS engineRows,
        @DisplayRows       AS displayRows,
        @Season            AS seasonFilter,
        @Collection        AS collectionFilter,
        @RangeName         AS rangeNameFilter,
        @ModelCode         AS modelCodeFilter,
        @ModelName         AS modelNameFilter;

    /* RS2: Engine dataset */
    IF (@IsDatasetMode = 1 AND @EngineTake > 0)
    BEGIN
        SELECT TOP (@EngineTake)
            s.ModelID    AS modelId,
            s.ModelUD    AS modelUD,
            s.ModelNM    AS modelNM,
            s.Season     AS season,
            s.Collection AS collection,
            s.RangeName  AS rangeName
        FROM #Modelscoped s
        ORDER BY s.ModelNM;
    END
    ELSE
    BEGIN
        SELECT
            CAST(NULL AS INT)            AS modelId,
            CAST(NULL AS NVARCHAR(50))   AS modelUD,
            CAST(NULL AS NVARCHAR(200))  AS modelNM,
            CAST(NULL AS VARCHAR(9))     AS season,
            CAST(NULL AS VARCHAR(50))    AS collection,
            CAST(NULL AS NVARCHAR(100))  AS rangeName
        WHERE 1 = 0;
    END

    /* RS3: Display list */
    IF (@DisplayTake > 0)
    BEGIN
        SELECT
            s.ModelID    AS modelId,
            s.ModelUD    AS modelUD,
            s.ModelNM    AS modelNM,
            s.Season     AS season,
            s.Collection AS collection,
            s.RangeName  AS rangeName
        FROM #Modelscoped s
        ORDER BY s.ModelNM
        OFFSET @Offset ROWS FETCH NEXT @DisplayTake ROWS ONLY;
    END
    ELSE
    BEGIN
        SELECT
            CAST(NULL AS INT)            AS modelId,
            CAST(NULL AS NVARCHAR(50))   AS modelUD,
            CAST(NULL AS NVARCHAR(200))  AS modelNM,
            CAST(NULL AS VARCHAR(9))     AS season,
            CAST(NULL AS VARCHAR(50))    AS collection,
            CAST(NULL AS NVARCHAR(100))  AS rangeName
        WHERE 1 = 0;
    END
END
GO

/* 4) Seed catalog for demo SP (context-driven) */
DECLARE @SpName NVARCHAR(256) = N'dbo.TILSOFTAI_sp_models_search';

IF EXISTS (SELECT 1 FROM dbo.TILSOFTAI_SPCatalog WHERE SpName = @SpName)
BEGIN
    UPDATE dbo.TILSOFTAI_SPCatalog
    SET
        IsEnabled = 1,
        IsReadOnly = 1,
        IsAtomicCompatible = 1,
        Domain = N'model',
        Entity = N'model',
        Tags = N'model;search;list;analytics;season;collection;range',
        IntentVi = N'Tìm kiếm danh sách model theo điều kiện (mùa/collection/range/mã/tên) hoặc lấy dataset để phân tích. Hỗ trợ list (phân trang) và analytics (@Page=0).',
        IntentEn = N'Search and list models with filters (season/collection/range/code/name) or return an engine dataset for analytics. Supports list paging and dataset mode (@Page=0).',
        SearchTextVi = N'Các câu hỏi thường gặp: "Danh sách model mùa 24/25", "Tìm model theo collection SUMMERLAND", "Model range OUTDOOR", "Phân tích số lượng model theo season/collection", "Top collection có nhiều model".',
        SearchTextEn = N'Common questions: "List models for season 24/25", "Find models by collection SUMMERLAND", "Models in range OUTDOOR", "Analyze model counts by season/collection", "Top collections by model count".',
        ParamsJson = N'[
  {"name":"@RangeName","sqlType":"nvarchar(100)","required":false,"description_vi":"Range name chính xác","description_en":"Exact range name"},
  {"name":"@ModelCode","sqlType":"varchar(50)","required":false,"description_vi":"Prefix mã model (LIKE)","description_en":"Model code prefix (LIKE)"},
  {"name":"@ModelName","sqlType":"nvarchar(200)","required":false,"description_vi":"Tên model contains","description_en":"Model name contains"},
  {"name":"@Season","sqlType":"varchar(200)","required":false,"description_vi":"Mùa (CSV được: 23/24,24/25)","description_en":"Season (CSV allowed: 23/24,24/25)"},
  {"name":"@Collection","sqlType":"varchar(200)","required":false,"description_vi":"Collection (CSV được)","description_en":"Collection (CSV allowed)"},
  {"name":"@Page","sqlType":"int","required":false,"default":1,"description_vi":"Trang (0=dataset mode)","description_en":"Page (0=dataset mode)"},
  {"name":"@Size","sqlType":"int","required":false,"default":20,"description_vi":"Kích thước trang hoặc giới hạn engine","description_en":"Page size or engine cap"}
]',
        ExampleJson = N'{
  "tool": "atomic.query.execute",
  "args": {
    "spName": "dbo.TILSOFTAI_sp_models_search",
    "params": {"@Season":"24/25","@Page":1,"@Size":20}
  }
}',
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE SpName = @SpName;
END
ELSE
BEGIN
    INSERT INTO dbo.TILSOFTAI_SPCatalog
    (
        SpName, IsEnabled, IsReadOnly, IsAtomicCompatible,
        Domain, Entity, Tags,
        IntentVi, IntentEn, SearchTextVi, SearchTextEn,
        ParamsJson, ExampleJson
    )
    VALUES
    (
        @SpName, 1, 1, 1,
        N'model', N'model', N'model;search;list;analytics;season;collection;range',
        N'Tìm kiếm danh sách model theo điều kiện (mùa/collection/range/mã/tên) hoặc lấy dataset để phân tích. Hỗ trợ list (phân trang) và analytics (@Page=0).',
        N'Search and list models with filters (season/collection/range/code/name) or return an engine dataset for analytics. Supports list paging and dataset mode (@Page=0).',
        N'Các câu hỏi thường gặp: "Danh sách model mùa 24/25", "Tìm model theo collection SUMMERLAND", "Model range OUTDOOR", "Phân tích số lượng model theo season/collection", "Top collection có nhiều model".',
        N'Common questions: "List models for season 24/25", "Find models by collection SUMMERLAND", "Models in range OUTDOOR", "Analyze model counts by season/collection", "Top collections by model count".',
        N'[
  {"name":"@RangeName","sqlType":"nvarchar(100)","required":false,"description_vi":"Range name chính xác","description_en":"Exact range name"},
  {"name":"@ModelCode","sqlType":"varchar(50)","required":false,"description_vi":"Prefix mã model (LIKE)","description_en":"Model code prefix (LIKE)"},
  {"name":"@ModelName","sqlType":"nvarchar(200)","required":false,"description_vi":"Tên model contains","description_en":"Model name contains"},
  {"name":"@Season","sqlType":"varchar(200)","required":false,"description_vi":"Mùa (CSV được: 23/24,24/25)","description_en":"Season (CSV allowed: 23/24,24/25)"},
  {"name":"@Collection","sqlType":"varchar(200)","required":false,"description_vi":"Collection (CSV được)","description_en":"Collection (CSV allowed)"},
  {"name":"@Page","sqlType":"int","required":false,"default":1,"description_vi":"Trang (0=dataset mode)","description_en":"Page (0=dataset mode)"},
  {"name":"@Size","sqlType":"int","required":false,"default":20,"description_vi":"Kích thước trang hoặc giới hạn engine","description_en":"Page size or engine cap"}
]',
        N'{
  "tool": "atomic.query.execute",
  "args": {
    "spName": "dbo.TILSOFTAI_sp_models_search",
    "params": {"@Season":"24/25","@Page":1,"@Size":20}
  }
}'
    );
END
GO
