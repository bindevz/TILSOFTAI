IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'model') EXEC(N'CREATE SCHEMA model');
GO

IF OBJECT_ID(N'model.Project', N'U') IS NULL
BEGIN
    CREATE TABLE model.Project (
        TenantId UNIQUEIDENTIFIER NOT NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        ProjectCode NVARCHAR(50) NOT NULL,
        ProjectName NVARCHAR(200) NOT NULL,
        IsActive BIT NOT NULL,
        CONSTRAINT PK_model_Project PRIMARY KEY (TenantId, ProjectId),
        CONSTRAINT UQ_model_Project_Code UNIQUE (TenantId, ProjectCode)
    );
END;
GO

IF OBJECT_ID(N'model.ProjectRun', N'U') IS NULL
BEGIN
    CREATE TABLE model.ProjectRun (
        TenantId UNIQUEIDENTIFIER NOT NULL,
        RunRecordId UNIQUEIDENTIFIER NOT NULL,
        ProjectId UNIQUEIDENTIFIER NOT NULL,
        RunStatus NVARCHAR(50) NOT NULL,
        OverallScore DECIMAL(9,2) NOT NULL,
        FailedCheckCount INT NOT NULL,
        WarningCheckCount INT NOT NULL,
        RunAtUtc DATETIME2 NOT NULL,
        CONSTRAINT PK_model_ProjectRun PRIMARY KEY (TenantId, RunRecordId)
    );
END;
GO

IF OBJECT_ID(N'model.RunCheck', N'U') IS NULL
BEGIN
    CREATE TABLE model.RunCheck (
        TenantId UNIQUEIDENTIFIER NOT NULL,
        RunCheckId UNIQUEIDENTIFIER NOT NULL,
        RunRecordId UNIQUEIDENTIFIER NOT NULL,
        CheckCode NVARCHAR(100) NOT NULL,
        CheckName NVARCHAR(200) NOT NULL,
        CheckStatus NVARCHAR(50) NOT NULL,
        Evidence NVARCHAR(1000) NULL,
        IsSensitive BIT NOT NULL,
        CONSTRAINT PK_model_RunCheck PRIMARY KEY (TenantId, RunCheckId)
    );
END;
GO

