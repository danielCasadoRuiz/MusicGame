$root = "C:\Users\VISYON\Documents\MusicGame"
$dst  = "D:\MusicGame_Backup"
$log  = "$dst\_logs"
New-Item -ItemType Directory -Force $log | Out-Null
$items = @(
  "MusicGame\Assets\_Project\Art\Arenas",
  "MusicGame\Assets\_Project\Art\Opponents",
  "MusicGame\Assets\_Project\Art\Player",
  "AssetsMaterial\3D_pipeline",
  "AssetsMaterial\AudioPipeline"
)
foreach ($i in $items) {
  $name = ($i -replace '[\\]', '_')
  Write-Output "=== $i  $(Get-Date -Format HH:mm:ss)"
  robocopy "$root\$i" "$dst\$i" /E /COPY:DAT /DCOPY:T /R:1 /W:1 /MT:8 /NP /NFL /NDL /XD __pycache__ /LOG:"$log\$name.log" | Out-Null
  $code = $LASTEXITCODE
  $summary = Get-Content "$log\$name.log" | Select-String -Pattern '^\s*(Dirs|Files|Bytes)\s*:' | ForEach-Object { $_.Line.Trim() }
  Write-Output "exit $code (0-7 = OK, 8+ = errors)"
  $summary | ForEach-Object { Write-Output "  $_" }
}
Write-Output "=== DONE $(Get-Date -Format HH:mm:ss)"
