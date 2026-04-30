# TILSOFTAI

TILSOFTAI is an enterprise-grade ERP AI Data Orchestration Framework MVP for the Model domain only.

Sprint 01 moves the runtime from deterministic simulation toward a production-shaped flow:

```text
SQL metadata -> controlled Model planning -> SQL stored procedure tool execution -> artifacts -> sanitized context -> local AI answer -> provenance
```

## Local Configuration

Runtime values are provided by environment variables, user secrets, or a secret manager. Do not commit live values.

Required keys for non-Testing runtime:

```powershell
$env:ConnectionStrings__TilsoftAi="Server=localhost;Database=TILSOFTAI;User Id=sa;Password=123;Encrypt=False;TrustServerCertificate=True"
$env:Ai__OpenAICompatible__BaseUrl="http://192.168.8.247:6688/v1/"
$env:Ai__OpenAICompatible__ApiKey="local-placeholder-or-secret"
$env:Ai__OpenAICompatible__ChatModel="<configured-local-chat-model>"
$env:Ai__OpenAICompatible__EmbeddingModel="<configured-local-embedding-model>"
$env:Artifacts__RootPath="C:\TILSOFTAI\artifacts"
```

## SQL Setup Order

Run the scripts in order against SQL Server 2025:

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

## Build And Test

```powershell
$env:DOTNET_CLI_HOME="$PWD\.dotnet_home"
$env:APPDATA="$PWD\.appdata"
$env:LOCALAPPDATA="$PWD\.localappdata"
dotnet build TILSOFTAI.sln
dotnet test TILSOFTAI.sln --no-build
dotnet run --project tests/TILSOFTAI.UnitTests/TILSOFTAI.UnitTests.csproj --no-build
dotnet run --project tests/TILSOFTAI.IntegrationTests/TILSOFTAI.IntegrationTests.csproj --no-build
dotnet run --project tests/TILSOFTAI.AgentTests/TILSOFTAI.AgentTests.csproj --no-build
dotnet run --project tests/TILSOFTAI.SqlTests/TILSOFTAI.SqlTests.csproj --no-build
```

## Run API Locally

```powershell
dotnet run --project src/TILSOFTAI.Api/TILSOFTAI.Api.csproj
```

Example Model request:

```powershell
curl -X POST "http://localhost:5000/api/v1/ai/runs" `
  -H "Content-Type: application/json" `
  -H "X-Tenant-Id: 00000000-0000-0000-0000-000000000001" `
  -H "X-User-Id: 00000000-0000-0000-0000-000000000101" `
  -H "X-Correlation-Id: sprint01-e2e-001" `
  -d '{"question":"Verify whether MODEL-001 achieved its run target.","domainHint":"Model"}'
```

Live SQL/local-AI E2E depends on local SQL Server 2025 and a reachable OpenAI-compatible endpoint.

