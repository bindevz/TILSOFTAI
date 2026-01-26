/*
TILSOFTAI - AI Context Store (Document RAG) for SQL Server 2025

Design notes:
- SQL Server 2025 vector indexes are currently preview and have important limitations:
  * The table must have a single-column INT/BIGINT clustered primary key.
  * A table with a vector index becomes read-only; to ingest continuously, we use Delta (writable) + Main (read-only with vector index).
  * The vector index is not incrementally maintained; refresh requires drop + rebuild.

References (Microsoft Learn):
- CREATE VECTOR INDEX (Preview)
- VECTOR_SEARCH (Preview)
*/

-- Enable preview features for vector search (required in SQL Server 2025 preview)
ALTER DATABASE SCOPED CONFIGURATION SET PREVIEW_FEATURES = ON;
GO

-------------------------------------------------------------------------------
-- Core document metadata
-------------------------------------------------------------------------------
IF OBJECT_ID('dbo.TILSOFTAI_Document', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TILSOFTAI_Document
    (
        DocId           INT IDENTITY(1,1) NOT NULL,
        Source          NVARCHAR(100) NULL,          -- e.g. 'share', 's3', 'db'
        ExternalId      NVARCHAR(200) NULL,          -- id/path in source system
        Title           NVARCHAR(500) NULL,
        Domain          NVARCHAR(100) NULL,
        DocType         NVARCHAR(100) NULL,
        Uri             NVARCHAR(1000) NULL,
        TagsJson        NVARCHAR(MAX) NULL,
        ContentHash     VARBINARY(32) NULL,          -- SHA-256 of full content (optional)
        ActiveVersion   INT NOT NULL CONSTRAINT DF_TILSOFTAI_Document_ActiveVersion DEFAULT(1),
        IsDeleted       BIT NOT NULL CONSTRAINT DF_TILSOFTAI_Document_IsDeleted DEFAULT(0),
        CreatedAtUtc    DATETIME2(0) NOT NULL CONSTRAINT DF_TILSOFTAI_Document_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
        UpdatedAtUtc    DATETIME2(0) NOT NULL CONSTRAINT DF_TILSOFTAI_Document_UpdatedAtUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_TILSOFTAI_Document PRIMARY KEY CLUSTERED (DocId)
    );

    CREATE INDEX IX_TILSOFTAI_Document_SourceExternal ON dbo.TILSOFTAI_Document(Source, ExternalId);
END
GO

-------------------------------------------------------------------------------
-- Delta table (writable): continuous ingestion lands here
-------------------------------------------------------------------------------
IF OBJECT_ID('dbo.TILSOFTAI_DocChunkDelta', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TILSOFTAI_DocChunkDelta
    (
        DeltaId         BIGINT IDENTITY(1,1) NOT NULL,
        DocId           INT NOT NULL,
        DocVersion      INT NOT NULL,
        ChunkNo         INT NOT NULL,
        Content         NVARCHAR(MAX) NOT NULL,
        ContentHash     VARBINARY(32) NULL,          -- SHA-256 of chunk content (optional)
        Embedding       VECTOR(1536) NOT NULL,
        CreatedAtUtc    DATETIME2(0) NOT NULL CONSTRAINT DF_TILSOFTAI_DocChunkDelta_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_TILSOFTAI_DocChunkDelta PRIMARY KEY CLUSTERED (DeltaId),
        CONSTRAINT FK_TILSOFTAI_DocChunkDelta_Document FOREIGN KEY (DocId) REFERENCES dbo.TILSOFTAI_Document(DocId)
    );

    CREATE INDEX IX_TILSOFTAI_DocChunkDelta_Doc ON dbo.TILSOFTAI_DocChunkDelta(DocId, DocVersion, ChunkNo);
END
GO

-------------------------------------------------------------------------------
-- Main table (read-optimized): rebuilt periodically; vector index lives here
-------------------------------------------------------------------------------
IF OBJECT_ID('dbo.TILSOFTAI_DocChunkMain', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TILSOFTAI_DocChunkMain
    (
        ChunkId         BIGINT IDENTITY(1,1) NOT NULL, -- REQUIRED: single-column integer clustered PK for vector index
        DocId           INT NOT NULL,
        DocVersion      INT NOT NULL,
        ChunkNo         INT NOT NULL,
        Content         NVARCHAR(MAX) NOT NULL,
        ContentHash     VARBINARY(32) NULL,
        Embedding       VECTOR(1536) NOT NULL,
        RefreshedAtUtc  DATETIME2(0) NOT NULL CONSTRAINT DF_TILSOFTAI_DocChunkMain_RefreshedAtUtc DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_TILSOFTAI_DocChunkMain PRIMARY KEY CLUSTERED (ChunkId),
        CONSTRAINT FK_TILSOFTAI_DocChunkMain_Document FOREIGN KEY (DocId) REFERENCES dbo.TILSOFTAI_Document(DocId)
    );

    CREATE INDEX IX_TILSOFTAI_DocChunkMain_Doc ON dbo.TILSOFTAI_DocChunkMain(DocId, DocVersion, ChunkNo);
END
GO

-------------------------------------------------------------------------------
-- Vector index (DiskANN) - drop/recreate when refreshing main
-------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'VEC_TILSOFTAI_DocChunkMain_Embedding' AND object_id = OBJECT_ID('dbo.TILSOFTAI_DocChunkMain'))
BEGIN
    -- NOTE: While this vector index exists, the table is read-only in SQL Server 2025 preview.
    CREATE VECTOR INDEX VEC_TILSOFTAI_DocChunkMain_Embedding
        ON dbo.TILSOFTAI_DocChunkMain (Embedding)
        WITH (METRIC = 'COSINE', TYPE = 'DISKANN');
END
GO

-------------------------------------------------------------------------------
-- Stored procedure: insert/update document metadata
-------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.TILSOFTAI_sp_Document_Upsert
    @Source NVARCHAR(100) = NULL,
    @ExternalId NVARCHAR(200) = NULL,
    @Title NVARCHAR(500) = NULL,
    @Domain NVARCHAR(100) = NULL,
    @DocType NVARCHAR(100) = NULL,
    @Uri NVARCHAR(1000) = NULL,
    @TagsJson NVARCHAR(MAX) = NULL,
    @ContentHash VARBINARY(32) = NULL,
    @ActiveVersion INT = 1,
    @DocId INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF @ActiveVersion IS NULL OR @ActiveVersion < 1 SET @ActiveVersion = 1;

    DECLARE @ExistingId INT;
    SELECT @ExistingId = DocId
    FROM dbo.TILSOFTAI_Document WITH (UPDLOCK, HOLDLOCK)
    WHERE ISNULL(Source, N'') = ISNULL(@Source, N'')
      AND ISNULL(ExternalId, N'') = ISNULL(@ExternalId, N'');

    IF @ExistingId IS NULL
    BEGIN
        INSERT INTO dbo.TILSOFTAI_Document (Source, ExternalId, Title, Domain, DocType, Uri, TagsJson, ContentHash, ActiveVersion)
        VALUES (@Source, @ExternalId, @Title, @Domain, @DocType, @Uri, @TagsJson, @ContentHash, @ActiveVersion);

        SET @DocId = SCOPE_IDENTITY();
        RETURN;
    END

    UPDATE dbo.TILSOFTAI_Document
    SET Title = COALESCE(@Title, Title),
        Domain = COALESCE(@Domain, Domain),
        DocType = COALESCE(@DocType, DocType),
        Uri = COALESCE(@Uri, Uri),
        TagsJson = COALESCE(@TagsJson, TagsJson),
        ContentHash = COALESCE(@ContentHash, ContentHash),
        ActiveVersion = @ActiveVersion,
        IsDeleted = 0,
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE DocId = @ExistingId;

    SET @DocId = @ExistingId;
END
GO

-------------------------------------------------------------------------------
-- Stored procedure: insert a chunk into Delta (writable)
-------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.TILSOFTAI_sp_DocChunkDelta_Insert
    @DocId INT,
    @DocVersion INT,
    @ChunkNo INT,
    @Content NVARCHAR(MAX),
    @ContentHash VARBINARY(32) = NULL,
    @EmbeddingJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    IF @DocVersion IS NULL OR @DocVersion < 1 SET @DocVersion = 1;

    DECLARE @v VECTOR(1536) = @EmbeddingJson;

    INSERT INTO dbo.TILSOFTAI_DocChunkDelta (DocId, DocVersion, ChunkNo, Content, ContentHash, Embedding)
    VALUES (@DocId, @DocVersion, @ChunkNo, @Content, @ContentHash, @v);
END
GO

-------------------------------------------------------------------------------
-- Stored procedure: rebuild Main from Delta (drop/recreate vector index)
-- This is designed to be executed during a maintenance window.
-------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.TILSOFTAI_sp_DocChunkMain_Rebuild
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'VEC_TILSOFTAI_DocChunkMain_Embedding' AND object_id = OBJECT_ID('dbo.TILSOFTAI_DocChunkMain'))
    BEGIN
        DROP INDEX VEC_TILSOFTAI_DocChunkMain_Embedding ON dbo.TILSOFTAI_DocChunkMain;
    END

    TRUNCATE TABLE dbo.TILSOFTAI_DocChunkMain;

    ;WITH Latest AS
    (
        SELECT
            d.DocId,
            d.ActiveVersion AS DocVersion,
            dc.ChunkNo,
            dc.Content,
            dc.ContentHash,
            dc.Embedding,
            ROW_NUMBER() OVER (PARTITION BY d.DocId, dc.ChunkNo ORDER BY dc.DeltaId DESC) AS rn
        FROM dbo.TILSOFTAI_Document d
        JOIN dbo.TILSOFTAI_DocChunkDelta dc ON dc.DocId = d.DocId AND dc.DocVersion = d.ActiveVersion
        WHERE d.IsDeleted = 0
    )
    INSERT INTO dbo.TILSOFTAI_DocChunkMain (DocId, DocVersion, ChunkNo, Content, ContentHash, Embedding, RefreshedAtUtc)
    SELECT DocId, DocVersion, ChunkNo, Content, ContentHash, Embedding, SYSUTCDATETIME()
    FROM Latest
    WHERE rn = 1;

    -- Re-create vector index
    CREATE VECTOR INDEX VEC_TILSOFTAI_DocChunkMain_Embedding
        ON dbo.TILSOFTAI_DocChunkMain (Embedding)
        WITH (METRIC = 'COSINE', TYPE = 'DISKANN');
END
GO

-------------------------------------------------------------------------------
-- Stored procedure: vector search on Main
-------------------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.TILSOFTAI_sp_DocChunk_Search
    @QueryEmbeddingJson NVARCHAR(MAX),
    @TopK INT = 5
AS
BEGIN
    SET NOCOUNT ON;

    SET @TopK = CASE WHEN @TopK IS NULL OR @TopK < 1 THEN 5 WHEN @TopK > 20 THEN 20 ELSE @TopK END;

    DECLARE @qv VECTOR(1536) = @QueryEmbeddingJson;

    SELECT TOP (@TopK)
        c.DocId,
        c.ChunkId,
        c.ChunkNo,
        d.Title,
        d.Uri,
        LEFT(c.Content, 1200) AS Snippet,
        s.distance AS Distance
    FROM VECTOR_SEARCH(
            TABLE = dbo.TILSOFTAI_DocChunkMain AS c,
            COLUMN = Embedding,
            SIMILAR_TO = @qv,
            METRIC = 'cosine',
            TOP_N = @TopK
        ) AS s
    JOIN dbo.TILSOFTAI_Document d ON d.DocId = c.DocId
    WHERE d.IsDeleted = 0
    ORDER BY s.distance ASC, c.ChunkId ASC;
END
GO
