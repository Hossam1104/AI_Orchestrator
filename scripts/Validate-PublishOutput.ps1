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

$expectedMachines = @{
    'win-x86' = 0x014C
    'win-x64' = 0x8664
    'win-arm64' = 0xAA64
}

$selfContainedMarkers = @(
    'System.Private.CoreLib.dll'
    'wpfgfx_cor3.dll'
    'PresentationFramework'
)

function Get-PeMetadata {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo] $File
    )

    $stream = $null
    $reader = $null
    try {
        $stream = [System.IO.File]::OpenRead($File.FullName)
        if ($stream.Length -lt 64) {
            throw "executable is truncated before the DOS header"
        }

        $reader = [System.IO.BinaryReader]::new($stream)
        $dosMagic = $reader.ReadUInt16()
        if ($dosMagic -ne 0x5A4D) {
            throw "invalid DOS header: expected MZ"
        }

        $stream.Position = 0x3C
        $peOffset = $reader.ReadInt32()
        if ($peOffset -lt 0 -or [long]$peOffset -gt ($stream.Length - 6)) {
            throw "invalid PE header offset: 0x{0:X8}" -f $peOffset
        }

        $stream.Position = $peOffset
        $signature = $reader.ReadBytes(4)
        if ($signature.Length -ne 4 -or
            $signature[0] -ne 0x50 -or
            $signature[1] -ne 0x45 -or
            $signature[2] -ne 0x00 -or
            $signature[3] -ne 0x00) {
            throw "missing PE signature at offset 0x{0:X8}: expected PE\0\0" -f $peOffset
        }

        [pscustomobject]@{
            Offset = $peOffset
            Machine = $reader.ReadUInt16()
        }
    }
    finally {
        if ($null -ne $reader) {
            $reader.Dispose()
        }
        elseif ($null -ne $stream) {
            $stream.Dispose()
        }
    }
}

function Find-AsciiMarkers {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string[]] $Markers
    )

    $maxMarkerLength = ($Markers | Measure-Object -Property Length -Maximum).Maximum
    $found = [ordered]@{}
    foreach ($marker in $Markers) {
        $found[$marker] = $false
    }

    $reader = $null
    try {
        # ASCII decoding preserves the executable's ASCII bundle names while allowing a bounded,
        # chunked scan without loading the entire single-file executable into memory.
        $reader = [System.IO.StreamReader]::new(
            $Path,
            [System.Text.Encoding]::ASCII,
            $false,
            1MB
        )
        $buffer = New-Object char[] 1MB
        $carry = ''

        while (($read = $reader.ReadBlock($buffer, 0, $buffer.Length)) -gt 0) {
            $text = $carry + [string]::new($buffer, 0, $read)
            foreach ($marker in $Markers) {
                if (-not $found[$marker] -and
                    $text.IndexOf($marker, [System.StringComparison]::Ordinal) -ge 0) {
                    $found[$marker] = $true
                }
            }

            $carryLength = $maxMarkerLength - 1
            if ($carryLength -gt 0) {
                $carry = if ($text.Length -gt $carryLength) {
                    $text.Substring($text.Length - $carryLength)
                }
                else {
                    $text
                }
            }

            if (($found.Values | Where-Object { -not $_ }).Count -eq 0) {
                break
            }
        }
    }
    finally {
        if ($null -ne $reader) {
            $reader.Dispose()
        }
    }

    return [pscustomobject]@{
        Found = $found
        Missing = @($found.GetEnumerator() | Where-Object { -not $_.Value } | ForEach-Object Key)
    }
}

try {
    if (-not $expectedMachines.ContainsKey($RuntimeIdentifier)) {
        throw "unsupported RuntimeIdentifier: $RuntimeIdentifier"
    }

    if (-not (Test-Path -LiteralPath $PublishDirectory -PathType Container)) {
        throw "publish output directory does not exist: $PublishDirectory"
    }

    $directory = Get-Item -LiteralPath $PublishDirectory
    $files = @(Get-ChildItem -LiteralPath $directory.FullName -File -Recurse)

    if ($files.Count -eq 0) {
        throw "publish output directory is empty: $($directory.FullName)"
    }

    [long]$totalBytes = ($files | Measure-Object -Property Length -Sum).Sum
    if ($totalBytes -le 0) {
        throw "publish output has no non-empty files: $($directory.FullName)"
    }

    $executable = Join-Path $directory.FullName $ExpectedExecutable
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "expected executable is missing: $ExpectedExecutable"
    }

    $executableFile = Get-Item -LiteralPath $executable
    if ($executableFile.Length -le 0) {
        throw "expected executable is empty: $ExpectedExecutable"
    }

    $unexpectedLooseRuntimeFiles = @(
        $files | Where-Object {
            $_.Name -match '(?i)(\.dll$|\.deps\.json$|\.runtimeconfig\.json$|\.config$|^appsettings.*\.json$)'
        }
    )

    if ($unexpectedLooseRuntimeFiles.Count -gt 0) {
        $names = $unexpectedLooseRuntimeFiles.FullName -join [Environment]::NewLine
        throw "single-file publish contains unexpected loose runtime/configuration files:$([Environment]::NewLine)$names"
    }

    $pe = Get-PeMetadata -File $executableFile
    $expectedMachine = $expectedMachines[$RuntimeIdentifier]
    if ($pe.Machine -ne $expectedMachine) {
        throw "wrong executable architecture for $RuntimeIdentifier`: detected PE Machine 0x{0:X4}, expected 0x{1:X4}" -f $pe.Machine, $expectedMachine
    }

    $markerResult = Find-AsciiMarkers -Path $executableFile.FullName -Markers $selfContainedMarkers
    if ($markerResult.Missing.Count -gt 0) {
        throw "self-contained runtime evidence is missing from the executable: $($markerResult.Missing -join ', ')"
    }

    $machineHex = '0x{0:X4}' -f $pe.Machine
    Write-Host "Validated $RuntimeIdentifier publish output."
    Write-Host "RID: $RuntimeIdentifier"
    Write-Host "Executable: $ExpectedExecutable"
    Write-Host "PE signature: present"
    Write-Host "Detected PE Machine: $machineHex"
    Write-Host "Architecture match: PASS"
    Write-Host "Self-contained runtime evidence: PASS ($($selfContainedMarkers -join ', '))"
    Write-Host "Files: $($files.Count)"
    Write-Host "Bytes: $totalBytes"
}
catch {
    Write-Error "Publish validation failed for $RuntimeIdentifier`: $($_.Exception.Message)"
    exit 1
}
