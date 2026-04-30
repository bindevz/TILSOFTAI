DECLARE @ModuleId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000001001';
DECLARE @VerifyCapabilityId UNIQUEIDENTIFIER = '10000000-0000-0000-0000-000000000001';
DECLARE @LatestCapabilityId UNIQUEIDENTIFIER = '10000000-0000-0000-0000-000000000002';
DECLARE @FailedCapabilityId UNIQUEIDENTIFIER = '10000000-0000-0000-0000-000000000003';

MERGE ai.Capability AS target
USING (VALUES
    (@VerifyCapabilityId, @ModuleId, N'model.project.run.verify', N'Verify Model project run', N'Determines whether a Model project achieved the run target.', N'Run verification', N'en,vi', N'Low', 1),
    (@LatestCapabilityId, @ModuleId, N'model.project.run.latest', N'Latest Model project run', N'Summarizes latest run status for a Model project.', N'Latest run status', N'en,vi', N'Low', 1),
    (@FailedCapabilityId, @ModuleId, N'model.project.run.failed_checks', N'Failed Model run checks', N'Lists failed checks in the latest Model project run.', N'Failed checks', N'en,vi', N'Low', 1)
) AS source(CapabilityId, ModuleId, CapabilityCode, CapabilityName, Description, BusinessPurpose, SupportedLanguages, RiskLevel, IsActive)
ON target.CapabilityId = source.CapabilityId
WHEN NOT MATCHED THEN INSERT (CapabilityId, ModuleId, CapabilityCode, CapabilityName, Description, BusinessPurpose, SupportedLanguages, RiskLevel, IsActive)
VALUES (source.CapabilityId, source.ModuleId, source.CapabilityCode, source.CapabilityName, source.Description, source.BusinessPurpose, source.SupportedLanguages, source.RiskLevel, source.IsActive);

MERGE ai.Tool AS target
USING (VALUES
    ('20000000-0000-0000-0000-000000000001', @VerifyCapabilityId, N'Model.GetProjectRunVerification', N'SqlStoredProcedure', N'Gets Model project run verification.', N'{"type":"object","required":["projectCode"]}', N'{"type":"array"}', N'model.project.run.read', N'model.usp_GetProjectRunVerification', 5000, 30000, 1),
    ('20000000-0000-0000-0000-000000000002', @LatestCapabilityId, N'Model.GetLatestProjectRun', N'SqlStoredProcedure', N'Gets latest Model project run.', N'{"type":"object","required":["projectCode"]}', N'{"type":"array"}', N'model.project.run.read', N'model.usp_GetLatestProjectRun', 5000, 30000, 1),
    ('20000000-0000-0000-0000-000000000003', @FailedCapabilityId, N'Model.GetFailedRunChecks', N'SqlStoredProcedure', N'Gets failed Model project run checks.', N'{"type":"object","required":["projectCode"]}', N'{"type":"array"}', N'model.project.run.read', N'model.usp_GetFailedRunChecks', 5000, 30000, 1)
) AS source(ToolId, CapabilityId, ToolName, ToolType, Description, InputJsonSchema, OutputJsonSchema, RequiredPermissionCode, SqlProcedureName, MaxRows, TimeoutMs, IsActive)
ON target.ToolId = source.ToolId
WHEN NOT MATCHED THEN INSERT (ToolId, CapabilityId, ToolName, ToolType, Description, InputJsonSchema, OutputJsonSchema, RequiredPermissionCode, SqlProcedureName, MaxRows, TimeoutMs, IsActive)
VALUES (source.ToolId, source.CapabilityId, source.ToolName, source.ToolType, source.Description, source.InputJsonSchema, source.OutputJsonSchema, source.RequiredPermissionCode, source.SqlProcedureName, source.MaxRows, source.TimeoutMs, source.IsActive);
GO

CREATE OR ALTER PROCEDURE model.usp_GetProjectRunVerification
    @TenantId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER,
    @CorrelationId NVARCHAR(100) = NULL,
    @ProjectCode NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM security.UserEffectivePermission(@TenantId, @UserId) WHERE PermissionCode = N'model.project.run.read')
        THROW 51001, 'Permission denied for model.project.run.read.', 1;

    SELECT TOP (5000)
        p.ProjectCode,
        r.RunStatus,
        r.OverallScore,
        r.FailedCheckCount,
        r.WarningCheckCount,
        r.RunAtUtc
    FROM model.Project p
    INNER JOIN model.ProjectRun r ON r.TenantId = p.TenantId AND r.ProjectId = p.ProjectId
    WHERE p.TenantId = @TenantId
      AND p.ProjectCode = @ProjectCode
      AND p.IsActive = 1
    ORDER BY r.RunAtUtc DESC;
END;
GO

CREATE OR ALTER PROCEDURE model.usp_GetLatestProjectRun
    @TenantId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER,
    @CorrelationId NVARCHAR(100) = NULL,
    @ProjectCode NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM security.UserEffectivePermission(@TenantId, @UserId) WHERE PermissionCode = N'model.project.run.read')
        THROW 51001, 'Permission denied for model.project.run.read.', 1;

    SELECT TOP (1)
        p.ProjectCode,
        r.RunStatus,
        r.OverallScore,
        r.FailedCheckCount,
        r.WarningCheckCount,
        r.RunAtUtc
    FROM model.Project p
    INNER JOIN model.ProjectRun r ON r.TenantId = p.TenantId AND r.ProjectId = p.ProjectId
    WHERE p.TenantId = @TenantId
      AND p.ProjectCode = @ProjectCode
      AND p.IsActive = 1
    ORDER BY r.RunAtUtc DESC;
END;
GO

CREATE OR ALTER PROCEDURE model.usp_GetFailedRunChecks
    @TenantId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER,
    @CorrelationId NVARCHAR(100) = NULL,
    @ProjectCode NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM security.UserEffectivePermission(@TenantId, @UserId) WHERE PermissionCode = N'model.project.run.read')
        THROW 51001, 'Permission denied for model.project.run.read.', 1;

    SELECT TOP (5000)
        p.ProjectCode,
        c.CheckCode,
        c.CheckName,
        c.CheckStatus,
        c.Evidence
    FROM model.Project p
    INNER JOIN model.ProjectRun r ON r.TenantId = p.TenantId AND r.ProjectId = p.ProjectId
    INNER JOIN model.RunCheck c ON c.TenantId = r.TenantId AND c.RunRecordId = r.RunRecordId
    WHERE p.TenantId = @TenantId
      AND p.ProjectCode = @ProjectCode
      AND c.CheckStatus = N'Failed'
      AND c.IsSensitive = 0
    ORDER BY c.CheckCode;
END;
GO

