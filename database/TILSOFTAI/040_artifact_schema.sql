IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'artifact') EXEC(N'CREATE SCHEMA artifact');
GO

IF OBJECT_ID(N'artifact.Artifact', N'U') IS NULL
BEGIN
    CREATE TABLE artifact.Artifact (
        ArtifactId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_artifact_Artifact PRIMARY KEY,
        TenantId UNIQUEIDENTIFIER NOT NULL,
        RunId UNIQUEIDENTIFIER NOT NULL,
        ArtifactType NVARCHAR(50) NOT NULL,
        ContentType NVARCHAR(100) NOT NULL,
        StoragePath NVARCHAR(1000) NOT NULL,
        Sha256 NVARCHAR(64) NOT NULL,
        SizeBytes BIGINT NOT NULL,
        CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_artifact_Artifact_CreatedAtUtc DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF OBJECT_ID(N'ai.Provenance', N'U') IS NULL
BEGIN
    CREATE TABLE ai.Provenance (
        ProvenanceId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ai_Provenance PRIMARY KEY,
        RunId UNIQUEIDENTIFIER NOT NULL,
        TenantId UNIQUEIDENTIFIER NOT NULL,
        ToolName NVARCHAR(200) NOT NULL,
        FiltersJson NVARCHAR(MAX) NOT NULL,
        ArtifactId UNIQUEIDENTIFIER NOT NULL,
        CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_ai_Provenance_CreatedAtUtc DEFAULT SYSUTCDATETIME()
    );
END;
GO

