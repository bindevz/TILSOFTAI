IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'security') EXEC(N'CREATE SCHEMA security');
GO

IF OBJECT_ID(N'security.Role', N'U') IS NULL
BEGIN
    CREATE TABLE security.Role (
        TenantId UNIQUEIDENTIFIER NOT NULL,
        RoleId UNIQUEIDENTIFIER NOT NULL,
        RoleCode NVARCHAR(100) NOT NULL,
        RoleName NVARCHAR(200) NOT NULL,
        IsActive BIT NOT NULL,
        CONSTRAINT PK_security_Role PRIMARY KEY (TenantId, RoleId)
    );
END;
GO

IF OBJECT_ID(N'security.Permission', N'U') IS NULL
BEGIN
    CREATE TABLE security.Permission (
        PermissionCode NVARCHAR(150) NOT NULL CONSTRAINT PK_security_Permission PRIMARY KEY,
        PermissionName NVARCHAR(200) NOT NULL,
        Description NVARCHAR(1000) NULL
    );
END;
GO

IF OBJECT_ID(N'security.UserRole', N'U') IS NULL
BEGIN
    CREATE TABLE security.UserRole (
        TenantId UNIQUEIDENTIFIER NOT NULL,
        UserId UNIQUEIDENTIFIER NOT NULL,
        RoleId UNIQUEIDENTIFIER NOT NULL,
        CONSTRAINT PK_security_UserRole PRIMARY KEY (TenantId, UserId, RoleId)
    );
END;
GO

IF OBJECT_ID(N'security.RolePermission', N'U') IS NULL
BEGIN
    CREATE TABLE security.RolePermission (
        TenantId UNIQUEIDENTIFIER NOT NULL,
        RoleId UNIQUEIDENTIFIER NOT NULL,
        PermissionCode NVARCHAR(150) NOT NULL,
        CONSTRAINT PK_security_RolePermission PRIMARY KEY (TenantId, RoleId, PermissionCode)
    );
END;
GO

CREATE OR ALTER FUNCTION security.UserEffectivePermission
(
    @TenantId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER
)
RETURNS TABLE
AS
RETURN
(
    SELECT DISTINCT rp.PermissionCode
    FROM core.Tenant t
    INNER JOIN core.AppUser u ON u.TenantId = t.TenantId
    INNER JOIN security.UserRole ur ON ur.TenantId = u.TenantId AND ur.UserId = u.UserId
    INNER JOIN security.Role r ON r.TenantId = ur.TenantId AND r.RoleId = ur.RoleId AND r.IsActive = 1
    INNER JOIN security.RolePermission rp ON rp.TenantId = r.TenantId AND rp.RoleId = r.RoleId
    WHERE t.TenantId = @TenantId
      AND u.UserId = @UserId
      AND t.IsActive = 1
      AND u.IsActive = 1
);
GO

