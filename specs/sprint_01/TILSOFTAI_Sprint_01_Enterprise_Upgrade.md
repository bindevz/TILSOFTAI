# TILSOFTAI Sprint 01 — Enterprise Upgrade Execution Plan

**Project:** TILSOFTAI — ERP AI Data Orchestration Framework  
**Sprint:** 01  
**Role:** CTO / AI Architecture  
**Audience:** Codex Agent, backend engineers, AI engineers, QA engineers, DevOps engineers  
**Primary technology stack:** C#, ASP.NET Core, SQL Server 2025, Microsoft Agent Framework, OpenAI-compatible local AI endpoint  
**MVP domain:** Model only  
**Date:** 2026-04-30

---

## 1. Sprint 01 Executive Objective

Sprint 01 upgrades TILSOFTAI from a skeleton/simulation MVP into the first production-shaped slice of an **Enterprise-grade ERP AI Data Orchestration Framework**.

The sprint outcome is not “more demo behavior”. The outcome is a real end-to-end Model-domain AI run where:

1. The API receives a natural-language question.
2. `tenantId`, `userId`, and `correlationId` are propagated through the full request.
3. Model capability/tool candidates are resolved from SQL metadata, not hardcoded in memory.
4. The agent/workflow selects only registered Model tools.
5. Tool parameters are bound and validated.
6. The selected Model tool executes a SQL Server stored procedure.
7. SQL Server enforces tenant filtering and permission checks.
8. Raw and sanitized tool results are persisted as artifacts.
9. A compressed context package is created from sanitized data, schema, metadata, and provenance.
10. The configured local OpenAI-compatible AI endpoint is called.
11. The answer includes summary, table, insights, caveats, provenance, and follow-up.
12. The run is auditable and replayable.

### Sprint North Star

A real request like this must work in a local enterprise development environment:

```http
POST /api/v1/ai/runs HTTP/1.1
X-Tenant-Id: 00000000-0000-0000-0000-000000000001
X-User-Id: 00000000-0000-0000-0000-000000000101
X-Correlation-Id: sprint01-e2e-001
Content-Type: application/json
```

```json
{
  "question": "Verify whether MODEL-001 achieved its run target.",
  "domainHint": "Model"
}
```

The response must be grounded in SQL Server data, not in mock rows or deterministic hardcoded answers.

---

## 2. Current State Summary

Codex must begin Sprint 01 by reading the current repository and confirming the current state. Based on the CTO review, the current state is:

- The repository is already named **TILSOFTAI** and declares itself as an enterprise-grade ERP AI Data Orchestration Framework MVP for the **Model** domain only.
- Runtime configuration keys exist for SQL Server connection string, local AI base URL, local AI API key, chat model, embedding model, and artifact root path.
- `AGENTS.md` already contains mandatory implementation rules: C# backend, SQL Server 2025, Model-only MVP, no hardcoded runtime values, `X-Tenant-Id`, `X-User-Id`, SQL-level tenant/user enforcement, registered tools only, artifact-first output, sanitized/compressed context, provenance, async APIs, tests, OpenAPI.
- SQL scripts already define foundational schemas: `core`, `security`, `ai`, `artifact`, and `model`.
- SQL scripts already define Model stored procedures such as:
  - `model.usp_GetProjectRunVerification`
  - `model.usp_GetLatestProjectRun`
  - `model.usp_GetFailedRunChecks`
- SQL stored procedures already accept `@TenantId`, `@UserId`, and `@CorrelationId`, and check `security.UserEffectivePermission`.
- `src/TILSOFTAI.Persistence` and `src/TILSOFTAI.Infrastructure` currently appear underdeveloped and must become real production projects.
- Current runtime implementation still relies on simulation components:
  - `InMemoryCapabilitySearchService`
  - `ModelToolRuntime`
  - `DeterministicLocalAiClient`
  - possible in-memory run/artifact repositories
- Current `ControlledAgentWorkflow` performs simple keyword/tool selection and defaults to `MODEL-001` when a project code is missing. That behavior is unacceptable for enterprise-grade runtime because the system must not silently invent business parameters.

---

## 3. Sprint 01 Non-Negotiable Rules

Codex must follow these rules throughout the sprint.

### 3.1 No Hardcoding

Never hardcode runtime values in source code.

Forbidden examples:

```csharp
var connectionString = "Server=localhost;Database=TILSOFTAI;User Id=sa;Password=123";
var baseUrl = "http://192.168.8.247:6688/v1";
var tenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");
var userId = Guid.Parse("00000000-0000-0000-0000-000000000101");
var projectCode = "MODEL-001";
```

Allowed local development configuration values may be documented in README, `launchSettings.json` placeholders, test fixtures, or setup scripts, but they must not be embedded in production logic.

Local developer example values:

```powershell
$env:ConnectionStrings__TilsoftAi="Server=localhost;Database=TILSOFTAI;User Id=sa;Password=123;Encrypt=False;TrustServerCertificate=True"
$env:Ai__OpenAICompatible__BaseUrl="http://192.168.8.247:6688/v1"
$env:Ai__OpenAICompatible__ApiKey="local-placeholder-or-secret"
$env:Ai__OpenAICompatible__ChatModel="<configured-local-chat-model>"
$env:Ai__OpenAICompatible__EmbeddingModel="<configured-local-embedding-model>"
$env:Artifacts__RootPath="C:\\TILSOFTAI\\artifacts"
```

### 3.2 Model Domain Only

Do not implement Sale, Purchasing, Inventory, Finance, HR, CRM, Manufacturing, Warehouse, or any other ERP domain in Sprint 01.

Allowed:

```text
Model
```

Forbidden:

```text
Sale
Purchasing
Inventory
Finance
HR
Cross-domain analysis
```

### 3.3 SQL Server Owns Tenant and Permission Enforcement

The API must pass `tenantId` and `userId`, but SQL Server must verify whether the user is allowed to access the requested data.

All Model tool stored procedure executions must pass:

```sql
@TenantId
@UserId
@CorrelationId
```

SQL must:

- Filter by `TenantId`.
- Check user active status.
- Check tenant active status.
- Check permission via `security.UserEffectivePermission`.
- Throw permission-denied errors when access is not allowed.

### 3.4 LLM Must Never Execute SQL Directly

The local AI model must never generate SQL and execute it.

Allowed:

```text
User question -> capability retrieval -> registered tool -> SQL stored procedure -> artifact -> sanitized context -> AI analysis
```

Forbidden:

```text
User question -> LLM generates SELECT statement -> app executes generated SQL
```

### 3.5 Registered Tools Only

The agent/workflow may only choose tools that came from SQL metadata and passed permission filtering.

### 3.6 Artifact-First Data Handling

Tool output must be persisted before being sent to the AI model.

Minimum artifacts per successful tool call:

1. Raw result artifact.
2. Sanitized result artifact.
3. Schema/metadata summary or embedded schema payload.
4. Provenance record referencing the sanitized artifact.

### 3.7 Fail Closed

If the system cannot validate parameters, enforce access, persist artifacts, build provenance, call local AI, or parse the AI response safely, the run must fail with a clear status and diagnostic metadata. It must not fabricate numbers or return an ungrounded answer.

---

## 4. Sprint 01 Scope

### In Scope

Sprint 01 must deliver:

1. Production DI mode that uses SQL-backed and local-AI-backed services.
2. Testing DI mode that may continue using deterministic stubs.
3. SQL connection factory and SQL execution utilities.
4. SQL-backed capability retrieval from `ai.Module`, `ai.Capability`, and `ai.Tool`.
5. SQL-backed Model tool runtime executing registered stored procedures.
6. SQL-backed run repository.
7. SQL-backed artifact metadata repository.
8. File-system artifact content store.
9. OpenAI-compatible local AI client.
10. Controlled Model workflow using Microsoft Agent Framework where feasible, behind a stable application interface.
11. Parameter binding that does not silently invent `projectCode`.
12. End-to-end integration test path for the Model domain.
13. Updated README and AGENTS.md with Sprint 01 production rules.
14. CI validation that builds and runs tests.

### Out of Scope

Sprint 01 must not include:

- New ERP domains.
- Dynamic SQL generated by AI.
- Write-back ERP actions.
- Vector ANN optimization as a hard dependency.
- Full enterprise SSO rollout.
- UI frontend.
- Excel export.
- Complex multi-agent cross-domain orchestration.

---

## 5. Sprint 01 Definition of Done

Sprint 01 is complete only when all items below are true.

### Functional Done

- `POST /api/v1/ai/runs` accepts a Model question and returns a valid response.
- The selected capability/tool is resolved from SQL metadata.
- The selected tool is a registered Model tool.
- Tool parameters are bound and validated.
- The tool executes a SQL stored procedure.
- SQL denies unauthorized users.
- Raw and sanitized artifacts are persisted.
- Provenance references the actual tool, filters, and artifact IDs.
- The configured local AI endpoint is called for final answer generation.
- Final answer contains:
  - Summary
  - Table when relevant
  - Insights
  - Caveats
  - Provenance
  - Follow-up suggestions

### Engineering Done

- Production service registration does not use in-memory or deterministic implementations.
- Testing service registration may use deterministic implementations.
- `IRequestContextAccessor` is scoped or `AsyncLocal`-safe.
- No production service hardcodes tenant, user, project, connection string, base URL, model name, artifact path, or API key.
- SQL command execution uses parameterized commands and `CommandType.StoredProcedure`.
- Tool stored procedure name comes from trusted SQL metadata, not user input.
- Logs include correlation ID but do not log sensitive payloads.
- All async APIs accept `CancellationToken`.
- Automated tests pass.

### Evidence Done

Codex must produce or update:

- Source code.
- SQL migration scripts if needed.
- Unit tests.
- Integration tests.
- README instructions.
- AGENTS.md production rules.
- A short Sprint 01 completion note under `docs/sprints/sprint-01-completion.md`.

---

## 6. Target Runtime Architecture After Sprint 01

```text
HTTP API
  |
  v
RequestContextMiddleware
  - X-Tenant-Id
  - X-User-Id
  - X-Correlation-Id
  |
  v
AiRunOrchestrator
  |
  v
ModelRunAnalysisWorkflow / Agent Brain
  |
  +--> SqlCapabilitySearchService
  |      - Reads ai.Module, ai.Capability, ai.Tool
  |      - Filters Model only
  |      - Filters by user permission
  |
  +--> ModelParameterBinder
  |      - Extracts projectCode
  |      - Does not invent default business parameters
  |      - Validates against tool input schema
  |
  +--> SqlModelToolRuntime
  |      - Executes registered model stored procedure
  |      - Passes TenantId/UserId/CorrelationId
  |      - Enforces command timeout and max rows
  |
  +--> Artifact Pipeline
  |      - Save raw artifact content
  |      - Save sanitized artifact content
  |      - Save artifact metadata in SQL
  |      - Save provenance in SQL
  |
  +--> Context Packager
  |      - Sanitized rows only
  |      - Schema + metadata + artifact refs
  |      - Compressed context
  |
  +--> OpenAICompatibleLocalAiClient
         - Calls configured local AI endpoint
         - Parses structured answer
         - Fails closed on invalid answer
```

---

## 7. Target Project Structure

Codex must organize Sprint 01 code using clean boundaries.

```text
src/
  TILSOFTAI.Api/
    Middleware/
      RequestContextMiddleware.cs
    DependencyInjection/
      ApiServiceCollectionExtensions.cs
    Program.cs

  TILSOFTAI.Application/
    Abstractions/
      ICapabilitySearchService.cs
      IToolRuntime.cs
      IArtifactContentStore.cs
      IArtifactRepository.cs
      IAiRunRepository.cs
      ILocalAiClient.cs
      IRequestContextAccessor.cs
      IModelParameterBinder.cs
      IAgentBrain.cs
    ContextPackaging/
      SanitizerAndContextPackager.cs
    Runs/
      AiRunOrchestrator.cs
    Security/
      PermissionDeniedException.cs
      TenantUserAccessException.cs

  TILSOFTAI.Agent/
    Model/
      ModelRunAnalysisWorkflow.cs
      ModelToolSelectionAgent.cs
      ModelParameterBindingAgent.cs
      ModelAnswerGenerationAgent.cs
    AgentFramework/
      AgentFrameworkBrain.cs
      AgentFrameworkServiceCollectionExtensions.cs
    AgentPrompts/
      model-system.md
      model-answer-generation.md

  TILSOFTAI.Contracts/
    Api/
    Common/
    Configuration/
    Tools/
    Artifacts/

  TILSOFTAI.Infrastructure/
    LocalAi/
      OpenAICompatibleLocalAiClient.cs
      OpenAICompatibleLocalAiOptions.cs
      OpenAIChatCompletionRequest.cs
      OpenAIChatCompletionResponse.cs
      OpenAIEmbeddingRequest.cs
      OpenAIEmbeddingResponse.cs
    DependencyInjection/
      InfrastructureServiceCollectionExtensions.cs

  TILSOFTAI.Persistence/
    Connection/
      ISqlConnectionFactory.cs
      SqlConnectionFactory.cs
    Capabilities/
      SqlCapabilitySearchService.cs
    Tools/
      SqlModelToolRuntime.cs
      SqlToolResultMapper.cs
    Runs/
      SqlAiRunRepository.cs
    Artifacts/
      SqlArtifactRepository.cs
      FileSystemArtifactContentStore.cs
    DependencyInjection/
      PersistenceServiceCollectionExtensions.cs

  TILSOFTAI.Modules.Model/
    ModelModuleRegistration.cs
```

If existing folder names differ, Codex may preserve existing names, but the boundaries must remain clear:

- API layer owns HTTP only.
- Application layer owns orchestration contracts and use cases.
- Agent layer owns agent/workflow logic.
- Persistence layer owns SQL Server access.
- Infrastructure layer owns external service integration such as local AI.
- Model module owns Model-specific registration and metadata conventions.

---

## 8. Workstream A — Build Stabilization and Dependency Injection Cleanup

### Goal

Make production and testing runtime modes explicit.

### Tasks

#### A1. Verify solution build

Run:

```powershell
dotnet restore TILSOFTAI.sln /p:RestoreConfigFile="$PWD\NuGet.Config"
dotnet build TILSOFTAI.sln --no-restore /p:RestoreConfigFile="$PWD\NuGet.Config"
```

If the build fails due to missing classes referenced by `Program.cs`, create or move the missing implementations into the correct projects.

#### A2. Fix `IRequestContextAccessor` lifetime

The accessor must not be a singleton with mutable shared state.

Acceptable options:

Option 1 — scoped accessor:

```csharp
builder.Services.AddScoped<IRequestContextAccessor, RequestContextAccessor>();
```

Option 2 — singleton accessor with `AsyncLocal<RequestContext?>`:

```csharp
public sealed class RequestContextAccessor : IRequestContextAccessor
{
    private static readonly AsyncLocal<RequestContext?> CurrentContext = new();

    public RequestContext? Current
    {
        get => CurrentContext.Value;
        set => CurrentContext.Value = value;
    }
}
```

Preferred for Sprint 01: scoped accessor.

#### A3. Create environment-aware service registration

Production must not use in-memory services.

Pseudo-registration:

```csharp
if (builder.Environment.IsEnvironment("Testing"))
{
    services.AddTilsoftAiTestingServices();
}
else
{
    services.AddTilsoftAiPersistence(builder.Configuration);
    services.AddTilsoftAiInfrastructure(builder.Configuration);
    services.AddTilsoftAiAgentFramework(builder.Configuration);
}
```

Production must use:

```text
SqlCapabilitySearchService
SqlModelToolRuntime
SqlAiRunRepository
SqlArtifactRepository
FileSystemArtifactContentStore
OpenAICompatibleLocalAiClient
```

Testing may use:

```text
InMemoryCapabilitySearchService
DeterministicLocalAiClient
InMemoryRunRepository
```

#### A4. Keep test doubles clearly marked

Move test-only doubles to one of these locations:

```text
tests/TILSOFTAI.TestingSupport/
src/TILSOFTAI.Application/Testing/    // only if excluded from production DI
```

Every test-only class name should make its purpose clear:

```text
TestingCapabilitySearchService
TestingToolRuntime
TestingLocalAiClient
TestingRunRepository
```

Avoid names like `ModelToolRuntime` for fake implementations.

### Acceptance Criteria

- Production DI does not register fake services.
- Testing DI remains deterministic and fast.
- No mutable singleton request context.
- Build succeeds.

---

## 9. Workstream B — SQL Server Runtime Foundation

### Goal

Create enterprise-grade SQL Server access infrastructure.

### Tasks

#### B1. Add SQL client package

Add a current supported `Microsoft.Data.SqlClient` package to `TILSOFTAI.Persistence`.

```powershell
dotnet add src/TILSOFTAI.Persistence/TILSOFTAI.Persistence.csproj package Microsoft.Data.SqlClient
```

If Sprint 01 writes or reads native SQL Server vector values from .NET, use a version that supports native vector handling. If not, Sprint 01 may treat vectors as JSON/strings and defer native vector optimization to Sprint 02.

#### B2. Implement `ISqlConnectionFactory`

```csharp
public interface ISqlConnectionFactory
{
    Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken);
}
```

Implementation requirements:

- Read connection string from `ConnectionStrings:TilsoftAi`.
- Validate it at startup.
- Never log the full connection string.
- Open the connection asynchronously.

#### B3. Add common SQL command helper

Create a helper to reduce repeated SQL execution code.

Minimum features:

- Set `CommandType.StoredProcedure` for stored procedure calls.
- Add typed parameters.
- Support command timeout.
- Pass `CancellationToken`.
- Map `SqlException` error number `51001` to a domain permission exception.
- Avoid logging sensitive parameter values.

#### B4. Add repository stored procedures if needed

The existing database scripts already define tables. Sprint 01 may add idempotent SQL scripts for runtime persistence procedures.

Add:

```text
database/TILSOFTAI/080_ai_runtime_procedures.sql
```

Suggested procedures:

```sql
ai.usp_CreateRun
ai.usp_UpdateRunStatus
ai.usp_RecordToolCallStart
ai.usp_RecordToolCallCompletion
artifact.usp_CreateArtifact
ai.usp_CreateProvenance
ai.usp_GetRunDetails
artifact.usp_GetArtifactMetadata
ai.usp_SearchModelCapabilities
```

Keep all scripts idempotent using `CREATE OR ALTER PROCEDURE` and `IF OBJECT_ID(...) IS NULL` patterns.

### Suggested SQL Procedure Contracts

#### `ai.usp_SearchModelCapabilities`

```sql
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
      AND (
          @DomainHint IS NULL
          OR @DomainHint = N''
          OR @DomainHint = N'Model'
      )
      AND (
          c.CapabilityCode LIKE N'%' + @Question + N'%'
          OR c.CapabilityName LIKE N'%' + @Question + N'%'
          OR c.Description LIKE N'%' + @Question + N'%'
          OR @Question LIKE N'%run%'
          OR @Question LIKE N'%MODEL-%'
          OR @Question LIKE N'%kiểm tra%'
          OR @Question LIKE N'%chứng thực%'
      )
    ORDER BY
      CASE
        WHEN @Question LIKE N'%failed%' OR @Question LIKE N'%fail%' THEN
          CASE WHEN c.CapabilityCode LIKE N'%failed_checks%' THEN 0 ELSE 1 END
        WHEN @Question LIKE N'%latest%' OR @Question LIKE N'%status%' THEN
          CASE WHEN c.CapabilityCode LIKE N'%latest%' THEN 0 ELSE 1 END
        ELSE
          CASE WHEN c.CapabilityCode LIKE N'%verify%' THEN 0 ELSE 1 END
      END,
      c.CapabilityCode;
END;
```

Codex may improve this SQL, but the key point is: capability lookup must come from SQL metadata and must permission-filter by tenant/user.

#### `ai.usp_CreateRun`

```sql
CREATE OR ALTER PROCEDURE ai.usp_CreateRun
    @RunId UNIQUEIDENTIFIER,
    @TenantId UNIQUEIDENTIFIER,
    @UserId UNIQUEIDENTIFIER,
    @CorrelationId NVARCHAR(100),
    @Question NVARCHAR(MAX),
    @DetectedLanguage NVARCHAR(20) = NULL,
    @DomainHint NVARCHAR(100) = NULL,
    @Status NVARCHAR(50)
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO ai.Run
    (
        RunId,
        TenantId,
        UserId,
        CorrelationId,
        Question,
        DetectedLanguage,
        DomainHint,
        Status,
        CreatedAtUtc
    )
    VALUES
    (
        @RunId,
        @TenantId,
        @UserId,
        @CorrelationId,
        @Question,
        @DetectedLanguage,
        @DomainHint,
        @Status,
        SYSUTCDATETIME()
    );
END;
```

### Acceptance Criteria

- SQL connection uses configuration only.
- SQL helper supports cancellation.
- Repository procedures are idempotent.
- SQL permission denial is converted to a clear application exception.
- Capability lookup is SQL-backed.

---

## 10. Workstream C — SQL-Backed Capability Retrieval

### Goal

Replace `InMemoryCapabilitySearchService` in production.

### Required Class

```text
src/TILSOFTAI.Persistence/Capabilities/SqlCapabilitySearchService.cs
```

### Interface

Use the existing `ICapabilitySearchService` if already defined. If it must be refined, keep the contract stable for the orchestrator:

```csharp
public interface ICapabilitySearchService
{
    Task<IReadOnlyList<CapabilityDescriptor>> SearchAsync(
        RequestContext context,
        string question,
        string? domainHint,
        CancellationToken cancellationToken);
}
```

### Implementation Rules

- Read from SQL metadata.
- Always pass tenant and user.
- Restrict Sprint 01 to `ModuleCode = 'Model'`.
- Filter inactive modules/capabilities/tools.
- Filter tools by required permission.
- Return only top K candidates.
- Do not expose all tools to the agent.
- Do not use hardcoded capabilities in production.
- If no capability is found, return an empty list and let orchestrator fail gracefully.

### Recommended Search Strategy for Sprint 01

Use keyword search plus metadata scoring first. Vector search can remain optional.

Priority ordering:

1. Exact project-run verification terms.
2. Latest/status terms.
3. Failed/failure/check terms.
4. General Model run terms.

Vietnamese synonyms to include in SQL metadata or search logic:

```text
kiểm tra
chứng thực
đạt mục tiêu run
trạng thái mới nhất
lỗi
failed
run
model
```

### Future Vector Hook

Do not block Sprint 01 on vector ANN. However, keep the design ready:

```csharp
public interface IEmbeddingGeneratorClient
{
    Task<float[]> EmbedAsync(string input, CancellationToken cancellationToken);
}
```

Later Sprint 02 can use SQL Server 2025 `VECTOR_DISTANCE` or `VECTOR_SEARCH` for hybrid retrieval.

### Acceptance Criteria

- `InMemoryCapabilitySearchService` is not used in production.
- SQL metadata changes affect runtime behavior without recompiling code.
- Unauthorized user receives no callable capabilities.
- Tests prove the system uses SQL metadata.

---

## 11. Workstream D — Model Parameter Binding

### Goal

Bind tool parameters safely without inventing business values.

### Current Issue

The current workflow may default to `MODEL-001` when no project code is present. That is not acceptable.

### Required Class

```text
src/TILSOFTAI.Application/Abstractions/IModelParameterBinder.cs
src/TILSOFTAI.Agent/Model/ModelParameterBinder.cs
```

### Contract

```csharp
public interface IModelParameterBinder
{
    Task<ParameterBindingResult> BindAsync(
        RequestContext context,
        string question,
        CapabilityDescriptor capability,
        CancellationToken cancellationToken);
}

public sealed record ParameterBindingResult(
    bool IsComplete,
    JsonObject Parameters,
    IReadOnlyList<string> MissingParameters,
    IReadOnlyList<string> ValidationErrors);
```

### Binding Rules

- Extract `projectCode` using a regex, not by simple split.
- Pattern: `MODEL-[0-9]{3}` for Sprint 01.
- Preserve uppercase normalization.
- Do not default to `MODEL-001`.
- If missing, fail with a clarification-needed result.
- Validate the parameter object against the tool input schema or typed DTO.

### Example Regex

```csharp
private static readonly Regex ProjectCodeRegex =
    new(@"\bMODEL-[0-9]{3}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
```

### Missing Parameter Behavior

If the user asks:

```text
Verify the latest Model run.
```

The system must not assume `MODEL-001`. It should return a safe response:

```json
{
  "status": "NeedsClarification",
  "message": "Please provide the Model project code, for example MODEL-001."
}
```

If the API response contract does not currently support `NeedsClarification`, add it.

### Acceptance Criteria

- `MODEL-001` is never silently invented.
- Invalid project codes are rejected.
- Missing project codes produce a clarification-needed result.
- Parameter binding tests cover English and Vietnamese questions.

---

## 12. Workstream E — SQL-Backed Model Tool Runtime

### Goal

Replace fake `ModelToolRuntime` in production.

### Required Class

```text
src/TILSOFTAI.Persistence/Tools/SqlModelToolRuntime.cs
```

### Interface

Use existing `IToolRuntime` if already defined.

```csharp
public interface IToolRuntime
{
    Task<ToolExecutionResult> ExecuteAsync(
        RequestContext context,
        ToolExecutionRequest request,
        CancellationToken cancellationToken);
}
```

### Runtime Rules

- Accept only `ToolType = SqlStoredProcedure`.
- Accept only Model tool procedures in Sprint 01.
- Procedure name must come from `request.Tool.SqlProcedureName`.
- Procedure name must pass a strict whitelist/pattern check.
- Execute using `CommandType.StoredProcedure`.
- Add common parameters:
  - `@TenantId`
  - `@UserId`
  - `@CorrelationId`
- Add input parameters from validated JSON.
- Use tool `TimeoutMs` for command timeout.
- Enforce `MaxRows` in application even if SQL also uses `TOP`.
- Convert SQL permission-denial exceptions into a safe application response.

### Stored Procedure Name Validation

Use strict validation before executing any metadata-provided procedure name.

Allowed examples:

```text
model.usp_GetProjectRunVerification
model.usp_GetLatestProjectRun
model.usp_GetFailedRunChecks
```

Rejected examples:

```text
model.usp_GetProjectRunVerification; DROP TABLE model.Project
../model.usp_GetProjectRunVerification
dbo.sp_executesql
sale.usp_GetRevenue
```

Suggested code:

```csharp
private static readonly Regex SafeStoredProcedureName =
    new(@"^model\.[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
```

### Parameter Mapping Rule

Map JSON properties to SQL parameters by PascalCase convention:

```json
{
  "projectCode": "MODEL-001"
}
```

becomes:

```sql
@ProjectCode = N'MODEL-001'
```

Do not create a special hardcoded branch for `projectCode` unless the existing contracts make generic mapping impossible. If a special branch is required, isolate it in a Model-specific parameter mapper and document it.

### Result Mapping

Map result sets into:

```csharp
IReadOnlyList<IReadOnlyDictionary<string, object?>> rows
```

Rules:

- Preserve column names exactly as returned by SQL.
- Convert `DateTime` to ISO 8601 strings when serializing artifacts.
- Convert decimals to JSON numbers.
- Nulls remain null.
- Do not include sensitive fields if SQL excluded them.

### Acceptance Criteria

- Production `IToolRuntime` executes SQL stored procedures.
- SQL stored procedure receives tenant/user/correlation.
- Unauthorized user is denied by SQL.
- No fake rows are returned by production runtime.
- Tests prove execution uses SQL by changing seed data and observing changed output.

---

## 13. Workstream F — Run, ToolCall, Artifact, and Provenance Persistence

### Goal

Make every AI run auditable.

### Required Classes

```text
src/TILSOFTAI.Persistence/Runs/SqlAiRunRepository.cs
src/TILSOFTAI.Persistence/Artifacts/SqlArtifactRepository.cs
src/TILSOFTAI.Persistence/Artifacts/FileSystemArtifactContentStore.cs
```

### Minimum Repository Contracts

```csharp
public interface IAiRunRepository
{
    Task CreateRunAsync(AiRunRecord run, CancellationToken cancellationToken);
    Task UpdateRunStatusAsync(Guid runId, string status, string? selectedCapabilityCode, CancellationToken cancellationToken);
    Task RecordToolCallAsync(ToolCallRecord toolCall, CancellationToken cancellationToken);
    Task CompleteToolCallAsync(Guid toolCallId, string status, int rowCount, CancellationToken cancellationToken);
    Task<RunDetailsResponse?> GetRunAsync(RequestContext context, Guid runId, CancellationToken cancellationToken);
}
```

```csharp
public interface IArtifactRepository
{
    Task CreateArtifactAsync(ArtifactRecord artifact, CancellationToken cancellationToken);
    Task CreateProvenanceAsync(ProvenanceRecord provenance, CancellationToken cancellationToken);
    Task<ArtifactMetadataResponse?> GetArtifactMetadataAsync(RequestContext context, Guid artifactId, CancellationToken cancellationToken);
}
```

```csharp
public interface IArtifactContentStore
{
    Task<StoredArtifactContent> SaveAsync(
        RequestContext context,
        Guid runId,
        Guid artifactId,
        string artifactType,
        string contentType,
        Stream content,
        CancellationToken cancellationToken);
}
```

### Artifact Content Storage Rules

Use local file-system storage for Sprint 01.

Path pattern:

```text
{Artifacts:RootPath}/tenant-{tenantId}/run-{runId}/{artifactId}-{artifactType}.json
```

Rules:

- Root path comes from configuration.
- Create directories if missing.
- Do not allow path traversal.
- Write files atomically where possible.
- Compute SHA-256.
- Record content type and size.

### Required Artifact Types

```text
RawToolResult
SanitizedToolResult
ContextPackage
FinalAnswer
```

### Recommended Artifact JSON Shape

#### Raw tool result

```json
{
  "runId": "...",
  "toolName": "Model.GetProjectRunVerification",
  "capabilityCode": "model.project.run.verify",
  "executedAtUtc": "2026-04-30T00:00:00Z",
  "rowCount": 1,
  "rows": []
}
```

#### Sanitized tool result

```json
{
  "runId": "...",
  "toolName": "Model.GetProjectRunVerification",
  "capabilityCode": "model.project.run.verify",
  "sanitizedAtUtc": "2026-04-30T00:00:00Z",
  "rowCount": 1,
  "schema": [],
  "rows": []
}
```

#### Context package

```json
{
  "question": "...",
  "module": "Model",
  "capabilityCode": "model.project.run.verify",
  "toolName": "Model.GetProjectRunVerification",
  "sanitizedSummary": {},
  "schema": [],
  "artifactRefs": [],
  "provenance": []
}
```

### Provenance Rules

Every final answer must include provenance from persisted records, not generated by the LLM alone.

Provenance minimum:

```json
{
  "toolName": "Model.GetProjectRunVerification",
  "filters": ["ProjectCode = MODEL-001"],
  "artifactId": "..."
}
```

### Acceptance Criteria

- Run is persisted at start.
- Run status is updated at completion/failure.
- ToolCall is persisted.
- Raw artifact is persisted.
- Sanitized artifact is persisted.
- Context package artifact is persisted.
- Provenance references actual sanitized artifact.
- Artifact metadata can be retrieved through API.

---

## 14. Workstream G — Sanitization and Context Packaging

### Goal

Ensure the AI model receives only sanitized, compact, evidence-based context.

### Tasks

#### G1. Review existing sanitizer

The existing `SanitizerAndContextPackager` can be kept if it already:

- Removes or masks sensitive fields.
- Limits rows.
- Includes schema.
- Includes provenance.
- Includes artifact references.

#### G2. Add enterprise rules

Add or verify these rules:

- Drop fields named or tagged as sensitive.
- Mask emails, phone numbers, and internal reviewer fields.
- Preserve numeric fields needed for analysis.
- Include row count and truncation flag.
- Include source tool and filters.
- Include artifact IDs.

#### G3. Context budget

Add configurable limits:

```json
{
  "ContextPackaging": {
    "MaxRowsForAi": 50,
    "MaxColumnsForAi": 30,
    "MaxEstimatedTokens": 8000
  }
}
```

For Sprint 01 Model domain, row count is small, but the framework must be ready for larger ERP data.

### Acceptance Criteria

- Sensitive fields do not appear in context artifacts.
- AI prompt receives sanitized context only.
- Context includes schema, metadata, and provenance.
- Tests verify masking and truncation.

---

## 15. Workstream H — OpenAI-Compatible Local AI Client

### Goal

Replace `DeterministicLocalAiClient` in production with a real HTTP client that calls the configured local AI endpoint.

### Required Class

```text
src/TILSOFTAI.Infrastructure/LocalAi/OpenAICompatibleLocalAiClient.cs
```

### Configuration

Use existing configuration keys:

```json
{
  "Ai": {
    "OpenAICompatible": {
      "BaseUrl": "",
      "ApiKey": "",
      "ChatModel": "",
      "EmbeddingModel": "",
      "TimeoutSeconds": 60
    }
  }
}
```

Local developer example:

```text
Ai__OpenAICompatible__BaseUrl=http://192.168.8.247:6688/v1
```

The IP must be in environment/user secrets, not source code.

### HTTP Client Rules

- Use `IHttpClientFactory`.
- Configure base address from options.
- Configure timeout from options.
- Add authorization header only when API key is configured.
- Do not log prompt content or raw response content.
- Log endpoint host, model name, latency, status code, and correlation ID.
- Handle cancellation.
- Handle non-success responses.
- Parse structured response safely.

### Chat Completion Request

Use OpenAI-compatible endpoint:

```http
POST {BaseUrl}/chat/completions
```

Minimum request shape:

```json
{
  "model": "configured-chat-model",
  "temperature": 0.1,
  "messages": [
    {
      "role": "system",
      "content": "..."
    },
    {
      "role": "user",
      "content": "..."
    }
  ]
}
```

### Required AI Output Contract

Ask the model to return strict JSON:

```json
{
  "summary": "string",
  "tables": [
    {
      "title": "string",
      "columns": ["string"],
      "rows": [["string"]]
    }
  ],
  "insights": ["string"],
  "caveats": ["string"],
  "provenance": [
    {
      "toolName": "string",
      "filters": ["string"],
      "artifactId": "guid"
    }
  ],
  "followUps": ["string"]
}
```

### Fail-Closed Parsing

If the AI returns invalid JSON:

- Do not fabricate a final answer.
- Mark run as failed or `AnswerGenerationFailed`.
- Store the AI failure metadata without storing sensitive prompt content.
- Return a safe error response.

Optional Sprint 01 fallback:

A deterministic answer may be allowed only in `Testing` environment. It must not be used in production.

### Embedding Endpoint

Implement `EmbedAsync` using:

```http
POST {BaseUrl}/embeddings
```

Sprint 01 may not require embeddings for capability retrieval, but implementing the client prepares for Sprint 02.

### Acceptance Criteria

- Production calls configured local AI endpoint.
- Base URL, model, API key, and timeout come from configuration.
- Deterministic AI is not used in production.
- Invalid AI output fails closed.
- Tests cover success, non-success HTTP status, invalid JSON, and cancellation.

---

## 16. Workstream I — Microsoft Agent Framework Integration

### Goal

Move from custom-only workflow toward Microsoft Agent Framework as the controlled brain, while preserving deterministic enterprise guardrails.

### CTO Position

The agent must not be free to call arbitrary SQL or arbitrary tools. Microsoft Agent Framework is used as the “brain” for reasoning and structured steps, but TILSOFTAI remains responsible for:

- Capability shortlisting.
- Tool allowlisting.
- Parameter validation.
- SQL permission enforcement.
- Artifact persistence.
- Sanitization.
- Provenance validation.

### Minimum Sprint 01 Integration

Add a stable application abstraction:

```csharp
public interface IAgentBrain
{
    Task<AgentPlanResult> PlanAsync(
        RequestContext context,
        AgentPlanningInput input,
        CancellationToken cancellationToken);
}
```

```csharp
public sealed record AgentPlanningInput(
    string Question,
    string? DomainHint,
    IReadOnlyList<CapabilityDescriptor> CandidateCapabilities);

public sealed record AgentPlanResult(
    CapabilityDescriptor SelectedCapability,
    JsonObject Parameters,
    IReadOnlyList<string> ReasoningSummary,
    bool NeedsClarification,
    IReadOnlyList<string> MissingParameters);
```

Create:

```text
src/TILSOFTAI.Agent/AgentFramework/AgentFrameworkBrain.cs
```

### Package Guidance

Use official Microsoft Agent Framework packages where available.

Expected package direction:

```powershell
dotnet add src/TILSOFTAI.Agent/TILSOFTAI.Agent.csproj package Microsoft.Agents.AI.OpenAI --prerelease
```

If the local AI endpoint is compatible with OpenAI Chat Completions, prefer the OpenAI-compatible path. If the installed local runtime is Ollama-compatible, consider Microsoft.Extensions.AI.Ollama only if it matches the actual endpoint contract.

### Controlled Planning Prompt

The planning agent must receive only shortlisted Model capabilities.

Prompt rules:

```text
You are TILSOFTAI Model-domain planning agent.
You must select exactly one capability from the provided candidates.
You must not invent tools.
You must not generate SQL.
You must bind only parameters that are present in the user question.
If projectCode is missing, set needsClarification = true.
Return strict JSON only.
```

### Important Design Constraint

Sprint 01 must still work even if the Agent Framework package surface changes because it is prerelease. To avoid blocking the enterprise runtime, isolate it behind `IAgentBrain`.

Allowed fallback:

- In `Testing` environment: deterministic planning brain.
- In production: Agent Framework brain should be the default once package integration compiles.

Not allowed:

- Silently using a fake planner in production while claiming Microsoft Agent Framework is active.

### Acceptance Criteria

- Agent planning sees only SQL-shortlisted tools.
- Agent cannot call arbitrary tools.
- Agent cannot execute SQL.
- Missing parameter behavior is safe.
- Agent Framework integration is isolated behind `IAgentBrain`.
- Production configuration clearly indicates whether Agent Framework is active.

---

## 17. Workstream J — API Contract and Error Handling

### Goal

Make API behavior enterprise-grade.

### Required API Behavior

#### Successful run

```json
{
  "runId": "guid",
  "status": "Completed",
  "answer": {
    "summary": "...",
    "tables": [],
    "insights": [],
    "caveats": [],
    "provenance": [],
    "followUps": []
  },
  "artifactIds": [],
  "correlationId": "..."
}
```

#### Missing project code

```json
{
  "runId": "guid",
  "status": "NeedsClarification",
  "message": "Please provide the Model project code, for example MODEL-001.",
  "missingParameters": ["projectCode"],
  "correlationId": "..."
}
```

#### Unauthorized

```json
{
  "status": "Forbidden",
  "message": "User does not have permission to access Model project run data.",
  "correlationId": "..."
}
```

#### No capability found

```json
{
  "runId": "guid",
  "status": "NoCapabilityFound",
  "message": "No permitted Model capability matched the question.",
  "correlationId": "..."
}
```

### Header Rules

All non-health endpoints require:

```text
X-Tenant-Id
X-User-Id
```

All responses should return:

```text
X-Correlation-Id
```

### Authentication Note

Sprint 01 may keep trusted-header mode for local development, but must document that production should derive tenant/user from a trusted gateway or validated claims. Do not represent raw client-supplied `X-User-Id` as a complete production authentication solution.

### Acceptance Criteria

- Error responses are structured.
- Correlation ID is always returned.
- Missing headers return `400`.
- Unauthorized tool execution returns `403` or an equivalent safe domain response.
- OpenAPI docs include required headers.

---

## 18. Workstream K — Testing Strategy

### Goal

Prove Sprint 01 behavior with automated tests.

### Test Types

#### Unit tests

Required coverage:

- Project code extraction.
- Missing project code behavior.
- Invalid project code behavior.
- Capability search mapping.
- Stored procedure name validation.
- Sanitization masking.
- Context packaging truncation.
- AI response parsing.
- Provenance validation.

#### Integration tests

Required coverage:

- SQL capability retrieval from seeded metadata.
- SQL Model tool execution with authorized user.
- SQL Model tool execution with unauthorized user.
- Cross-tenant isolation.
- Artifact metadata persistence.
- Run status persistence.

#### Local AI client tests

Use a test HTTP server or mocked `HttpMessageHandler`.

Required coverage:

- Valid chat completion response.
- Invalid JSON response.
- HTTP 500 response.
- Timeout/cancellation.
- API key header behavior.

#### Agent behavior tests

Required prompts:

```text
Verify whether MODEL-001 achieved its run target.
MODEL-001 có đạt mục tiêu run không?
Show latest status for MODEL-002.
Which checks failed for MODEL-001?
Verify latest Model run.
Verify MODEL-XYZ.
```

Expected behavior:

- First two select `model.project.run.verify`.
- Latest status selects `model.project.run.latest`.
- Failed checks selects `model.project.run.failed_checks`.
- Missing code returns clarification.
- Invalid code returns validation error.

### Test Framework Recommendation

Move from console assertions toward xUnit or NUnit for enterprise test reporting. If this is too large for Sprint 01, at least add xUnit for new tests while keeping old console acceptance suites temporarily.

### Acceptance Criteria

- `dotnet test TILSOFTAI.sln` passes.
- CI runs tests.
- Tests prove production services are SQL-backed when not in `Testing` environment.

---

## 19. Workstream L — Observability and Logging

### Goal

Make runs traceable without leaking sensitive data.

### Required Logging Events

Log these events with `CorrelationId`, `TenantId`, `UserId`, and `RunId` where available:

```text
RunCreated
CapabilitySearchStarted
CapabilitySearchCompleted
AgentPlanningStarted
AgentPlanningCompleted
ToolExecutionStarted
ToolExecutionCompleted
ArtifactSaved
ContextPackaged
LocalAiCallStarted
LocalAiCallCompleted
RunCompleted
RunFailed
PermissionDenied
```

### Sensitive Data Rules

Do not log:

- Full prompts.
- Raw tool rows.
- Full AI responses.
- Connection strings.
- API keys.
- Sensitive field values.

Allowed:

- Row count.
- Column count.
- Artifact ID.
- Tool name.
- Capability code.
- Duration.
- Status.
- Error category.

### OpenTelemetry

If `Observability:EnableOpenTelemetry` already exists, Sprint 01 may add basic tracing. If this is too large, log structured events first and defer full OpenTelemetry exporters to Sprint 02.

### Acceptance Criteria

- Every run can be traced by correlation ID.
- Sensitive payloads are not logged.
- Tool execution duration and AI call duration are logged.

---

## 20. Workstream M — Documentation Updates

### Goal

Make Codex, developers, and operators understand how to run the Sprint 01 system.

### Required README Updates

Add:

```markdown
## Sprint 01 Runtime Modes

- Testing: deterministic in-process services.
- Production/Development: SQL Server + local AI endpoint.
```

Add local setup:

```powershell
$env:ConnectionStrings__TilsoftAi="Server=localhost;Database=TILSOFTAI;User Id=sa;Password=123;Encrypt=False;TrustServerCertificate=True"
$env:Ai__OpenAICompatible__BaseUrl="http://192.168.8.247:6688/v1"
$env:Ai__OpenAICompatible__ApiKey="local-placeholder-or-secret"
$env:Ai__OpenAICompatible__ChatModel="<model>"
$env:Ai__OpenAICompatible__EmbeddingModel="<embedding-model>"
$env:Artifacts__RootPath="C:\\TILSOFTAI\\artifacts"
```

Add database setup:

```powershell
sqlcmd -S localhost -U sa -P 123 -i database/TILSOFTAI/000_create_database.sql
sqlcmd -S localhost -U sa -P 123 -d TILSOFTAI -i database/TILSOFTAI/010_core_schema.sql
sqlcmd -S localhost -U sa -P 123 -d TILSOFTAI -i database/TILSOFTAI/020_security_schema.sql
sqlcmd -S localhost -U sa -P 123 -d TILSOFTAI -i database/TILSOFTAI/030_ai_metadata_schema.sql
sqlcmd -S localhost -U sa -P 123 -d TILSOFTAI -i database/TILSOFTAI/040_artifact_schema.sql
sqlcmd -S localhost -U sa -P 123 -d TILSOFTAI -i database/TILSOFTAI/050_model_domain_schema.sql
sqlcmd -S localhost -U sa -P 123 -d TILSOFTAI -i database/TILSOFTAI/060_model_seed_data.sql
sqlcmd -S localhost -U sa -P 123 -d TILSOFTAI -i database/TILSOFTAI/070_model_tools.sql
sqlcmd -S localhost -U sa -P 123 -d TILSOFTAI -i database/TILSOFTAI/080_ai_runtime_procedures.sql
```

Add E2E test request:

```powershell
curl -X POST "http://localhost:5000/api/v1/ai/runs" `
  -H "Content-Type: application/json" `
  -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000001" `
  -H "X-User-Id: 00000000-0000-0000-0000-000000000101" `
  -H "X-Correlation-Id: sprint01-e2e-001" `
  -d '{"question":"Verify whether MODEL-001 achieved its run target.","domainHint":"Model"}'
```

### Required AGENTS.md Updates

Add this section:

```markdown
## Sprint 01 Production Rules

- In-memory services are allowed only in Testing environment or test projects.
- Production services must be SQL-backed and local-AI-backed.
- Tool stored procedure names must come from ai.Tool metadata.
- Every Model tool execution must pass TenantId, UserId, and CorrelationId to SQL.
- Do not default missing projectCode to MODEL-001.
- Missing business parameters must return NeedsClarification.
- Deterministic AI clients are test-only.
- Final answers must be grounded in persisted sanitized artifacts and provenance.
```

### Required Sprint Completion Note

Create:

```text
docs/sprints/sprint-01-completion.md
```

It must include:

- What was implemented.
- What remains deferred.
- How to run tests.
- Known risks.
- Evidence screenshots or command output snippets if available.

---

## 21. Codex Execution Plan — PR Sequence

Codex should implement Sprint 01 in small, reviewable changes.

### PR 1 — Build and DI Stabilization

Scope:

- Fix missing implementation files.
- Clean production/testing DI split.
- Fix request context lifetime.
- Ensure build passes.

Acceptance:

```powershell
dotnet build TILSOFTAI.sln
```

### PR 2 — SQL Runtime Foundation

Scope:

- Add SQL connection factory.
- Add SQL helper.
- Add runtime SQL procedures.
- Add SQL-backed run/artifact repositories.

Acceptance:

- SQL scripts run idempotently.
- Repositories pass integration tests.

### PR 3 — SQL Capability Search and Model Tool Runtime

Scope:

- Implement `SqlCapabilitySearchService`.
- Implement `SqlModelToolRuntime`.
- Add tool result mapping.
- Remove production use of fake runtime.

Acceptance:

- Authorized SQL tool execution works.
- Unauthorized SQL tool execution fails safely.

### PR 4 — Artifact Pipeline and Context Packaging

Scope:

- Persist raw artifact.
- Persist sanitized artifact.
- Persist context artifact.
- Persist provenance.
- Harden sanitizer.

Acceptance:

- Artifact metadata API returns real metadata.
- Final context uses sanitized data only.

### PR 5 — Local AI Client

Scope:

- Implement OpenAI-compatible chat client.
- Implement embedding client method.
- Add response parsing and failure behavior.
- Use test HTTP server in tests.

Acceptance:

- Production calls configured local endpoint.
- Invalid AI output fails closed.

### PR 6 — Agent Framework Brain

Scope:

- Add `IAgentBrain`.
- Integrate Microsoft Agent Framework behind adapter.
- Keep candidate tools allowlisted.
- Keep missing parameter behavior safe.

Acceptance:

- Agent planning only sees shortlisted Model tools.
- Production config uses Agent Framework brain where available.

### PR 7 — E2E Tests and Docs

Scope:

- Add E2E Model run test.
- Add README setup.
- Add AGENTS.md Sprint 01 production rules.
- Add sprint completion note.

Acceptance:

- `dotnet test` passes.
- Manual curl request works locally.

---

## 22. Detailed Codex Task Checklist

Codex must work through this checklist.

### Repository Preparation

- [ ] Read `README.md`.
- [ ] Read `AGENTS.md`.
- [ ] Read all SQL scripts under `database/TILSOFTAI`.
- [ ] Read current API `Program.cs`.
- [ ] Read current middleware.
- [ ] Read current capability service.
- [ ] Read current tool runtime.
- [ ] Read current local AI client.
- [ ] Read current orchestrator.
- [ ] Run build.
- [ ] Record initial failures.

### Configuration

- [ ] Validate `ConnectionStrings:TilsoftAi` exists for non-Testing.
- [ ] Validate `Ai:OpenAICompatible:BaseUrl` exists for non-Testing.
- [ ] Validate `Ai:OpenAICompatible:ChatModel` exists for non-Testing.
- [ ] Validate `Ai:OpenAICompatible:EmbeddingModel` exists for non-Testing.
- [ ] Validate `Artifacts:RootPath` exists for non-Testing.
- [ ] Do not require live values in committed appsettings.

### SQL Persistence

- [ ] Implement `ISqlConnectionFactory`.
- [ ] Implement SQL helper.
- [ ] Implement SQL run repository.
- [ ] Implement SQL artifact repository.
- [ ] Implement artifact content store.
- [ ] Add idempotent SQL scripts as needed.

### Capability Retrieval

- [ ] Implement SQL-backed capability search.
- [ ] Filter Model module only.
- [ ] Filter inactive records.
- [ ] Filter by effective permission.
- [ ] Return top K.
- [ ] Add tests.

### Tool Runtime

- [ ] Implement SQL-backed Model tool runtime.
- [ ] Validate stored procedure name.
- [ ] Pass tenant/user/correlation.
- [ ] Pass bound parameters.
- [ ] Enforce timeout.
- [ ] Enforce max rows.
- [ ] Map results.
- [ ] Add tests.

### Parameter Binding

- [ ] Implement projectCode extraction regex.
- [ ] Reject invalid project code.
- [ ] Return clarification if missing.
- [ ] Remove default `MODEL-001` behavior.
- [ ] Add English and Vietnamese tests.

### Artifact and Context

- [ ] Save raw artifact.
- [ ] Sanitize rows.
- [ ] Save sanitized artifact.
- [ ] Save context package artifact.
- [ ] Save provenance.
- [ ] Verify final answer references actual artifact.

### Local AI

- [ ] Implement HTTP chat completion call.
- [ ] Implement embeddings call.
- [ ] Add options binding.
- [ ] Add typed request/response models.
- [ ] Add invalid JSON handling.
- [ ] Add HTTP failure handling.
- [ ] Add cancellation handling.
- [ ] Add tests with fake HTTP handler.

### Agent Brain

- [ ] Add `IAgentBrain` contract.
- [ ] Add Agent Framework implementation.
- [ ] Ensure agent sees only candidate tools.
- [ ] Ensure agent returns strict JSON plan.
- [ ] Ensure tool execution remains outside agent direct control.
- [ ] Add tests.

### API

- [ ] Return structured success response.
- [ ] Return structured clarification response.
- [ ] Return structured no-capability response.
- [ ] Return structured forbidden response.
- [ ] Ensure `X-Correlation-Id` response header.
- [ ] Update OpenAPI metadata.

### Documentation

- [ ] Update README.
- [ ] Update AGENTS.md.
- [ ] Add `docs/sprints/sprint-01-completion.md`.
- [ ] Document local environment variables.
- [ ] Document SQL setup order.
- [ ] Document E2E curl.

---

## 23. End-to-End Runtime Sequence

Codex must implement this sequence.

```text
1. API receives request.
2. Middleware validates X-Tenant-Id and X-User-Id.
3. Middleware sets RequestContext.
4. Orchestrator creates ai.Run with Status = Running.
5. SqlCapabilitySearchService searches SQL metadata.
6. Agent brain selects one capability from candidates.
7. Parameter binder extracts projectCode.
8. Parameter binder validates input schema.
9. Orchestrator records ai.ToolCall start.
10. SqlModelToolRuntime executes stored procedure.
11. SQL enforces tenant/user permission.
12. Tool result rows returned.
13. Raw result artifact saved.
14. Sanitizer removes/masks sensitive fields.
15. Sanitized result artifact saved.
16. Provenance persisted.
17. Context packager builds compact context.
18. Context artifact saved.
19. OpenAICompatibleLocalAiClient calls local AI.
20. AI response parsed into FinalAnswer.
21. Provenance validator verifies tool/artifact references.
22. Final answer artifact saved.
23. ai.Run updated to Completed.
24. API returns response.
```

---

## 24. Required Guardrail: Provenance Validator

### Goal

Prevent ungrounded answers.

### Required Class

```text
src/TILSOFTAI.Application/Runs/FinalAnswerProvenanceValidator.cs
```

### Rules

- Final answer must contain at least one provenance item for data-bearing answers.
- Provenance tool name must match executed tool name.
- Provenance artifact ID must exist in the current run.
- Provenance filters must match or be a subset of actual tool filters.
- If AI returns provenance that does not match, override with system provenance or fail closed.

Preferred Sprint 01 behavior:

- Trust system-generated provenance more than model-generated provenance.
- The model may phrase provenance, but the application should attach the authoritative provenance.

### Acceptance Criteria

- AI cannot invent a tool name in provenance.
- AI cannot reference an artifact from another run.
- Tests cover invalid provenance.

---

## 25. SQL Seed Data Requirements

Sprint 01 E2E tests need stable seed data.

Minimum tenants:

```text
Tenant A: active
Tenant B: active
```

Minimum users:

```text
Authorized Model user in Tenant A
Unauthorized user in Tenant A
Authorized Model user in Tenant B
```

Minimum permissions:

```text
model.project.run.read
```

Minimum Model projects:

```text
Tenant A / MODEL-001 / Passed
Tenant A / MODEL-002 / Warning
Tenant B / MODEL-001 / Different data from Tenant A
```

Minimum run checks:

```text
MODEL-001: no failed checks, one warning check
MODEL-002: at least one failed check or warning depending seed intent
Sensitive check row exists but should not leak if IsSensitive = 1
```

### Acceptance Criteria

- Cross-tenant queries never return the other tenant’s data.
- Unauthorized user cannot call Model tool procedures.
- Sensitive run check evidence is not returned or is masked.

---

## 26. Enterprise Security Considerations for Sprint 01

Sprint 01 is still local/MVP, but must not create bad architecture.

### Required

- Treat `X-User-Id` as trusted only in local/testing/trusted-gateway mode.
- Document that production must validate JWT/SSO or accept headers only from a trusted API gateway.
- Do not log secrets.
- Do not log raw prompts by default.
- Do not log artifact contents.
- Do not expose artifact files directly without authorization.
- API artifact metadata lookup must filter by tenant.

### Deferred to Sprint 02+

- Full JWT/SSO implementation.
- Rate limiting.
- Artifact download authorization.
- Full audit dashboard.
- Secret manager integration.

---

## 27. Performance and Reliability Requirements

### Timeouts

Minimum config:

```json
{
  "ExecutionBudget": {
    "MaxToolCalls": 3,
    "DefaultSqlTimeoutSeconds": 30,
    "DefaultAiTimeoutSeconds": 60,
    "MaxRowsPerTool": 5000
  }
}
```

### Retry Policy

Sprint 01 may retry local AI transient failures once or twice, but must not retry SQL permission errors.

Retry allowed:

```text
HTTP 408
HTTP 429
HTTP 500/502/503/504 from local AI
Transient network timeout
```

Retry forbidden:

```text
SQL permission denied
Invalid parameter
Missing parameter
No capability found
AI invalid JSON response, unless the retry explicitly asks for valid JSON and is capped
```

### Acceptance Criteria

- Request cancellation cancels SQL and AI calls.
- AI calls have timeout.
- SQL calls have timeout.
- Tool call count is capped.

---

## 28. Suggested Implementation Pseudocode

### Orchestrator

```csharp
public async Task<AiRunResponse> CreateRunAsync(
    RequestContext context,
    CreateAiRunRequest request,
    CancellationToken cancellationToken)
{
    var runId = Guid.NewGuid();
    await runRepository.CreateRunAsync(...);

    try
    {
        var candidates = await capabilitySearch.SearchAsync(
            context,
            request.Question,
            request.DomainHint,
            cancellationToken);

        if (candidates.Count == 0)
        {
            await runRepository.UpdateRunStatusAsync(runId, "NoCapabilityFound", null, cancellationToken);
            return AiRunResponse.NoCapabilityFound(...);
        }

        var plan = await agentBrain.PlanAsync(
            context,
            new AgentPlanningInput(request.Question, request.DomainHint, candidates),
            cancellationToken);

        if (plan.NeedsClarification)
        {
            await runRepository.UpdateRunStatusAsync(runId, "NeedsClarification", plan.SelectedCapability.CapabilityCode, cancellationToken);
            return AiRunResponse.NeedsClarification(...);
        }

        var toolCallId = Guid.NewGuid();
        await runRepository.RecordToolCallAsync(...);

        var toolResult = await toolRuntime.ExecuteAsync(
            context,
            new ToolExecutionRequest(plan.SelectedCapability.Tool, plan.Parameters),
            cancellationToken);

        await runRepository.CompleteToolCallAsync(toolCallId, "Completed", toolResult.Rows.Count, cancellationToken);

        var rawArtifact = await artifactPipeline.SaveRawAsync(...);
        var sanitized = sanitizer.Sanitize(toolResult);
        var sanitizedArtifact = await artifactPipeline.SaveSanitizedAsync(...);
        await artifactRepository.CreateProvenanceAsync(...);

        var contextPackage = contextPackager.Build(...);
        var contextArtifact = await artifactPipeline.SaveContextAsync(...);

        var aiResponse = await localAiClient.ChatAsync(
            new AiChatRequest(request.Question, contextPackage),
            cancellationToken);

        var finalAnswer = provenanceValidator.ValidateAndAttachSystemProvenance(aiResponse.Answer, ...);
        var finalAnswerArtifact = await artifactPipeline.SaveFinalAnswerAsync(...);

        await runRepository.UpdateRunStatusAsync(runId, "Completed", plan.SelectedCapability.CapabilityCode, cancellationToken);

        return AiRunResponse.Completed(runId, finalAnswer, ...);
    }
    catch (PermissionDeniedException ex)
    {
        await runRepository.UpdateRunStatusAsync(runId, "Forbidden", null, cancellationToken);
        return AiRunResponse.Forbidden(...);
    }
    catch (Exception ex)
    {
        await runRepository.UpdateRunStatusAsync(runId, "Failed", null, cancellationToken);
        throw;
    }
}
```

---

## 29. Codex Quality Bar

Codex must not stop after creating compile-only scaffolding. Each new component must be connected and tested.

### Code Quality

- Prefer small classes with single responsibility.
- Use dependency injection.
- Use options pattern.
- Use `ILogger<T>`.
- Use `CancellationToken`.
- Avoid static mutable state.
- Avoid swallowing exceptions.
- Avoid broad `catch` unless converting to a domain-safe result.
- Keep test doubles out of production DI.

### SQL Quality

- Idempotent scripts.
- No dynamic SQL.
- Use stored procedures for tool execution.
- Use parameterized commands.
- Enforce tenant filters.
- Enforce permission checks.
- Add indexes where needed.

### AI Quality

- Give the model sanitized context only.
- Ask for strict JSON.
- Parse strictly.
- Validate provenance.
- Fail closed on invalid output.
- Keep temperature low.
- Do not ask the model to calculate facts already calculated by SQL unless the calculation is simple and verified.

---

## 30. Sprint 01 Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---:|---|
| Agent Framework prerelease package changes | Medium | Isolate behind `IAgentBrain`; keep production-safe adapter. |
| Local AI endpoint does not support strict JSON | High | Use strict prompt, low temperature, parse/fail closed; document model requirement. |
| Local AI endpoint does not require API key | Low | Allow placeholder API key via config; do not hardcode. |
| SQL Server 2025 vector driver support uncertain | Low for Sprint 01 | Use keyword SQL retrieval first; defer vector optimization. |
| Fake services accidentally used in production | High | Environment-aware DI and tests that assert production registrations. |
| Tenant/user spoofing through headers | Medium | Document trusted-gateway mode; SQL still enforces user permissions. |
| Sensitive data leakage through logs | High | Structured logging with no prompt/raw data by default. |
| Missing projectCode silently defaulted | High | Remove default and add NeedsClarification. |

---

## 31. Sprint 01 Acceptance Test Matrix

| Test ID | Scenario | Expected Result |
|---|---|---|
| S01-E2E-001 | Authorized user asks “Verify whether MODEL-001 achieved its run target.” | Completed answer, SQL-backed data, artifacts, provenance. |
| S01-E2E-002 | Authorized user asks Vietnamese: “MODEL-001 có đạt mục tiêu run không?” | Completed answer, selected verify capability. |
| S01-E2E-003 | Authorized user asks “Show latest status for MODEL-002.” | Completed answer, selected latest capability. |
| S01-E2E-004 | Authorized user asks “Which checks failed for MODEL-001?” | Completed answer, selected failed checks capability. |
| S01-E2E-005 | User omits project code | NeedsClarification, no default project code. |
| S01-E2E-006 | User sends invalid project code `MODEL-XYZ` | Validation error. |
| S01-E2E-007 | Unauthorized user asks valid Model question | Forbidden/permission denied. |
| S01-E2E-008 | Tenant A requests Tenant B project data through same project code | Only Tenant A data is returned. |
| S01-E2E-009 | Local AI returns invalid JSON | Run fails closed or AnswerGenerationFailed. |
| S01-E2E-010 | Artifact metadata requested from wrong tenant | Not found or forbidden. |

---

## 32. Commands Codex Should Use

### Build

```powershell
dotnet restore TILSOFTAI.sln /p:RestoreConfigFile="$PWD\NuGet.Config"
dotnet build TILSOFTAI.sln --no-restore /p:RestoreConfigFile="$PWD\NuGet.Config"
```

### Test

```powershell
dotnet test TILSOFTAI.sln --no-build /p:RestoreConfigFile="$PWD\NuGet.Config"
```

### Run API locally

```powershell
dotnet run --project src/TILSOFTAI.Api/TILSOFTAI.Api.csproj
```

### Validate SQL scripts

```powershell
sqlcmd -S localhost -U sa -P 123 -d TILSOFTAI -i database/TILSOFTAI/070_model_tools.sql
sqlcmd -S localhost -U sa -P 123 -d TILSOFTAI -i database/TILSOFTAI/080_ai_runtime_procedures.sql
```

### E2E API call

```powershell
curl -X POST "http://localhost:5000/api/v1/ai/runs" `
  -H "Content-Type: application/json" `
  -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000001" `
  -H "X-User-Id: 00000000-0000-0000-0000-000000000101" `
  -H "X-Correlation-Id: sprint01-e2e-001" `
  -d '{"question":"Verify whether MODEL-001 achieved its run target.","domainHint":"Model"}'
```

---

## 33. Final CTO Instruction to Codex

Codex must treat Sprint 01 as the transformation from a simulated MVP to a real enterprise runtime slice.

The correct end state is:

```text
SQL metadata -> controlled agent planning -> SQL stored procedure tool execution -> artifacts -> sanitized context -> local AI answer -> provenance
```

The incorrect end state is:

```text
hardcoded capability -> fake tool rows -> deterministic answer -> simulated provenance
```

Do not open new ERP domains. Do not add demo shortcuts. Do not let the LLM query SQL. Do not trust raw user headers as a full authentication solution. Do not default missing business parameters. Do not skip artifact persistence.

Sprint 01 is successful when the Model domain proves the framework architecture end-to-end using real SQL Server, real local AI integration, controlled Agent Framework planning, and auditable artifacts/provenance.

---

## 34. Reference Notes for Engineers

These references are for implementation alignment:

- Microsoft Agent Framework supports agents that use LLMs, tools, MCP servers, and graph-based workflows for multi-step tasks with type-safe routing, checkpointing, and human-in-the-loop support.
- Microsoft Agent Framework guidance distinguishes agents from workflows: use workflows when the process has well-defined steps and requires explicit execution control.
- Agent Framework providers can use OpenAI-compatible chat clients and local model options such as Ollama, depending on the runtime.
- SQL Server 2025 supports vector data type and vector search capabilities, but Sprint 01 does not need to depend on approximate vector search.
- ASP.NET Core `IHttpClientFactory` is the preferred pattern for resilient outbound HTTP clients.

