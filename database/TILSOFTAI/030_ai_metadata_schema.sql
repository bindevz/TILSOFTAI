IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'ai') EXEC(N'CREATE SCHEMA ai');
GO

IF OBJECT_ID(N'ai.Module', N'U') IS NULL
BEGIN
    CREATE TABLE ai.Module (
        ModuleId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ai_Module PRIMARY KEY,
        ModuleCode NVARCHAR(50) NOT NULL CONSTRAINT UQ_ai_Module_ModuleCode UNIQUE,
        ModuleName NVARCHAR(200) NOT NULL,
        Description NVARCHAR(1000) NULL,
        IsActive BIT NOT NULL,
        Version NVARCHAR(50) NOT NULL,
        CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_ai_Module_CreatedAtUtc DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF OBJECT_ID(N'ai.Capability', N'U') IS NULL
BEGIN
    CREATE TABLE ai.Capability (
        CapabilityId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ai_Capability PRIMARY KEY,
        ModuleId UNIQUEIDENTIFIER NOT NULL,
        CapabilityCode NVARCHAR(150) NOT NULL CONSTRAINT UQ_ai_Capability_CapabilityCode UNIQUE,
        CapabilityName NVARCHAR(200) NOT NULL,
        Description NVARCHAR(MAX) NOT NULL,
        BusinessPurpose NVARCHAR(MAX) NULL,
        SupportedLanguages NVARCHAR(500) NULL,
        RiskLevel NVARCHAR(50) NOT NULL,
        IsActive BIT NOT NULL,
        CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_ai_Capability_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_ai_Capability_Module FOREIGN KEY (ModuleId) REFERENCES ai.Module(ModuleId)
    );
END;
GO

IF OBJECT_ID(N'ai.Tool', N'U') IS NULL
BEGIN
    CREATE TABLE ai.Tool (
        ToolId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ai_Tool PRIMARY KEY,
        CapabilityId UNIQUEIDENTIFIER NOT NULL,
        ToolName NVARCHAR(200) NOT NULL CONSTRAINT UQ_ai_Tool_ToolName UNIQUE,
        ToolType NVARCHAR(50) NOT NULL,
        Description NVARCHAR(MAX) NOT NULL,
        InputJsonSchema NVARCHAR(MAX) NOT NULL,
        OutputJsonSchema NVARCHAR(MAX) NOT NULL,
        RequiredPermissionCode NVARCHAR(150) NOT NULL,
        SqlProcedureName NVARCHAR(255) NULL,
        MaxRows INT NOT NULL,
        TimeoutMs INT NOT NULL,
        IsActive BIT NOT NULL,
        CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_ai_Tool_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_ai_Tool_Capability FOREIGN KEY (CapabilityId) REFERENCES ai.Capability(CapabilityId)
    );
END;
GO

IF OBJECT_ID(N'ai.Embedding', N'U') IS NULL
BEGIN
    CREATE TABLE ai.Embedding (
        EmbeddingId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ai_Embedding PRIMARY KEY,
        EntityType NVARCHAR(50) NOT NULL,
        EntityId UNIQUEIDENTIFIER NOT NULL,
        EmbeddingModel NVARCHAR(200) NOT NULL,
        SourceText NVARCHAR(MAX) NOT NULL,
        SourceTextHash VARBINARY(32) NOT NULL,
        EmbeddingVector VECTOR(1536) NULL,
        CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_ai_Embedding_CreatedAtUtc DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF OBJECT_ID(N'ai.Run', N'U') IS NULL
BEGIN
    CREATE TABLE ai.Run (
        RunId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ai_Run PRIMARY KEY,
        TenantId UNIQUEIDENTIFIER NOT NULL,
        UserId UNIQUEIDENTIFIER NOT NULL,
        CorrelationId NVARCHAR(100) NULL,
        Question NVARCHAR(MAX) NOT NULL,
        DetectedLanguage NVARCHAR(20) NULL,
        DomainHint NVARCHAR(100) NULL,
        SelectedModuleCode NVARCHAR(50) NULL,
        SelectedCapabilityCode NVARCHAR(150) NULL,
        Status NVARCHAR(50) NOT NULL,
        CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_ai_Run_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CompletedAtUtc DATETIME2 NULL
    );
END;
GO

IF OBJECT_ID(N'ai.ToolCall', N'U') IS NULL
BEGIN
    CREATE TABLE ai.ToolCall (
        ToolCallId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ai_ToolCall PRIMARY KEY,
        RunId UNIQUEIDENTIFIER NOT NULL,
        TenantId UNIQUEIDENTIFIER NOT NULL,
        UserId UNIQUEIDENTIFIER NOT NULL,
        ToolName NVARCHAR(200) NOT NULL,
        ParametersJson NVARCHAR(MAX) NOT NULL,
        Status NVARCHAR(50) NOT NULL,
        RowCount INT NOT NULL,
        StartedAtUtc DATETIME2 NOT NULL,
        CompletedAtUtc DATETIME2 NULL
    );
END;
GO

