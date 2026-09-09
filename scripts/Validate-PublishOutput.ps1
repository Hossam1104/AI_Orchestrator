[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $PublishDirectory,

    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x86', 'win-x64', 'win-arm64')]
    [string] $RuntimeIdentifier,

    [string] $ExpectedExecutable = 'AIUsageMonitor.Desktop.exe'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PublishDirectory -PathType Container)) {
    throw "Publish output directory does not exist for $RuntimeIdentifier`: $PublishDirectory"
}

$directory = Get-Item -LiteralPath $PublishDirectory
$files = @(Get-ChildItem -LiteralPath $directory.FullName -File -Recurse)

if ($files.Count -eq 0) {
    throw "Publish output directory is empty for $RuntimeIdentifier`: $($directory.FullName)"
}

$totalBytes = ($files | Measure-Object -Property Length -Sum).Sum
if ($totalBytes -le 0) {
    throw "Publish output has no non-empty files for $RuntimeIdentifier`: $($directory.FullName)"
}

$executable = Join-Path $directory.FullName $ExpectedExecutable
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Expected executable is missing for $RuntimeIdentifier`: $ExpectedExecutable"
}

if ((Get-Item -LiteralPath $executable).Length -le 0) {
    throw "Expected executable is empty for $RuntimeIdentifier`: $ExpectedExecutable"
}

$unexpectedLooseRuntimeFiles = @(
    $files | Where-Object {
        $_.Name -match '(?i)(\.dll$|\.deps\.json$|\.runtimeconfig\.json$|\.config$|^appsettings.*\.json$)'
    }
)

if ($unexpectedLooseRuntimeFiles.Count -gt 0) {
    $names = $unexpectedLooseRuntimeFiles.FullName -join [Environment]::NewLine
    throw "Single-file publish contains unexpected loose runtime/configuration files for $RuntimeIdentifier`:$([Environment]::NewLine)$names"
}

Write-Host "Validated $RuntimeIdentifier publish output."
Write-Host "Directory: $($directory.FullName)"
Write-Host "Files: $($files.Count)"
Write-Host "Bytes: $totalBytes"
Write-Host "Executable: $ExpectedExecutable"
