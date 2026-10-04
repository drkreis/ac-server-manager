param([switch]$Tests)
$ErrorActionPreference='Stop'
$env:DOTNET_CLI_HOME=Join-Path $PSScriptRoot '.tools\cli-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$env:NUGET_PACKAGES=Join-Path $PSScriptRoot '.tools\packages'
$taskSdk=Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $taskSdk)) {
    $taskCommand = Get-Command dotnet -All -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $taskCommand) { throw 'Install .NET 10 SDK and make dotnet available in PATH.' }
    $taskSdk = $taskCommand.Source
}
& $taskSdk build (Join-Path $PSScriptRoot 'src\ServerManager.App\ServerManager.App.csproj') -c Release -o (Join-Path $PSScriptRoot 'artifacts\desktop') --ignore-failed-sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($Tests) {
    & $taskSdk run --project (Join-Path $PSScriptRoot 'tests\ServerManager.Core.Tests\ServerManager.Core.Tests.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
