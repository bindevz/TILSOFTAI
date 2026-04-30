DECLARE @TenantA UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';
DECLARE @TenantB UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000002';
DECLARE @AuthorizedUserA UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000101';
DECLARE @UnauthorizedUserA UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000102';
DECLARE @AuthorizedUserB UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000201';
DECLARE @RoleA UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000301';
DECLARE @RoleB UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000302';
DECLARE @ModuleId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000001001';
DECLARE @ProjectA1 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000002001';
DECLARE @ProjectA2 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000002002';
DECLARE @ProjectB1 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000002101';
DECLARE @RunA1 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000003001';
DECLARE @RunA2 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000003002';
DECLARE @RunB1 UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000003101';

MERGE core.Tenant AS target
USING (VALUES (@TenantA, N'MODEL-TENANT-A', N'Model Tenant A', 1), (@TenantB, N'MODEL-TENANT-B', N'Model Tenant B', 1)) AS source(TenantId, TenantCode, TenantName, IsActive)
ON target.TenantId = source.TenantId
WHEN NOT MATCHED THEN INSERT (TenantId, TenantCode, TenantName, IsActive) VALUES (source.TenantId, source.TenantCode, source.TenantName, source.IsActive);

MERGE core.AppUser AS target
USING (VALUES
    (@TenantA, @AuthorizedUserA, N'model.a.authorized', N'Authorized Model User A', 1),
    (@TenantA, @UnauthorizedUserA, N'model.a.unauthorized', N'Unauthorized Model User A', 1),
    (@TenantB, @AuthorizedUserB, N'model.b.authorized', N'Authorized Model User B', 1)
) AS source(TenantId, UserId, UserName, DisplayName, IsActive)
ON target.TenantId = source.TenantId AND target.UserId = source.UserId
WHEN NOT MATCHED THEN INSERT (TenantId, UserId, UserName, DisplayName, IsActive) VALUES (source.TenantId, source.UserId, source.UserName, source.DisplayName, source.IsActive);

MERGE security.Permission AS target
USING (VALUES (N'model.project.run.read', N'Read Model project runs', N'Allows reading Model project run evidence.')) AS source(PermissionCode, PermissionName, Description)
ON target.PermissionCode = source.PermissionCode
WHEN NOT MATCHED THEN INSERT (PermissionCode, PermissionName, Description) VALUES (source.PermissionCode, source.PermissionName, source.Description);

MERGE security.Role AS target
USING (VALUES (@TenantA, @RoleA, N'MODEL_READER', N'Model Reader', 1), (@TenantB, @RoleB, N'MODEL_READER', N'Model Reader', 1)) AS source(TenantId, RoleId, RoleCode, RoleName, IsActive)
ON target.TenantId = source.TenantId AND target.RoleId = source.RoleId
WHEN NOT MATCHED THEN INSERT (TenantId, RoleId, RoleCode, RoleName, IsActive) VALUES (source.TenantId, source.RoleId, source.RoleCode, source.RoleName, source.IsActive);

MERGE security.UserRole AS target
USING (VALUES (@TenantA, @AuthorizedUserA, @RoleA), (@TenantB, @AuthorizedUserB, @RoleB)) AS source(TenantId, UserId, RoleId)
ON target.TenantId = source.TenantId AND target.UserId = source.UserId AND target.RoleId = source.RoleId
WHEN NOT MATCHED THEN INSERT (TenantId, UserId, RoleId) VALUES (source.TenantId, source.UserId, source.RoleId);

MERGE security.RolePermission AS target
USING (VALUES (@TenantA, @RoleA, N'model.project.run.read'), (@TenantB, @RoleB, N'model.project.run.read')) AS source(TenantId, RoleId, PermissionCode)
ON target.TenantId = source.TenantId AND target.RoleId = source.RoleId AND target.PermissionCode = source.PermissionCode
WHEN NOT MATCHED THEN INSERT (TenantId, RoleId, PermissionCode) VALUES (source.TenantId, source.RoleId, source.PermissionCode);

MERGE ai.Module AS target
USING (VALUES (@ModuleId, N'Model', N'Model', N'Model domain MVP', 1, N'1.0.0')) AS source(ModuleId, ModuleCode, ModuleName, Description, IsActive, Version)
ON target.ModuleId = source.ModuleId
WHEN NOT MATCHED THEN INSERT (ModuleId, ModuleCode, ModuleName, Description, IsActive, Version) VALUES (source.ModuleId, source.ModuleCode, source.ModuleName, source.Description, source.IsActive, source.Version);

MERGE model.Project AS target
USING (VALUES
    (@TenantA, @ProjectA1, N'MODEL-001', N'Tenant A Model Project 001', 1),
    (@TenantA, @ProjectA2, N'MODEL-002', N'Tenant A Model Project 002', 1),
    (@TenantB, @ProjectB1, N'MODEL-001', N'Tenant B Model Project 001', 1)
) AS source(TenantId, ProjectId, ProjectCode, ProjectName, IsActive)
ON target.TenantId = source.TenantId AND target.ProjectId = source.ProjectId
WHEN NOT MATCHED THEN INSERT (TenantId, ProjectId, ProjectCode, ProjectName, IsActive) VALUES (source.TenantId, source.ProjectId, source.ProjectCode, source.ProjectName, source.IsActive);

MERGE model.ProjectRun AS target
USING (VALUES
    (@TenantA, @RunA1, @ProjectA1, N'Passed', 96.5, 0, 1, DATEADD(day, -1, SYSUTCDATETIME())),
    (@TenantA, @RunA2, @ProjectA2, N'Warning', 88.0, 1, 2, SYSUTCDATETIME()),
    (@TenantB, @RunB1, @ProjectB1, N'Failed', 67.0, 3, 0, SYSUTCDATETIME())
) AS source(TenantId, RunRecordId, ProjectId, RunStatus, OverallScore, FailedCheckCount, WarningCheckCount, RunAtUtc)
ON target.TenantId = source.TenantId AND target.RunRecordId = source.RunRecordId
WHEN NOT MATCHED THEN INSERT (TenantId, RunRecordId, ProjectId, RunStatus, OverallScore, FailedCheckCount, WarningCheckCount, RunAtUtc) VALUES (source.TenantId, source.RunRecordId, source.ProjectId, source.RunStatus, source.OverallScore, source.FailedCheckCount, source.WarningCheckCount, source.RunAtUtc);

MERGE model.RunCheck AS target
USING (VALUES
    (@TenantA, '00000000-0000-0000-0000-000000004001', @RunA1, N'RUN-TARGET', N'Run target achieved', N'Passed', N'Overall score exceeded target.', 0),
    (@TenantA, '00000000-0000-0000-0000-000000004002', @RunA1, N'WARN-REVIEW', N'Warning review', N'Warning', N'One non-blocking warning remains.', 0),
    (@TenantA, '00000000-0000-0000-0000-000000004003', @RunA1, N'SENSITIVE-NOTE', N'Sensitive reviewer note', N'Warning', N'Internal reviewer email and private note.', 1),
    (@TenantA, '00000000-0000-0000-0000-000000004004', @RunA2, N'QUALITY-GATE', N'Quality gate', N'Failed', N'Quality threshold was missed.', 0),
    (@TenantB, '00000000-0000-0000-0000-000000004101', @RunB1, N'RUN-TARGET', N'Run target achieved', N'Failed', N'Tenant B result must not leak.', 0)
) AS source(TenantId, RunCheckId, RunRecordId, CheckCode, CheckName, CheckStatus, Evidence, IsSensitive)
ON target.TenantId = source.TenantId AND target.RunCheckId = source.RunCheckId
WHEN NOT MATCHED THEN INSERT (TenantId, RunCheckId, RunRecordId, CheckCode, CheckName, CheckStatus, Evidence, IsSensitive) VALUES (source.TenantId, source.RunCheckId, source.RunRecordId, source.CheckCode, source.CheckName, source.CheckStatus, source.Evidence, source.IsSensitive);

