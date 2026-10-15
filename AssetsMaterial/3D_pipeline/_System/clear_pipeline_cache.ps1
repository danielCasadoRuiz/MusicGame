param([switch]$Preview, [switch]$ConfirmCleanup)
$ErrorActionPreference = 'Stop'
$pipelineRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')

try {
    # Never stop processes: a running job must finish or be closed by the user.
    $active = @(Get-CimInstance Win32_Process | Where-Object {
        $_.ProcessId -ne $PID -and (
            $_.Name -match '^(blender|xremesh)\.exe$' -or
            ($_.Name -match '^(python.*|py|node|claude)\.exe$' -and
             $_.CommandLine -and $_.CommandLine.IndexOf($pipelineRoot, [StringComparison]::OrdinalIgnoreCase) -ge 0)
        )
    })
    if ($active.Count) {
        Write-Host 'No es neteja: hi ha processos del pipeline o Blender oberts.'
        $active | Select-Object Name, ProcessId | Format-Table
        exit 2
    }

    # Explicit allowlist. Paid checkpoints, source files and final outputs are excluded.
    $relativeTargets = @('_W', '_System\Temp', '_System\FittingAndRigging\_work',
                         'Debug', '.playwright-mcp')
    $systemDirectory = Join-Path $pipelineRoot '_System'
    if (Test-Path -LiteralPath $systemDirectory) {
        $relativeTargets += @(Get-ChildItem -LiteralPath $systemDirectory -Directory -Recurse -Force |
            Where-Object { $_.Name -eq '__pycache__' } |
            ForEach-Object { $_.FullName.Substring($pipelineRoot.Length + 1) })
    }
    $targets = @()
    foreach ($relativeTarget in $relativeTargets) {
        $absoluteTarget = [IO.Path]::GetFullPath((Join-Path $pipelineRoot $relativeTarget))
        if (-not $absoluteTarget.StartsWith($pipelineRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Ruta fora del pipeline: $absoluteTarget"
        }
        if (-not (Test-Path -LiteralPath $absoluteTarget)) { continue }
        # Check every ancestor and descendant before any recursive deletion.
        $ancestor = Get-Item -LiteralPath $absoluteTarget -Force
        while ($ancestor.FullName -ne $pipelineRoot) {
            if ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "No es netegen enllacos o junctions: $($ancestor.FullName)"
            }
            $ancestor = Get-Item -LiteralPath (Split-Path -Parent $ancestor.FullName) -Force
        }
        $entries = @(Get-ChildItem -LiteralPath $absoluteTarget -Recurse -Force)
        if (@($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) {
            throw "La carpeta conte enllacos o junctions: $absoluteTarget"
        }
        $bytes = ($entries | Where-Object { -not $_.PSIsContainer } | Measure-Object Length -Sum).Sum
        $targets += [PSCustomObject]@{ Path = $absoluteTarget; Folder = $relativeTarget; MB = [math]::Round($bytes / 1MB, 2) }
    }
    if (-not $targets.Count) {
        Write-Host 'No hi ha cache per netejar.'
        exit 0
    }
    $targets | Select-Object Folder, MB | Format-Table -AutoSize
    Write-Host 'Es conserven Input, Upscaled, Upscaled_and_Cut, Manual_Leonardo, Raw3D, Output, fitting desat, configuracio i logs centralitzats.'
    Write-Host 'Els fittings manuals encara no confirmats dins _work es perdran.'
    if ($Preview) { exit 0 }
    if (-not $ConfirmCleanup) {
        $answer = Read-Host 'Eliminar aquestes carpetes temporals? Escriu SI'
        if ($answer -cne 'SI') { Write-Host 'Neteja cancel.lada.'; exit 0 }
    }
    foreach ($target in $targets) {
        if (Test-Path -LiteralPath $target.Path) {
            Remove-Item -LiteralPath $target.Path -Recurse -Force
        }
    }
    Write-Host 'Cache eliminada. Els logs centralitzats es conserven.'
    exit 0
} catch {
    Write-Host "ERROR: $($_.Exception.Message)"
    exit 1
}
