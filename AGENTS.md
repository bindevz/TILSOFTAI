# AGENTS.md - TILSOFTAI Implementation Rules

You are implementing TILSOFTAI, an enterprise-grade ERP AI Data Orchestration Framework.

## Mandatory Rules

1. Use C# and ASP.NET Core for backend implementation.
2. Use SQL Server 2025 for the database named TILSOFTAI.
3. Implement only the Model domain for MVP.
4. Do not implement Sale, Purchasing, Inventory, Finance, HR, or other ERP domains.
5. Never hardcode runtime values in source code.
6. Use configuration, options, environment variables, user secrets, or secret manager.
7. Every API request must require X-Tenant-Id and X-User-Id.
8. Every SQL tool stored procedure must accept @TenantId and @UserId.
9. SQL must enforce tenant filtering and permission checks.
10. The LLM must never generate SQL and execute it directly.
11. Agents may only call registered tools.
12. Tool input must be validated against JSON schema or typed DTOs.
13. Tool output must be stored as artifacts before being used by the LLM.
14. Only sanitized and compressed context may be sent to the AI model.
15. Every answer must include provenance.
16. Use async APIs and CancellationToken.
17. Add structured logging and correlation IDs.
18. Add unit, integration, SQL, and agent behavior tests.
19. All public APIs must have OpenAPI documentation.
20. When uncertain, choose the safer implementation and document the assumption.

## Coding Standards

* Prefer clean architecture boundaries.
* Keep domain logic out of controllers.
* Keep SQL execution out of agents.
* Keep prompts versioned.
* Keep tool definitions declarative.
* Keep sensitive values out of logs.
* Redact user question content if marked sensitive.

## Completion Criteria

The MVP is complete only when an API request can:

1. Receive a natural-language question.
2. Resolve the Model capability from SQL metadata.
3. Select a registered Model tool.
4. Bind and validate parameters.
5. Execute a SQL stored procedure with tenantId and userId.
6. Persist raw and sanitized artifacts.
7. Build a compressed context package.
8. Call the configured local AI endpoint.
9. Return answer, table, insight, provenance, and follow-up.
10. Pass automated tests.



## Sprint 01 Production Rules

- In-memory services are allowed only in Testing environment or test projects.
- Production services must be SQL-backed and local-AI-backed.
- Tool stored procedure names must come from `ai.Tool` metadata.
- Every Model tool execution must pass `TenantId`, `UserId`, and `CorrelationId` to SQL.
- Do not default missing `projectCode` to `MODEL-001`.
- Missing business parameters must return `NeedsClarification`.
- Deterministic AI clients are test-only.
- Final answers must be grounded in persisted sanitized artifacts and provenance.
- User ID headers are trusted only in local testing or trusted-gateway deployment mode.
- Do not log secrets, raw artifact contents, or sensitive prompts.
