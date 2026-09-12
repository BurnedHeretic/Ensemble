$ErrorActionPreference = "Stop"

function Backup-File([string]$Path) {
    if (Test-Path $Path) {
        $backup = "$Path.v15-backup"
        if (-not (Test-Path $backup)) {
            Copy-Item $Path $backup
            Write-Host "Backup created: $backup"
        }
    }
}

# ------------------------------------------------------------
# MainWindow.xaml.cs
# Remove statements that sit after an unconditional return null;
# in TryLoadTerrainHeightMap().
# ------------------------------------------------------------
$main = Join-Path $PSScriptRoot "MainWindow.xaml.cs"

if (Test-Path $main) {
    Backup-File $main

    $text = Get-Content -Raw -Path $main

    $pattern = '(?ms)(return\s+null\s*;)\s*_currentTerrainChunk\s*=\s*null\s*;\s*_currentTerrainOriginalXtdData\s*=\s*null\s*;'
    $updated = [regex]::Replace($text, $pattern, '$1')

    if ($updated -ne $text) {
        Set-Content -Path $main -Value $updated -Encoding UTF8
        Write-Host "Cleaned unreachable terrain-reset statements in MainWindow.xaml.cs"
    }
    else {
        Write-Host "MainWindow.xaml.cs: no matching unreachable blocks found."
    }
}
else {
    Write-Host "MainWindow.xaml.cs not found; skipped."
}

# ------------------------------------------------------------
# Services\EcfFileService.cs
# byte[].Length is Int32, so it can never exceed UInt32.MaxValue.
# Keep the earlier MemoryStream.Position > uint.MaxValue guard.
# ------------------------------------------------------------
$ecf = Join-Path $PSScriptRoot "Services\EcfFileService.cs"

if (Test-Path $ecf) {
    Backup-File $ecf

    $text = Get-Content -Raw -Path $ecf

    $pattern = '(?ms)\s*if\s*\(\s*result\.Length\s*>\s*uint\.MaxValue\s*\)\s*\{\s*throw\s+new\s+InvalidDataException\s*\(\s*"ECF file exceeded 4 GB\."\s*\)\s*;\s*\}'
    $updated = [regex]::Replace($text, $pattern, '')

    if ($updated -ne $text) {
        Set-Content -Path $ecf -Value $updated -Encoding UTF8
        Write-Host "Removed impossible result.Length > uint.MaxValue comparison in EcfFileService.cs"
    }
    else {
        Write-Host "EcfFileService.cs: no matching impossible comparison found."
    }
}
else {
    Write-Host "Services\EcfFileService.cs not found; skipped."
}

Write-Host ""
Write-Host "v15 warning cleanup complete."
