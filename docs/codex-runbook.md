# Codex Runbook

Run build and tests with a workspace-local .NET home:

```powershell
$env:DOTNET_CLI_HOME="$PWD\.dotnet_home"
dotnet build TILSOFTAI.sln /p:RestoreConfigFile="$PWD\NuGet.Config"
dotnet test TILSOFTAI.sln /p:RestoreConfigFile="$PWD\NuGet.Config"
```

