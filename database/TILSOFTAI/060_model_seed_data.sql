DECLARE @TenantId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';
DECLARE @OtherTenantId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000002';
DECLARE @AuthorizedUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000101';
DECLARE @UnauthorizedUserId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000102';
DECLARE @RoleId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000201';
DECLARE @ModuleId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000001001';
DECLARE @ProjectId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000002001';
DECLARE @RunRecordId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000003001';

MERGE core.Tenant AS target
USING (VALUES (@TenantId, N'MODEL-TENANT', N'Model Tenant', 1), (@OtherTenantId, N'OTHER-TENANT', N'Other Tenant', 1)) AS source(TenantId, TenantCode, TenantName, IsActive)
ON target.TenantId = source.TenantId
WHEN NOT MATCHED THEN INSERT (TenantId, TenantCode, TenantName, IsActive) VALUES (source.TenantId, source.TenantCode, source.TenantName, source.IsActive);

MERGE core.AppUser AS target
USING (VALUES (@TenantId, @AuthorizedUserId, N'model.authorized', N'Authorized Model User', 1), (@TenantId, @UnauthorizedUserId, N'model.unauthorized', N'Unauthorized Model User', 1)) AS source(TenantId, UserId, UserName, DisplayName, IsActive)
ON target.TenantId = source.TenantId AND target.UserId = source.UserId
WHEN NOT MATCHED THEN INSERT (TenantId, UserId, UserName, DisplayName, IsActive) VALUES (source.TenantId, source.UserId, source.UserName, source.DisplayName, source.IsActive);

MERGE security.Permission AS target
USING (VALUES (N'model.project.run.read', N'Read Model project runs', N'Allows reading Model project run evidence.')) AS source(PermissionCode, PermissionName, Description)
ON target.PermissionCode = source.PermissionCode
WHEN NOT MATCHED THEN INSERT (PermissionCode, PermissionName, Description) VALUES (source.PermissionCode, source.PermissionName, source.Description);

MERGE security.Role AS target
USING (VALUES (@TenantId, @RoleId, N'MODEL_READER', N'Model Reader', 1)) AS source(TenantId, RoleId, RoleCode, RoleName, IsActive)
ON target.TenantId = source.TenantId AND target.RoleId = source.RoleId
WHEN NOT MATCHED THEN INSERT (TenantId, RoleId, RoleCode, RoleName, IsActive) VALUES (source.TenantId, source.RoleId, source.RoleCode, source.RoleName, source.IsActive);

MERGE security.UserRole AS target
USING (VALUES (@TenantId, @AuthorizedUserId, @RoleId)) AS source(TenantId, UserId, RoleId)
ON target.TenantId = source.TenantId AND target.UserId = source.UserId AND target.RoleId = source.RoleId
WHEN NOT MATCHED THEN INSERT (TenantId, UserId, RoleId) VALUES (source.TenantId, source.UserId, source.RoleId);

MERGE security.RolePermission AS target
USING (VALUES (@TenantId, @RoleId, N'model.project.run.read')) AS source(TenantId, RoleId, PermissionCode)
ON target.TenantId = source.TenantId AND target.RoleId = source.RoleId AND target.PermissionCode = source.PermissionCode
WHEN NOT MATCHED THEN INSERT (TenantId, RoleId, PermissionCode) VALUES (source.TenantId, source.RoleId, source.PermissionCode);

MERGE ai.Module AS target
USING (VALUES (@ModuleId, N'Model', N'Model', N'Model domain MVP', 1, N'1.0.0')) AS source(ModuleId, ModuleCode, ModuleName, Description, IsActive, Version)
ON target.ModuleId = source.ModuleId
WHEN NOT MATCHED THEN INSERT (ModuleId, ModuleCode, ModuleName, Description, IsActive, Version) VALUES (source.ModuleId, source.ModuleCode, source.ModuleName, source.Description, source.IsActive, source.Version);

MERGE model.Project AS target
USING (VALUES (@TenantId, @ProjectId, N'MODEL-001', N'Model Project 001', 1)) AS source(TenantId, ProjectId, ProjectCode, ProjectName, IsActive)
ON target.TenantId = source.TenantId AND target.ProjectId = source.ProjectId
WHEN NOT MATCHED THEN INSERT (TenantId, ProjectId, ProjectCode, ProjectName, IsActive) VALUES (source.TenantId, source.ProjectId, source.ProjectCode, source.ProjectName, source.IsActive);

MERGE model.ProjectRun AS target
USING (VALUES (@TenantId, @RunRecordId, @ProjectId, N'Passed', 96.5, 0, 1, SYSUTCDATETIME())) AS source(TenantId, RunRecordId, ProjectId, RunStatus, OverallScore, FailedCheckCount, WarningCheckCount, RunAtUtc)
ON target.TenantId = source.TenantId AND target.RunRecordId = source.RunRecordId
WHEN NOT MATCHED THEN INSERT (TenantId, RunRecordId, ProjectId, RunStatus, OverallScore, FailedCheckCount, WarningCheckCount, RunAtUtc) VALUES (source.TenantId, source.RunRecordId, source.ProjectId, source.RunStatus, source.OverallScore, source.FailedCheckCount, source.WarningCheckCount, source.RunAtUtc);

