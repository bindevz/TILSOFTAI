# TILSOFTAI

TILSOFTAI is an enterprise-grade ERP AI Data Orchestration Framework MVP for the Model domain only.

## Local Configuration

Runtime values are provided by environment variables, user secrets, or a secret manager. Do not commit live values.

Required keys:

- `ConnectionStrings__TilsoftAi`
- `Ai__OpenAICompatible__BaseUrl`
- `Ai__OpenAICompatible__ApiKey`
- `Ai__OpenAICompatible__ChatModel`
- `Ai__OpenAICompatible__EmbeddingModel`
- `Artifacts__RootPath`

## Build and Test

```powershell
$env:DOTNET_CLI_HOME="$PWD\.dotnet_home"
dotnet build TILSOFTAI.sln /p:RestoreConfigFile="$PWD\NuGet.Config"
dotnet test TILSOFTAI.sln /p:RestoreConfigFile="$PWD\NuGet.Config"
```

The API fails fast when required configuration is missing. Tests use deterministic in-process implementations and do not require SQL Server or a local AI server.

