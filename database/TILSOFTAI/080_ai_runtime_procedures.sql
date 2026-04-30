CREATE OR ALTER PROCEDURE ai.usp_CreateRun
    @TenantId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER,
    @RunId UNIQUEIDENTIFIER,
    @CorrelationId NVARCHAR(100) = NULL,
    @Question NVARCHAR(MAX),
    @DetectedLanguage NVARCHAR(20) = NULL,
    @DomainHint NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM core.Tenant WHERE TenantId = @TenantId AND IsActive = 1)
        THROW 51001, 'Tenant is inactive or unavailable.', 1;
    IF NOT EXISTS (SELECT 1 FROM core.AppUser WHERE TenantId = @TenantId AND UserId = @UserId AND IsActive = 1)
        THROW 51001, 'User is inactive or unavailable.', 1;

    IF NOT EXISTS (SELECT 1 FROM ai.Run WHERE RunId = @RunId)
    BEGIN
        INSERT ai.Run (RunId, TenantId, UserId, CorrelationId, Question, DetectedLanguage, DomainHint, Status)
        VALUES (@RunId, @TenantId, @UserId, @CorrelationId, @Question, @DetectedLanguage, @DomainHint, N'Running');
    END
END;
GO

CREATE OR ALTER PROCEDURE ai.usp_UpdateRunStatus
    @TenantId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER,
    @RunId UNIQUEIDENTIFIER,
    @Status NVARCHAR(50),
    @SelectedCapabilityCode NVARCHAR(150) = NULL,
    @DiagnosticCode NVARCHAR(100) = NULL,
    @DiagnosticMessage NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE ai.Run
    SET Status = @Status,
        SelectedCapabilityCode = COALESCE(@SelectedCapabilityCode, SelectedCapabilityCode),
        CompletedAtUtc = CASE WHEN @Status IN (N'Completed', N'Failed', N'Forbidden', N'NeedsClarification', N'NoCapabilityFound') THEN SYSUTCDATETIME() ELSE CompletedAtUtc END
    WHERE TenantId = @TenantId AND UserId = @UserId AND RunId = @RunId;
END;
GO

CREATE OR ALTER PROCEDURE ai.usp_RecordToolCallStart
    @TenantId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER,
    @ToolCallId UNIQUEIDENTIFIER,
    @RunId UNIQUEIDENTIFIER,
    @ToolName NVARCHAR(200),
    @ParametersJson NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM ai.Tool WHERE ToolName = @ToolName AND IsActive = 1)
        THROW 51001, 'Tool is not registered.', 1;

    INSERT ai.ToolCall (ToolCallId, RunId, TenantId, UserId, ToolName, ParametersJson, Status, RowCount, StartedAtUtc)
    VALUES (@ToolCallId, @RunId, @TenantId, @UserId, @ToolName, @ParametersJson, N'Running', 0, SYSUTCDATETIME());
END;
GO

CREATE OR ALTER PROCEDURE ai.usp_RecordToolCallCompletion
    @TenantId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER,
    @ToolCallId UNIQUEIDENTIFIER,
    @RunId UNIQUEIDENTIFIER,
    @ToolName NVARCHAR(200),
    @ParametersJson NVARCHAR(MAX),
    @Status NVARCHAR(50),
    @RowCount INT,
    @ElapsedMilliseconds BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM ai.Tool WHERE ToolName = @ToolName AND IsActive = 1)
        THROW 51001, 'Tool is not registered.', 1;

    IF EXISTS (SELECT 1 FROM ai.ToolCall WHERE ToolCallId = @ToolCallId)
    BEGIN
        UPDATE ai.ToolCall
        SET Status = @Status, RowCount = @RowCount, CompletedAtUtc = SYSUTCDATETIME()
        WHERE ToolCallId = @ToolCallId AND TenantId = @TenantId AND UserId = @UserId;
    END
    ELSE
    BEGIN
        INSERT ai.ToolCall (ToolCallId, RunId, TenantId, UserId, ToolName, ParametersJson, Status, RowCount, StartedAtUtc, CompletedAtUtc)
        VALUES (@ToolCallId, @RunId, @TenantId, @UserId, @ToolName, @ParametersJson, @Status, @RowCount, DATEADD(millisecond, -@ElapsedMilliseconds, SYSUTCDATETIME()), SYSUTCDATETIME());
    END
END;
GO

CREATE OR ALTER PROCEDURE artifact.usp_CreateArtifact
    @TenantId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER,
    @ArtifactId UNIQUEIDENTIFIER,
    @RunId UNIQUEIDENTIFIER,
    @ArtifactType NVARCHAR(50),
    @ContentType NVARCHAR(100),
    @StoragePath NVARCHAR(1000),
    @Sha256 NVARCHAR(64),
    @SizeBytes BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM ai.Run WHERE TenantId = @TenantId AND UserId = @UserId AND RunId = @RunId)
        THROW 51001, 'Run is not visible to this tenant/user.', 1;

    IF NOT EXISTS (SELECT 1 FROM artifact.Artifact WHERE ArtifactId = @ArtifactId)
    BEGIN
        INSERT artifact.Artifact (ArtifactId, TenantId, RunId, ArtifactType, ContentType, StoragePath, Sha256, SizeBytes)
        VALUES (@ArtifactId, @TenantId, @RunId, @ArtifactType, @ContentType, @StoragePath, @Sha256, @SizeBytes);
    END
END;
GO

CREATE OR ALTER PROCEDURE artifact.usp_GetArtifactMetadata
    @TenantId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER,
    @ArtifactId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT a.ArtifactId, a.RunId, a.ArtifactType, a.ContentType, a.Sha256, a.SizeBytes, a.CreatedAtUtc
    FROM artifact.Artifact a
    INNER JOIN ai.Run r ON r.TenantId = a.TenantId AND r.RunId = a.RunId
    WHERE a.TenantId = @TenantId
      AND a.ArtifactId = @ArtifactId
      AND r.UserId = @UserId;
END;
GO

CREATE OR ALTER PROCEDURE ai.usp_CreateProvenance
    @TenantId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER,
    @RunId UNIQUEIDENTIFIER,
    @ToolName NVARCHAR(200),
    @FiltersJson NVARCHAR(MAX),
    @ArtifactId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM ai.Run WHERE TenantId = @TenantId AND UserId = @UserId AND RunId = @RunId)
        THROW 51001, 'Run is not visible to this tenant/user.', 1;

    INSERT ai.Provenance (ProvenanceId, RunId, TenantId, ToolName, FiltersJson, ArtifactId)
    VALUES (NEWID(), @RunId, @TenantId, @ToolName, @FiltersJson, @ArtifactId);
END;
GO

CREATE OR ALTER PROCEDURE ai.usp_GetRunDetails
    @TenantId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER,
    @RunId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;

    SELECT N'Run' AS RecordType, r.Question, r.Status, r.SelectedCapabilityCode, r.CreatedAtUtc,
           CAST(NULL AS NVARCHAR(200)) AS ToolName, CAST(NULL AS NVARCHAR(50)) AS ToolStatus, CAST(NULL AS INT) AS RowCount, CAST(NULL AS BIGINT) AS ElapsedMilliseconds,
           CAST(NULL AS UNIQUEIDENTIFIER) AS ArtifactId, CAST(NULL AS NVARCHAR(50)) AS ArtifactType, CAST(NULL AS NVARCHAR(100)) AS ContentType, CAST(NULL AS NVARCHAR(64)) AS Sha256, CAST(NULL AS BIGINT) AS SizeBytes
    FROM ai.Run r
    WHERE r.TenantId = @TenantId AND r.UserId = @UserId AND r.RunId = @RunId
    UNION ALL
    SELECT N'ToolCall', NULL, NULL, NULL, NULL, tc.ToolName, tc.Status, tc.RowCount, DATEDIFF_BIG(millisecond, tc.StartedAtUtc, COALESCE(tc.CompletedAtUtc, SYSUTCDATETIME())),
           NULL, NULL, NULL, NULL, NULL
    FROM ai.ToolCall tc
    WHERE tc.TenantId = @TenantId AND tc.UserId = @UserId AND tc.RunId = @RunId
    UNION ALL
    SELECT N'Artifact', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL,
           a.ArtifactId, a.ArtifactType, a.ContentType, a.Sha256, a.SizeBytes
    FROM artifact.Artifact a
    INNER JOIN ai.Run r ON r.TenantId = a.TenantId AND r.RunId = a.RunId
    WHERE a.TenantId = @TenantId AND r.UserId = @UserId AND a.RunId = @RunId;
END;
GO

CREATE OR ALTER PROCEDURE ai.usp_SearchModelCapabilities
    @TenantId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER,
    @Question NVARCHAR(MAX),
    @DomainHint NVARCHAR(100) = NULL,
    @TopK INT = 5
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@TopK)
        c.CapabilityId,
        m.ModuleCode,
        c.CapabilityCode,
        c.CapabilityName,
        c.Description,
        t.ToolId,
        t.ToolName,
        t.ToolType,
        t.SqlProcedureName,
        t.InputJsonSchema,
        t.OutputJsonSchema,
        t.RequiredPermissionCode,
        t.MaxRows,
        t.TimeoutMs
    FROM ai.Module m
    INNER JOIN ai.Capability c ON c.ModuleId = m.ModuleId
    INNER JOIN ai.Tool t ON t.CapabilityId = c.CapabilityId
    WHERE m.ModuleCode = N'Model'
      AND m.IsActive = 1
      AND c.IsActive = 1
      AND t.IsActive = 1
      AND EXISTS (
          SELECT 1
          FROM security.UserEffectivePermission(@TenantId, @UserId) p
          WHERE p.PermissionCode = t.RequiredPermissionCode
      )
      AND (@DomainHint IS NULL OR @DomainHint = N'' OR @DomainHint = N'Model')
      AND (@Question LIKE N'%MODEL-%' OR @Question LIKE N'%run%' OR @Question LIKE N'%status%' OR @Question LIKE N'%failed%' OR @Question LIKE N'%đạt%' OR @Question LIKE N'%kiểm tra%')
    ORDER BY
      CASE
        WHEN @Question LIKE N'%failed%' OR @Question LIKE N'%fail%' THEN CASE WHEN c.CapabilityCode LIKE N'%failed_checks%' THEN 0 ELSE 1 END
        WHEN @Question LIKE N'%latest%' OR @Question LIKE N'%status%' THEN CASE WHEN c.CapabilityCode LIKE N'%latest%' THEN 0 ELSE 1 END
        ELSE CASE WHEN c.CapabilityCode LIKE N'%verify%' THEN 0 ELSE 1 END
      END,
      c.CapabilityCode;
END;
GO

