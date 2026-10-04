param([string]$OutputRoot = (Join-Path $PSScriptRoot 'artifacts\release'))
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools\cli-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.tools\packages'
$taskSdk = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $taskSdk)) {
    $taskCommand = Get-Command dotnet -All -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $taskCommand) { throw 'Install .NET 10 SDK and make dotnet available in PATH.' }
    $taskSdk = $taskCommand.Source
}
$project = Join-Path $PSScriptRoot 'src\ServerManager.App\ServerManager.App.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$version = [string]$projectXml.Project.PropertyGroup.InformationalVersion
if ($version -notmatch '^\d+\.\d+\.\d+(-[a-zA-Z0-9.-]+)?$') { throw 'Invalid release version.' }
$packageName = 'ACServerManager-' + $version + '-win-x64'
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$publishDir = Join-Path $OutputRoot $packageName
$archive = Join-Path $OutputRoot ($packageName + '.zip')
if ((Test-Path -LiteralPath $publishDir) -or (Test-Path -LiteralPath $archive)) {
    throw 'Output already exists. Choose a new -OutputRoot to preserve previous builds.'
}
& $taskSdk run --project (Join-Path $PSScriptRoot 'tests\ServerManager.Core.Tests\ServerManager.Core.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
& $taskSdk publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'Publishing failed.' }
foreach ($document in @('LICENSE', 'README.md', 'DESKTOP.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $document) -Destination $publishDir
}
# Use notices from the exact runtime packages selected by the SDK.
$runtimeConfig = Get-Content -LiteralPath (Join-Path $publishDir 'AssettoServerManager.runtimeconfig.json') -Raw | ConvertFrom-Json
foreach ($framework in $runtimeConfig.runtimeOptions.includedFrameworks) {
    $runtime = $framework.name + '.Runtime.win-x64'
    $packageDir = Join-Path $env:NUGET_PACKAGES ($runtime.ToLowerInvariant() + '\' + $framework.version)
    $noticeDir = Join-Path $publishDir ('licenses\' + $runtime)
    New-Item -ItemType Directory -Path $noticeDir -Force | Out-Null
    $notices = @(Get-ChildItem -LiteralPath $packageDir -File | Where-Object { $_.Name -match '^(LICENSE|THIRD[-.]PARTY[-.]NOTICES)(\.TXT)?$' })
    if (-not ($notices | Where-Object { $_.Name -match '^LICENSE' })) { throw ('Missing license for ' + $runtime) }
    foreach ($notice in $notices) { Copy-Item -LiteralPath $notice.FullName -Destination $noticeDir }
}
if (-not (Test-Path -LiteralPath (Join-Path $publishDir 'AssettoServerManager.exe'))) { throw 'Missing app executable.' }
if (Test-Path -LiteralPath (Join-Path $publishDir 'settings.json')) { throw 'Personal settings must not be packaged.' }
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $OutputRoot 'SHA256SUMS.txt'), $hash + '  ' + [IO.Path]::GetFileName($archive) + "`n", [Text.UTF8Encoding]::new($false))
Write-Output ('Archive: ' + $archive)
Write-Output ('SHA256: ' + $hash)
