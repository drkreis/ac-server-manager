param([string]$OutputDirectory, [string]$ApplicationDirectory)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools\cli-home'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.tools\packages'
$taskSdk = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $taskSdk)) { $taskSdk = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot ('artifacts\reliability-' + [Guid]::NewGuid().ToString('N')) }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a fresh output folder for isolated reliability checks.' }
if (-not $ApplicationDirectory) {
    $ApplicationDirectory = Join-Path $OutputDirectory 'app'
    & $taskSdk build (Join-Path $PSScriptRoot 'src\ServerManager.App\ServerManager.App.csproj') -c Release -o $ApplicationDirectory --ignore-failed-sources
    if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
}
$fixtureOutput = Join-Path $OutputDirectory 'process-fixture'
& $taskSdk build (Join-Path $PSScriptRoot 'tests\ServerManager.ProcessFixture\ServerManager.ProcessFixture.csproj') -c Release -o $fixtureOutput --ignore-failed-sources
if ($LASTEXITCODE -ne 0) { throw 'Test process build failed.' }
$env:DOTNET_ROOT = Split-Path -Parent $taskSdk
$app = Join-Path ([IO.Path]::GetFullPath($ApplicationDirectory)) 'AssettoServerManager.exe'
$results = Join-Path $OutputDirectory 'results'
$fixture = Join-Path $fixtureOutput 'AssettoServer.exe'
$process = Start-Process -FilePath $app -ArgumentList @('--runtime-qa', ('"' + $results + '"'), '--fixture', ('"' + $fixture + '"')) -WindowStyle Hidden -PassThru
try {
    if (-not $process.WaitForExit(180000)) {
        $process.Kill($true)
        throw 'Runtime checks exceeded three minutes; the owned test process tree was stopped.'
    }
    if ($process.ExitCode -ne 0) {
        $errorFile = Join-Path $results 'smoke-error.txt'
        if (Test-Path -LiteralPath $errorFile) { Get-Content -LiteralPath $errorFile }
        throw ('Runtime reliability checks failed: ' + $process.ExitCode)
    }
    Get-Content -LiteralPath (Join-Path $results 'runtime-result.txt')
    $folderResults = Join-Path $OutputDirectory 'folder-results'
    $folderProcess = Start-Process -FilePath $app -ArgumentList @('--folder-qa', ('"' + $folderResults + '"')) -WindowStyle Hidden -PassThru
    try {
        if (-not $folderProcess.WaitForExit(60000)) { $folderProcess.Kill(); throw 'Folder UI checks exceeded one minute.' }
        if ($folderProcess.ExitCode -ne 0) {
            $errorFile = Join-Path $folderResults 'smoke-error.txt'
            if (Test-Path -LiteralPath $errorFile) { Get-Content -LiteralPath $errorFile }
            throw ('Folder UI checks failed: ' + $folderProcess.ExitCode)
        }
        Get-Content -LiteralPath (Join-Path $folderResults 'folder-result.txt')
    }
    finally { $folderProcess.Dispose() }
    $recoveryResults = Join-Path $OutputDirectory 'recovery-results'
    $recoveryProcess = Start-Process -FilePath $app -ArgumentList @('--recovery-qa', ('"' + $recoveryResults + '"')) -WindowStyle Hidden -PassThru
    try {
        if (-not $recoveryProcess.WaitForExit(90000)) { $recoveryProcess.Kill(); throw 'Recovery UI checks exceeded ninety seconds.' }
        if ($recoveryProcess.ExitCode -ne 0) {
            $errorFile = Join-Path $recoveryResults 'smoke-error.txt'
            if (Test-Path -LiteralPath $errorFile) { Get-Content -LiteralPath $errorFile }
            throw ('Recovery UI checks failed: ' + $recoveryProcess.ExitCode)
        }
        Get-Content -LiteralPath (Join-Path $recoveryResults 'recovery-result.txt')
    }
    finally { $recoveryProcess.Dispose() }

}
finally {
    # Also handle a manager crash before its own finally can finish. Only the fresh test output is eligible.
    $fixtureScope = [IO.Path]::GetFullPath((Join-Path $results 'servers')) + [IO.Path]::DirectorySeparatorChar
    if (-not $fixtureScope.StartsWith($OutputDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid test cleanup scope.' }
    foreach ($child in [Diagnostics.Process]::GetProcessesByName('AssettoServer')) {
        try {
            if ($child.MainModule.FileName.StartsWith($fixtureScope, [StringComparison]::OrdinalIgnoreCase)) { $child.Kill(); $child.WaitForExit() }
        }
        catch [InvalidOperationException] { }
        catch [ComponentModel.Win32Exception] { }
        finally { $child.Dispose() }
    }
    $process.Dispose()
}
