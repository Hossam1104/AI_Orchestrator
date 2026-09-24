[CmdletBinding()]
param(
    [switch] $SmokeTest
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$projectPath = Join-Path $repoRoot 'src\AIUsageMonitor.Desktop\AIUsageMonitor.Desktop.csproj'
$validatorPath = Join-Path $repoRoot 'scripts\Validate-PublishOutput.ps1'
$runtimeIdentifier = 'win-x64'
$publishDirectory = Join-Path $repoRoot 'artifacts\local-run\win-x64'
$expectedExecutable = 'AIUsageMonitor.Desktop.exe'
$processName = [IO.Path]::GetFileNameWithoutExtension($expectedExecutable)

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Desktop project does not exist: $projectPath"
}

if (-not (Test-Path -LiteralPath $validatorPath -PathType Leaf)) {
    throw "Publish validator does not exist: $validatorPath"
}

$repoPrefix = ([IO.Path]::GetFullPath($repoRoot)).TrimEnd('\') + '\'
$publishFullPath = ([IO.Path]::GetFullPath($publishDirectory)).TrimEnd('\') + '\'
if (-not $publishFullPath.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    $publishFullPath -eq $repoPrefix) {
    throw "Refusing to recreate output outside the repository: $publishDirectory"
}

$branch = (& git -C $repoRoot branch --show-current).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($branch)) {
    throw 'Could not determine the current Git branch.'
}

$head = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($head)) {
    throw 'Could not determine the current Git HEAD.'
}

$statusLines = @(& git -C $repoRoot status --porcelain=v1)
if ($LASTEXITCODE -ne 0) {
    throw 'Could not determine the current Git worktree state.'
}

$worktree = if ($statusLines.Count -eq 0) { 'CLEAN' } else { 'DIRTY' }
$publishUtc = [DateTimeOffset]::UtcNow.ToString('o')

$existingProcesses = @(Get-Process -Name $processName -ErrorAction SilentlyContinue)
foreach ($existingProcess in $existingProcesses) {
    if (-not $existingProcess.HasExited) {
        Write-Host "STOPPING EXISTING APO PID = $($existingProcess.Id)"
        Stop-Process -Id $existingProcess.Id -Force
        $existingProcess.WaitForExit(10000)
    }
}

if (@(Get-Process -Name $processName -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'An existing APO process could not be stopped; refusing to create a duplicate.'
}

if ([IO.Directory]::Exists($publishDirectory)) {
    [IO.Directory]::Delete($publishDirectory, $true)
}
[IO.Directory]::CreateDirectory($publishDirectory) | Out-Null

& dotnet publish $projectPath `
    --configuration Release `
    --runtime $runtimeIdentifier `
    --self-contained true `
    --nologo `
    "-p:PublishProfile=$runtimeIdentifier" `
    --output $publishDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Fresh Release publish failed with exit code $LASTEXITCODE."
}

& $validatorPath -PublishDirectory $publishDirectory -RuntimeIdentifier $runtimeIdentifier
if ($LASTEXITCODE -ne 0) {
    throw "Publish validation failed with exit code $LASTEXITCODE."
}

$executablePath = [IO.Path]::GetFullPath((Join-Path $publishDirectory $expectedExecutable))
$publishPrefix = ([IO.Path]::GetFullPath($publishDirectory)).TrimEnd('\') + '\'
if (-not $executablePath.StartsWith($publishPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    -not [IO.File]::Exists($executablePath)) {
    throw "Fresh executable is missing from the recreated publish directory: $executablePath"
}

$hash = (Get-FileHash -LiteralPath $executablePath -Algorithm SHA256).Hash
if ([string]::IsNullOrWhiteSpace($hash)) {
    throw "Could not hash the fresh executable: $executablePath"
}

Write-Host "BRANCH = $branch"
Write-Host "HEAD = $head"
Write-Host "SHORT HEAD = $($head.Substring(0, 12))"
Write-Host "WORKTREE = $worktree"
Write-Host "PUBLISH UTC = $publishUtc"
Write-Host "EXE = $executablePath"
Write-Host "SHA256 = $hash"
Write-Host "FRESH OUTPUT = $publishDirectory"

$process = $null
try {
    $process = Start-Process -FilePath $executablePath -WorkingDirectory $publishDirectory -PassThru

    if (-not $SmokeTest) {
        Write-Host "PID = $($process.Id)"
        Write-Host 'RUN MODE = INTERACTIVE'
        Write-Host 'APPLICATION LEFT OPEN = YES'
        Write-Host 'LEFT RUNNING = YES'
        return
    }

    # ponytail: fixed 30-second startup bound; increase only with measured startup evidence.
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    $ready = $false
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $process.Refresh()
        if ($process.HasExited) {
            break
        }

        if ($process.MainWindowHandle -ne [IntPtr]::Zero -and $process.Responding) {
            $ready = $true
            break
        }

        Start-Sleep -Milliseconds 250
    }

    if (-not $ready) {
        throw 'Fresh executable did not present a responding main window within the smoke-test timeout.'
    }

    Write-Host "PID = $($process.Id)"
    Write-Host 'RUN MODE = SMOKE TEST'
    Write-Host 'SMOKE TEST = PASS'
}
finally {
    if ($SmokeTest -and $null -ne $process) {
        $process.Refresh()
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
            $null = $process.WaitForExit(10000)
        }

        $apoProcessCount = @(Get-Process -Name 'AIUsageMonitor.Desktop' -ErrorAction SilentlyContinue).Count
        Write-Host "APO PROCESS COUNT = $apoProcessCount"
    }
}
