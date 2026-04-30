IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'core') EXEC(N'CREATE SCHEMA core');
GO

IF OBJECT_ID(N'core.Tenant', N'U') IS NULL
BEGIN
    CREATE TABLE core.Tenant (
        TenantId UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_core_Tenant PRIMARY KEY,
        TenantCode NVARCHAR(50) NOT NULL CONSTRAINT UQ_core_Tenant_TenantCode UNIQUE,
        TenantName NVARCHAR(200) NOT NULL,
        IsActive BIT NOT NULL,
        CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_core_Tenant_CreatedAtUtc DEFAULT SYSUTCDATETIME()
    );
END;
GO

IF OBJECT_ID(N'core.AppUser', N'U') IS NULL
BEGIN
    CREATE TABLE core.AppUser (
        TenantId UNIQUEIDENTIFIER NOT NULL,
        UserId UNIQUEIDENTIFIER NOT NULL,
        UserName NVARCHAR(100) NOT NULL,
        DisplayName NVARCHAR(200) NULL,
        Email NVARCHAR(255) NULL,
        IsActive BIT NOT NULL,
        CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_core_AppUser_CreatedAtUtc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_core_AppUser PRIMARY KEY (TenantId, UserId),
        CONSTRAINT FK_core_AppUser_Tenant FOREIGN KEY (TenantId) REFERENCES core.Tenant(TenantId)
    );
END;
GO

