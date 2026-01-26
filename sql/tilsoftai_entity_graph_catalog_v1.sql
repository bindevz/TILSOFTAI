/*
TILSOFTAI - Entity Graph Catalog (EGC) - SQL Schema V1
Authoring intent: Multi-domain smart orchestration with Entity Graph + Drill-down Packs.

This script creates:
- Tables:
  dbo.TILSOFTAI_EntityGraphCatalog
  dbo.TILSOFTAI_EntityGraphPack
  dbo.TILSOFTAI_EntityGraphNode
  dbo.TILSOFTAI_EntityGraphEdge
  dbo.TILSOFTAI_EntityGraphGlossary
- Stored procedures:
  dbo.TILSOFTAI_sp_EntityGraph_Search
  dbo.TILSOFTAI_sp_EntityGraph_Get
- Seeds a demo graphCode = 'product.model' with packs and join hints.
- Ensures dbo.TILSOFTAI_SPCatalog exists (minimal, compatible) and registers demo pack SP names (metadata only).

Notes:
- CREATE/ALTER PROCEDURE statements are separated by GO (SQL Server batch rule).
- This script does NOT create ERP business tables (Model, ModelPiece, ...).
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
  BEGIN TRAN;

  ------------------------------------------------------------------------------
  -- Ensure dbo.TILSOFTAI_SPCatalog exists (minimal compatible definition)
  ------------------------------------------------------------------------------
  IF OBJECT_ID(N'dbo.TILSOFTAI_SPCatalog', N'U') IS NULL
  BEGIN
    CREATE TABLE dbo.TILSOFTAI_SPCatalog
    (
      CatalogId            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TILSOFTAI_SPCatalog PRIMARY KEY,
      SpName               NVARCHAR(256) NOT NULL,
      IsEnabled            BIT NOT NULL CONSTRAINT DF_TILSOFTAI_SPCatalog_IsEnabled DEFAULT (1),
      IsReadOnly           BIT NOT NULL CONSTRAINT DF_TILSOFTAI_SPCatalog_IsReadOnly DEFAULT (1),
      IsAtomicCompatible   BIT NOT NULL CONSTRAINT DF_TILSOFTAI_SPCatalog_IsAtomicCompatible DEFAULT (1),
      Domain               NVARCHAR(100) NULL,
      Entity               NVARCHAR(100) NULL,
      Tags                 NVARCHAR(400) NULL,
      IntentVi             NVARCHAR(1000) NULL,
      IntentEn             NVARCHAR(1000) NULL,
      ParamsJson           NVARCHAR(MAX) NULL,
      ExampleJson          NVARCHAR(MAX) NULL,
      SchemaHintsJson      NVARCHAR(MAX) NULL,
      UpdatedAtUtc         DATETIME2(0) NOT NULL CONSTRAINT DF_TILSOFTAI_SPCatalog_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
      CreatedAtUtc         DATETIME2(0) NOT NULL CONSTRAINT DF_TILSOFTAI_SPCatalog_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX UX_TILSOFTAI_SPCatalog_SpName ON dbo.TILSOFTAI_SPCatalog(SpName);
  END;

  IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_TILSOFTAI_SPCatalog_ParamsJson_IsJson')
    ALTER TABLE dbo.TILSOFTAI_SPCatalog
      ADD CONSTRAINT CK_TILSOFTAI_SPCatalog_ParamsJson_IsJson
      CHECK (ParamsJson IS NULL OR ISJSON(ParamsJson) = 1);

  IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_TILSOFTAI_SPCatalog_ExampleJson_IsJson')
    ALTER TABLE dbo.TILSOFTAI_SPCatalog
      ADD CONSTRAINT CK_TILSOFTAI_SPCatalog_ExampleJson_IsJson
      CHECK (ExampleJson IS NULL OR ISJSON(ExampleJson) = 1);

  IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_TILSOFTAI_SPCatalog_SchemaHintsJson_IsJson')
    ALTER TABLE dbo.TILSOFTAI_SPCatalog
      ADD CONSTRAINT CK_TILSOFTAI_SPCatalog_SchemaHintsJson_IsJson
      CHECK (SchemaHintsJson IS NULL OR ISJSON(SchemaHintsJson) = 1);

  ------------------------------------------------------------------------------
  -- Entity Graph Catalog tables
  ------------------------------------------------------------------------------
  IF OBJECT_ID(N'dbo.TILSOFTAI_EntityGraphCatalog', N'U') IS NULL
  BEGIN
    CREATE TABLE dbo.TILSOFTAI_EntityGraphCatalog
    (
      GraphId       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TILSOFTAI_EntityGraphCatalog PRIMARY KEY,
      GraphCode     NVARCHAR(128) NOT NULL,
      Domain        NVARCHAR(100) NULL,
      Entity        NVARCHAR(100) NULL,
      Tags          NVARCHAR(400) NULL,
      DescriptionVi NVARCHAR(2000) NULL,
      DescriptionEn NVARCHAR(2000) NULL,
      RootSpName    NVARCHAR(256) NULL,
      IsEnabled     BIT NOT NULL CONSTRAINT DF_TILSOFTAI_EGC_IsEnabled DEFAULT (1),
      UpdatedAtUtc  DATETIME2(0) NOT NULL CONSTRAINT DF_TILSOFTAI_EGC_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
      CreatedAtUtc  DATETIME2(0) NOT NULL CONSTRAINT DF_TILSOFTAI_EGC_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX UX_TILSOFTAI_EGC_GraphCode ON dbo.TILSOFTAI_EntityGraphCatalog(GraphCode);
    CREATE INDEX IX_TILSOFTAI_EGC_Enabled ON dbo.TILSOFTAI_EntityGraphCatalog(IsEnabled, Domain, Entity) INCLUDE (GraphCode, Tags, RootSpName, UpdatedAtUtc);
  END;

  IF OBJECT_ID(N'dbo.TILSOFTAI_EntityGraphPack', N'U') IS NULL
  BEGIN
    CREATE TABLE dbo.TILSOFTAI_EntityGraphPack
    (
      PackId               INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TILSOFTAI_EntityGraphPack PRIMARY KEY,
      GraphId              INT NOT NULL CONSTRAINT FK_TILSOFTAI_EGP_GraphId REFERENCES dbo.TILSOFTAI_EntityGraphCatalog(GraphId),
      PackCode             NVARCHAR(128) NOT NULL,
      PackType             NVARCHAR(20) NOT NULL, -- root|detail|dim|compare
      SpName               NVARCHAR(256) NOT NULL,
      Tags                 NVARCHAR(400) NULL,
      IntentVi             NVARCHAR(1000) NULL,
      IntentEn             NVARCHAR(1000) NULL,
      ParamsJson           NVARCHAR(MAX) NULL,
      ExampleJson          NVARCHAR(MAX) NULL,
      ProducesDatasetsJson NVARCHAR(MAX) NULL,
      SortOrder            INT NOT NULL CONSTRAINT DF_TILSOFTAI_EGP_SortOrder DEFAULT (0),
      IsEnabled            BIT NOT NULL CONSTRAINT DF_TILSOFTAI_EGP_IsEnabled DEFAULT (1),
      UpdatedAtUtc         DATETIME2(0) NOT NULL CONSTRAINT DF_TILSOFTAI_EGP_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
      CreatedAtUtc         DATETIME2(0) NOT NULL CONSTRAINT DF_TILSOFTAI_EGP_CreatedAtUtc DEFAULT (SYSUTCDATETIME())
    );
    CREATE UNIQUE INDEX UX_TILSOFTAI_EGP_Graph_PackCode ON dbo.TILSOFTAI_EntityGraphPack(GraphId, PackCode);
    CREATE INDEX IX_TILSOFTAI_EGP_Graph_Enabled ON dbo.TILSOFTAI_EntityGraphPack(GraphId, IsEnabled, PackType, SortOrder) INCLUDE (SpName, Tags);
  END;

  IF OBJECT_ID(N'dbo.TILSOFTAI_EntityGraphNode', N'U') IS NULL
  BEGIN
    CREATE TABLE dbo.TILSOFTAI_EntityGraphNode
    (
      NodeId             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TILSOFTAI_EntityGraphNode PRIMARY KEY,
      GraphId            INT NOT NULL CONSTRAINT FK_TILSOFTAI_EGN_GraphId REFERENCES dbo.TILSOFTAI_EntityGraphCatalog(GraphId),
      DatasetName        NVARCHAR(128) NOT NULL,
      TableKind          NVARCHAR(50) NULL,  -- fact|dim|bridge|display
      Delivery           NVARCHAR(20) NULL,  -- engine|display
      PrimaryKeyJson     NVARCHAR(MAX) NULL, -- JSON array
      IdColumnsJson      NVARCHAR(MAX) NULL, -- JSON array
      DimensionHintsJson NVARCHAR(MAX) NULL, -- JSON array
      MeasureHintsJson   NVARCHAR(MAX) NULL, -- JSON array
      TimeColumnsJson    NVARCHAR(MAX) NULL, -- JSON array
      Notes              NVARCHAR(1000) NULL
    );
    CREATE UNIQUE INDEX UX_TILSOFTAI_EGN_Graph_Dataset ON dbo.TILSOFTAI_EntityGraphNode(GraphId, DatasetName);
  END;

  IF OBJECT_ID(N'dbo.TILSOFTAI_EntityGraphEdge', N'U') IS NULL
  BEGIN
    CREATE TABLE dbo.TILSOFTAI_EntityGraphEdge
    (
      EdgeId         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TILSOFTAI_EntityGraphEdge PRIMARY KEY,
      GraphId        INT NOT NULL CONSTRAINT FK_TILSOFTAI_EGE_GraphId REFERENCES dbo.TILSOFTAI_EntityGraphCatalog(GraphId),
      LeftDataset    NVARCHAR(128) NOT NULL,
      RightDataset   NVARCHAR(128) NOT NULL,
      LeftKeysJson   NVARCHAR(MAX) NOT NULL, -- JSON array
      RightKeysJson  NVARCHAR(MAX) NOT NULL, -- JSON array
      How            NVARCHAR(20) NOT NULL,  -- left|inner
      RightPrefix    NVARCHAR(50) NULL,
      SelectRightJson NVARCHAR(MAX) NULL,    -- JSON array (optional)
      Notes          NVARCHAR(1000) NULL
    );
    CREATE INDEX IX_TILSOFTAI_EGE_Graph_Left ON dbo.TILSOFTAI_EntityGraphEdge(GraphId, LeftDataset) INCLUDE (RightDataset, How);
  END;

  IF OBJECT_ID(N'dbo.TILSOFTAI_EntityGraphGlossary', N'U') IS NULL
  BEGIN
    CREATE TABLE dbo.TILSOFTAI_EntityGraphGlossary
    (
      GlossaryId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TILSOFTAI_EntityGraphGlossary PRIMARY KEY,
      GraphId    INT NOT NULL CONSTRAINT FK_TILSOFTAI_EGG_GraphId REFERENCES dbo.TILSOFTAI_EntityGraphCatalog(GraphId),
      Lang       CHAR(2) NOT NULL,           -- en|vi
      Term       NVARCHAR(200) NOT NULL,
      Canonical  NVARCHAR(200) NOT NULL,
      Notes      NVARCHAR(400) NULL
    );
    CREATE UNIQUE INDEX UX_TILSOFTAI_EGG_UQ ON dbo.TILSOFTAI_EntityGraphGlossary(GraphId, Lang, Term);
  END;

  -- JSON constraints (idempotent)
  DECLARE @JsonChecks TABLE (ConstraintName SYSNAME, TableName SYSNAME, ColumnName SYSNAME);
  INSERT INTO @JsonChecks VALUES
  ('CK_TILSOFTAI_EGP_ParamsJson_IsJson', 'dbo.TILSOFTAI_EntityGraphPack', 'ParamsJson'),
  ('CK_TILSOFTAI_EGP_ExampleJson_IsJson', 'dbo.TILSOFTAI_EntityGraphPack', 'ExampleJson'),
  ('CK_TILSOFTAI_EGP_ProducesDatasetsJson_IsJson', 'dbo.TILSOFTAI_EntityGraphPack', 'ProducesDatasetsJson'),
  ('CK_TILSOFTAI_EGN_PrimaryKeyJson_IsJson', 'dbo.TILSOFTAI_EntityGraphNode', 'PrimaryKeyJson'),
  ('CK_TILSOFTAI_EGN_IdColumnsJson_IsJson', 'dbo.TILSOFTAI_EntityGraphNode', 'IdColumnsJson'),
  ('CK_TILSOFTAI_EGN_DimensionHintsJson_IsJson', 'dbo.TILSOFTAI_EntityGraphNode', 'DimensionHintsJson'),
  ('CK_TILSOFTAI_EGN_MeasureHintsJson_IsJson', 'dbo.TILSOFTAI_EntityGraphNode', 'MeasureHintsJson'),
  ('CK_TILSOFTAI_EGN_TimeColumnsJson_IsJson', 'dbo.TILSOFTAI_EntityGraphNode', 'TimeColumnsJson'),
  ('CK_TILSOFTAI_EGE_LeftKeysJson_IsJson', 'dbo.TILSOFTAI_EntityGraphEdge', 'LeftKeysJson'),
  ('CK_TILSOFTAI_EGE_RightKeysJson_IsJson', 'dbo.TILSOFTAI_EntityGraphEdge', 'RightKeysJson'),
  ('CK_TILSOFTAI_EGE_SelectRightJson_IsJson', 'dbo.TILSOFTAI_EntityGraphEdge', 'SelectRightJson');

  DECLARE @c SYSNAME, @t SYSNAME, @col SYSNAME, @sql NVARCHAR(MAX);
  DECLARE cur CURSOR FAST_FORWARD FOR SELECT ConstraintName, TableName, ColumnName FROM @JsonChecks;
  OPEN cur;
  FETCH NEXT FROM cur INTO @c, @t, @col;
  WHILE @@FETCH_STATUS = 0
  BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = @c)
    BEGIN
      SET @sql = N'ALTER TABLE ' + @t + N' ADD CONSTRAINT ' + QUOTENAME(@c) +
                 N' CHECK (' + QUOTENAME(@col) + N' IS NULL OR ISJSON(' + QUOTENAME(@col) + N') = 1);';
      EXEC sp_executesql @sql;
    END
    FETCH NEXT FROM cur INTO @c, @t, @col;
  END
  CLOSE cur;
  DEALLOCATE cur;

  ------------------------------------------------------------------------------
  -- Seed demo graph: product.model
  ------------------------------------------------------------------------------
  DECLARE @GraphCode NVARCHAR(128) = N'product.model';

  IF NOT EXISTS (SELECT 1 FROM dbo.TILSOFTAI_EntityGraphCatalog WHERE GraphCode = @GraphCode)
  BEGIN
    INSERT INTO dbo.TILSOFTAI_EntityGraphCatalog
      (GraphCode, Domain, Entity, Tags, DescriptionVi, DescriptionEn, RootSpName)
    VALUES
      (@GraphCode, N'Product', N'Model',
       N'model;product;season;range;collection;packaging;piece;material',
       N'Entity graph cho Model: danh sách + drill-down packs (pieces/packaging/materials) + so sánh mùa.',
       N'Entity graph for Model: list + drill-down packs (pieces/packaging/materials) + season comparison.',
       N'dbo.TILSOFTAI_Atomic_Model_Index');
  END;

  DECLARE @GraphId INT = (SELECT GraphId FROM dbo.TILSOFTAI_EntityGraphCatalog WHERE GraphCode = @GraphCode);

  ;WITH src AS (
    SELECT * FROM (VALUES
      (N'root',      N'root',   N'dbo.TILSOFTAI_Atomic_Model_Index',                      0,  N'model;list;summary',        N'List/scope models', N'Danh sách/scope model'),
      (N'pieces',    N'detail', N'dbo.TILSOFTAI_Atomic_Model_Pack_Pieces_ByModelIds',    10, N'model;pack;pieces',         N'Load pieces',       N'Tải pieces'),
      (N'packaging', N'detail', N'dbo.TILSOFTAI_Atomic_Model_Pack_Packaging_ByModelIds', 20, N'model;pack;packaging',      N'Load packaging',    N'Tải packaging'),
      (N'materials', N'detail', N'dbo.TILSOFTAI_Atomic_Model_Pack_Materials_ByModelIds', 30, N'model;pack;materials;wizard', N'Load materials',   N'Tải materials')
    ) v(PackCode, PackType, SpName, SortOrder, Tags, IntentEn, IntentVi)
  )
  MERGE dbo.TILSOFTAI_EntityGraphPack AS tgt
  USING src
    ON tgt.GraphId = @GraphId AND tgt.PackCode = src.PackCode
  WHEN MATCHED THEN UPDATE SET
    PackType = src.PackType, SpName = src.SpName, SortOrder = src.SortOrder,
    Tags = src.Tags, IntentEn = src.IntentEn, IntentVi = src.IntentVi, IsEnabled = 1, UpdatedAtUtc = SYSUTCDATETIME()
  WHEN NOT MATCHED THEN INSERT
    (GraphId, PackCode, PackType, SpName, SortOrder, Tags, IntentEn, IntentVi, IsEnabled)
    VALUES (@GraphId, src.PackCode, src.PackType, src.SpName, src.SortOrder, src.Tags, src.IntentEn, src.IntentVi, 1);

  -- Nodes (dataset roles)
  ;WITH n AS (
    SELECT * FROM (VALUES
      (N'model', N'fact', N'engine', N'["ModelID"]', N'["ModelID","ClientID","ProductTypeID"]', N'["Season","RangeName","ModelUD","ModelNM","ClientID","ProductTypeID"]', N'[]', N'[]', N'Root scope for list/summary.'),
      (N'modelPiece', N'fact', N'engine', N'["ModelPieceID"]', N'["ModelID","PieceModelID"]', N'["PieceModelUD","PieceModelNM"]', N'["Quantity"]', N'[]', N'Model composition pieces.'),
      (N'modelPackagingMethodOption', N'fact', N'engine', N'["ModelPackagingMethodOptionID"]', N'["ModelID","PackagingMethodID"]', N'["IsDefault","MethodCode"]', N'["CBM","NetWeight","GrossWeight","Qnt40HC"]', N'["UpdatedDate"]', N'Packaging/loadability measures.'),
      (N'modelMaterialConfig', N'fact', N'engine', N'["ModelMaterialConfigID"]', N'["ModelID","ProductWizardSectionID"]', N'[]', N'[]', N'[]', N'Links model to wizard sections.'),
      (N'productWizardSection', N'dim', N'engine', N'["ProductWizardSectionID"]', N'["ProductWizardSectionID","MaterialGroupID","ParentID"]', N'["ProductWizardSectionNM","SectionGroupID"]', N'[]', N'[]', N'Wizard sections dimension.'),
      (N'productWizardSectionMaterialGroup', N'fact', N'engine', N'["ProductWizardSectionMaterialGroupID"]', N'["ProductWizardSectionID","MaterialGroupID"]', N'[]', N'[]', N'[]', N'Allowed material groups per section.'),
      (N'materialConfig', N'fact', N'engine', N'["MaterialConfigID"]', N'["MaterialGroupID","MaterialID","MaterialTypeID","MaterialColorID"]', N'[]', N'[]', N'["UpdatedDate"]', N'Material configs by group.'),
      (N'model_preview', N'display', N'display', N'["ModelID"]', N'[]', N'[]', N'[]', N'[]', N'UI preview only.')
    ) v(DatasetName, TableKind, Delivery, PrimaryKeyJson, IdColumnsJson, DimensionHintsJson, MeasureHintsJson, TimeColumnsJson, Notes)
  )
  MERGE dbo.TILSOFTAI_EntityGraphNode AS tgt
  USING n
    ON tgt.GraphId = @GraphId AND tgt.DatasetName = n.DatasetName
  WHEN MATCHED THEN UPDATE SET
    TableKind = n.TableKind, Delivery = n.Delivery,
    PrimaryKeyJson = n.PrimaryKeyJson, IdColumnsJson = n.IdColumnsJson,
    DimensionHintsJson = n.DimensionHintsJson, MeasureHintsJson = n.MeasureHintsJson, TimeColumnsJson = n.TimeColumnsJson,
    Notes = n.Notes
  WHEN NOT MATCHED THEN INSERT
    (GraphId, DatasetName, TableKind, Delivery, PrimaryKeyJson, IdColumnsJson, DimensionHintsJson, MeasureHintsJson, TimeColumnsJson, Notes)
    VALUES (@GraphId, n.DatasetName, n.TableKind, n.Delivery, n.PrimaryKeyJson, n.IdColumnsJson, n.DimensionHintsJson, n.MeasureHintsJson, n.TimeColumnsJson, n.Notes);

  -- Edges (join hints)
  ;WITH e AS (
    SELECT * FROM (VALUES
      (N'model', N'modelPiece', N'["ModelID"]', N'["ModelID"]', N'left', NULL, NULL, N'model -> pieces'),
      (N'model', N'modelPackagingMethodOption', N'["ModelID"]', N'["ModelID"]', N'left', NULL, NULL, N'model -> packaging'),
      (N'model', N'modelMaterialConfig', N'["ModelID"]', N'["ModelID"]', N'left', NULL, NULL, N'model -> mmc'),
      (N'modelMaterialConfig', N'productWizardSection', N'["ProductWizardSectionID"]', N'["ProductWizardSectionID"]', N'left', NULL, NULL, N'mmc -> section'),
      (N'productWizardSection', N'productWizardSection', N'["ParentID"]', N'["ProductWizardSectionID"]', N'left', N'parent_', NULL, N'self parent')
    ) v(LeftDataset, RightDataset, LeftKeysJson, RightKeysJson, How, RightPrefix, SelectRightJson, Notes)
  )
  MERGE dbo.TILSOFTAI_EntityGraphEdge AS tgt
  USING e
    ON tgt.GraphId = @GraphId AND tgt.LeftDataset = e.LeftDataset AND tgt.RightDataset = e.RightDataset AND tgt.How = e.How
  WHEN MATCHED THEN UPDATE SET
    LeftKeysJson = e.LeftKeysJson, RightKeysJson = e.RightKeysJson,
    RightPrefix = e.RightPrefix, SelectRightJson = e.SelectRightJson, Notes = e.Notes
  WHEN NOT MATCHED THEN INSERT
    (GraphId, LeftDataset, RightDataset, LeftKeysJson, RightKeysJson, How, RightPrefix, SelectRightJson, Notes)
    VALUES (@GraphId, e.LeftDataset, e.RightDataset, e.LeftKeysJson, e.RightKeysJson, e.How, e.RightPrefix, e.SelectRightJson, e.Notes);

  -- Glossary (synonyms)
  ;WITH g AS (
    SELECT * FROM (VALUES
      ('en', N'collection', N'RangeName', N'collection maps to RangeName'),
      ('en', N'range',      N'RangeName', N'range maps to RangeName'),
      ('en', N'season',     N'Season',    N'season dimension'),
      ('vi', N'bộ sưu tập', N'RangeName', N'collection ~ RangeName'),
      ('vi', N'range',      N'RangeName', N'range ~ RangeName'),
      ('vi', N'mùa',        N'Season',    N'mùa ~ Season')
    ) v(Lang, Term, Canonical, Notes)
  )
  MERGE dbo.TILSOFTAI_EntityGraphGlossary AS tgt
  USING g
    ON tgt.GraphId = @GraphId AND tgt.Lang = g.Lang AND tgt.Term = g.Term
  WHEN MATCHED THEN UPDATE SET Canonical = g.Canonical, Notes = g.Notes
  WHEN NOT MATCHED THEN INSERT (GraphId, Lang, Term, Canonical, Notes) VALUES (@GraphId, g.Lang, g.Term, g.Canonical, g.Notes);

  -- Register SPs in SPCatalog (metadata only)
  DECLARE @sp TABLE (SpName NVARCHAR(256), Domain NVARCHAR(100), Entity NVARCHAR(100), Tags NVARCHAR(400), IntentEn NVARCHAR(1000), ParamsJson NVARCHAR(MAX), ExampleJson NVARCHAR(MAX), SchemaHintsJson NVARCHAR(MAX));
  INSERT INTO @sp VALUES
  (N'dbo.TILSOFTAI_Atomic_Model_Index', N'Product', N'Model', N'model;graph:product.model;pack:root',
   N'List models (root pack) and create scope for drill-down.',
   N'[{"name":"@Season","sqlType":"varchar(9)","required":false},{"name":"@RangeName","sqlType":"varchar(200)","required":false},{"name":"@ClientID","sqlType":"int","required":false},{"name":"@ProductTypeID","sqlType":"int","required":false},{"name":"@Top","sqlType":"int","required":false,"default":20000,"min":1,"max":20000}]',
   N'[{"lang":"vi","q":"Danh sách model mùa 2024/2025"},{"lang":"en","q":"List models season 2024/2025"}]',
   N'{"graphCode":"product.model","packCode":"root"}'),
  (N'dbo.TILSOFTAI_Atomic_Model_Pack_Pieces_ByModelIds', N'Product', N'Model', N'model;graph:product.model;pack:pieces',
   N'Load pieces for selected ModelID list.',
   N'[{"name":"@ModelIdsJson","sqlType":"nvarchar(max)","required":true},{"name":"@Top","sqlType":"int","required":false,"default":20000,"min":1,"max":20000}]',
   N'[{"lang":"vi","q":"Pieces của model 123"},{"lang":"en","q":"Pieces of model 123"}]',
   N'{"graphCode":"product.model","packCode":"pieces"}'),
  (N'dbo.TILSOFTAI_Atomic_Model_Pack_Packaging_ByModelIds', N'Product', N'Model', N'model;graph:product.model;pack:packaging',
   N'Load packaging for selected ModelID list.',
   N'[{"name":"@ModelIdsJson","sqlType":"nvarchar(max)","required":true},{"name":"@Top","sqlType":"int","required":false,"default":20000,"min":1,"max":20000}]',
   N'[{"lang":"vi","q":"Packaging của model 123"},{"lang":"en","q":"Packaging of model 123"}]',
   N'{"graphCode":"product.model","packCode":"packaging"}'),
  (N'dbo.TILSOFTAI_Atomic_Model_Pack_Materials_ByModelIds', N'Product', N'Model', N'model;graph:product.model;pack:materials',
   N'Load material wizard/config for selected ModelID list.',
   N'[{"name":"@ModelIdsJson","sqlType":"nvarchar(max)","required":true},{"name":"@Top","sqlType":"int","required":false,"default":20000,"min":1,"max":20000}]',
   N'[{"lang":"vi","q":"Material wizard của model 123"},{"lang":"en","q":"Material wizard of model 123"}]',
   N'{"graphCode":"product.model","packCode":"materials"}');

  MERGE dbo.TILSOFTAI_SPCatalog AS tgt
  USING @sp AS src
    ON tgt.SpName = src.SpName
  WHEN MATCHED THEN UPDATE SET
    IsEnabled = 1, IsReadOnly = 1, IsAtomicCompatible = 1,
    Domain = src.Domain, Entity = src.Entity, Tags = src.Tags,
    IntentEn = src.IntentEn, ParamsJson = src.ParamsJson, ExampleJson = src.ExampleJson, SchemaHintsJson = src.SchemaHintsJson,
    UpdatedAtUtc = SYSUTCDATETIME()
  WHEN NOT MATCHED THEN INSERT (SpName, IsEnabled, IsReadOnly, IsAtomicCompatible, Domain, Entity, Tags, IntentEn, ParamsJson, ExampleJson, SchemaHintsJson)
    VALUES (src.SpName, 1, 1, 1, src.Domain, src.Entity, src.Tags, src.IntentEn, src.ParamsJson, src.ExampleJson, src.SchemaHintsJson);

  COMMIT TRAN;
END TRY
BEGIN CATCH
  IF @@TRANCOUNT > 0 ROLLBACK TRAN;
  THROW;
END CATCH
GO

-------------------------------------------------------------------------------
-- Metadata SPs
-------------------------------------------------------------------------------

CREATE OR ALTER PROCEDURE dbo.TILSOFTAI_sp_EntityGraph_Search
  @Query NVARCHAR(256),
  @TopK  INT = 5
AS
BEGIN
  SET NOCOUNT ON;

  SET @Query = LTRIM(RTRIM(ISNULL(@Query, N'')));
  SET @TopK = CASE WHEN @TopK IS NULL OR @TopK < 1 THEN 5 WHEN @TopK > 20 THEN 20 ELSE @TopK END;

  DECLARE @TopGraphs TABLE
  (
    GraphId INT NOT NULL PRIMARY KEY,
    GraphCode NVARCHAR(128) NOT NULL,
    Domain NVARCHAR(100) NULL,
    Entity NVARCHAR(100) NULL,
    Tags NVARCHAR(400) NULL,
    RootSpName NVARCHAR(256) NULL,
    DescriptionVi NVARCHAR(2000) NULL,
    DescriptionEn NVARCHAR(2000) NULL,
    Score INT NOT NULL,
    UpdatedAtUtc DATETIME2(0) NOT NULL
  );

  INSERT INTO @TopGraphs (GraphId, GraphCode, Domain, Entity, Tags, RootSpName, DescriptionVi, DescriptionEn, Score, UpdatedAtUtc)
  SELECT TOP (@TopK)
      eg.GraphId, eg.GraphCode, eg.Domain, eg.Entity, eg.Tags, eg.RootSpName, eg.DescriptionVi, eg.DescriptionEn,
      CASE
        WHEN eg.GraphCode = @Query THEN 1000
        WHEN eg.GraphCode LIKE N'%' + @Query + N'%' THEN 500
        WHEN eg.Tags LIKE N'%' + @Query + N'%' THEN 250
        WHEN eg.Domain LIKE N'%' + @Query + N'%' THEN 150
        WHEN eg.Entity LIKE N'%' + @Query + N'%' THEN 150
        WHEN eg.DescriptionVi LIKE N'%' + @Query + N'%' THEN 120
        WHEN eg.DescriptionEn LIKE N'%' + @Query + N'%' THEN 120
        ELSE 1
      END AS Score,
      eg.UpdatedAtUtc
  FROM dbo.TILSOFTAI_EntityGraphCatalog eg WITH (NOLOCK)
  WHERE eg.IsEnabled = 1
    AND (
      @Query = N''
      OR eg.GraphCode LIKE N'%' + @Query + N'%'
      OR eg.Tags LIKE N'%' + @Query + N'%'
      OR eg.Domain LIKE N'%' + @Query + N'%'
      OR eg.Entity LIKE N'%' + @Query + N'%'
      OR eg.DescriptionVi LIKE N'%' + @Query + N'%'
      OR eg.DescriptionEn LIKE N'%' + @Query + N'%'
    )
  ORDER BY Score DESC, eg.UpdatedAtUtc DESC;

  -- RS1: graphs
  SELECT
    GraphId, GraphCode, Domain, Entity, Tags, RootSpName, DescriptionVi, DescriptionEn, Score, UpdatedAtUtc
  FROM @TopGraphs
  ORDER BY Score DESC, UpdatedAtUtc DESC;

  -- RS2: packs only for selected graphs
  SELECT
    p.GraphId, p.PackCode, p.PackType, p.SpName, p.Tags, p.SortOrder, p.ParamsJson, p.ProducesDatasetsJson
  FROM dbo.TILSOFTAI_EntityGraphPack p WITH (NOLOCK)
  WHERE p.IsEnabled = 1
    AND EXISTS (SELECT 1 FROM @TopGraphs tg WHERE tg.GraphId = p.GraphId)
  ORDER BY p.GraphId, p.SortOrder, p.PackType, p.PackCode;
END
GO


CREATE OR ALTER PROCEDURE dbo.TILSOFTAI_sp_EntityGraph_Get
  @GraphCode NVARCHAR(128)
AS
BEGIN
  SET NOCOUNT ON;
  SET @GraphCode = LTRIM(RTRIM(ISNULL(@GraphCode, N'')));
  IF @GraphCode = N'' THROW 50000, 'GraphCode is required.', 1;

  DECLARE @GraphId INT;
  SELECT @GraphId = GraphId FROM dbo.TILSOFTAI_EntityGraphCatalog WITH (NOLOCK) WHERE GraphCode = @GraphCode AND IsEnabled = 1;
  IF @GraphId IS NULL THROW 50001, 'GraphCode not found or disabled.', 1;

  SELECT GraphId, GraphCode, Domain, Entity, Tags, RootSpName, DescriptionVi, DescriptionEn, UpdatedAtUtc, CreatedAtUtc
  FROM dbo.TILSOFTAI_EntityGraphCatalog WITH (NOLOCK) WHERE GraphId = @GraphId;

  SELECT PackId, GraphId, PackCode, PackType, SpName, Tags, IntentVi, IntentEn, ParamsJson, ExampleJson, ProducesDatasetsJson, SortOrder
  FROM dbo.TILSOFTAI_EntityGraphPack WITH (NOLOCK)
  WHERE GraphId = @GraphId AND IsEnabled = 1
  ORDER BY SortOrder, PackType, PackCode;

  SELECT NodeId, GraphId, DatasetName, TableKind, Delivery, PrimaryKeyJson, IdColumnsJson, DimensionHintsJson, MeasureHintsJson, TimeColumnsJson, Notes
  FROM dbo.TILSOFTAI_EntityGraphNode WITH (NOLOCK)
  WHERE GraphId = @GraphId
  ORDER BY DatasetName;

  SELECT EdgeId, GraphId, LeftDataset, RightDataset, LeftKeysJson, RightKeysJson, How, RightPrefix, SelectRightJson, Notes
  FROM dbo.TILSOFTAI_EntityGraphEdge WITH (NOLOCK)
  WHERE GraphId = @GraphId
  ORDER BY LeftDataset, RightDataset;

  SELECT GlossaryId, GraphId, Lang, Term, Canonical, Notes
  FROM dbo.TILSOFTAI_EntityGraphGlossary WITH (NOLOCK)
  WHERE GraphId = @GraphId
  ORDER BY Lang, Term;
END
GO
