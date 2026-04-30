# Database Contract

SQL Server 2025 scripts live under `database/TILSOFTAI`. Tool stored procedures accept `@TenantId`, `@UserId`, and `@CorrelationId`, enforce tenant filters, and check `security.UserEffectivePermission`.

