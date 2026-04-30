# Sprint 01 Completion Note

## Implemented

- Replaced source-linked project wiring with normal project references.
- Added environment-aware DI: production uses SQL/local-AI/agent services, Testing uses deterministic test doubles.
- Changed request context registration to scoped lifetime.
- Added SQL Server connection factory, stored procedure executor, SQL run/artifact repositories, SQL capability search, and SQL Model tool runtime.
- Added runtime SQL procedures in `080_ai_runtime_procedures.sql`.
- Extended Model seed data for Tenant A, Tenant B, authorized/unauthorized users, `MODEL-001`, `MODEL-002`, failed/warning/sensitive checks.
- Split artifact content storage from SQL artifact metadata.
- Added final-answer provenance validation.
- Added OpenAI-compatible local AI HTTP client with strict JSON parsing.
- Added safe Model parameter binding with no default project code.
- Added test coverage for DI guardrails, context lifetime, parameter binding, sanitized context, provenance correction, local AI parsing, Model scenarios, and SQL contract checks.

## Deferred

- Full JWT/SSO validation remains deferred; Sprint 01 documents trusted-gateway header usage.
- Live SQL Server and live local AI E2E are opt-in because CI does not own those services.
- Native SQL Server vector optimization is deferred; keyword metadata retrieval is implemented first.
- Microsoft Agent Framework package integration is isolated behind `IAgentBrain`; the current adapter keeps deterministic workflow semantics.

## Verification Commands

```powershell
dotnet build TILSOFTAI.sln
dotnet test TILSOFTAI.sln --no-build
dotnet run --project tests/TILSOFTAI.UnitTests/TILSOFTAI.UnitTests.csproj --no-build
dotnet run --project tests/TILSOFTAI.IntegrationTests/TILSOFTAI.IntegrationTests.csproj --no-build
dotnet run --project tests/TILSOFTAI.AgentTests/TILSOFTAI.AgentTests.csproj --no-build
dotnet run --project tests/TILSOFTAI.SqlTests/TILSOFTAI.SqlTests.csproj --no-build
```

## Known Risks

- Local AI strict JSON support varies by model; invalid responses fail closed.
- Local SQL Server 2025 setup is required for real end-to-end execution.
- Header-based user identity must be protected by a trusted gateway before production exposure.

