param([switch]$Tests, [string]$OutputDirectory)
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
$project = Join-Path $PSScriptRoot 'src\ServerManager.App\ServerManager.App.csproj'
if (-not $OutputDirectory) {
    [xml]$projectXml = Get-Content -LiteralPath $project -Raw
    $version = [version][string]$projectXml.Project.PropertyGroup.Version
    $OutputDirectory = Join-Path $PSScriptRoot ('artifacts\desktop-' + $version.ToString(3))
}
& $taskSdk build $project -c Release -o $OutputDirectory --ignore-failed-sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($Tests) {
    & $taskSdk run --project (Join-Path $PSScriptRoot 'tests\ServerManager.Core.Tests\ServerManager.Core.Tests.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
