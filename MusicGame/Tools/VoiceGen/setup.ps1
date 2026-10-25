# MusicGame voice generator — one-time setup (PRODUCTION ONLY, nothing here ships with the game).
# Creates .venv (Python 3.12), installs PyTorch (CUDA 12.8) + qwen-tts, then downloads ONLY the three models
# the pipeline uses (~9.7 GB): 1.7B VoiceDesign, 1.7B Base (clone) and the 12 Hz tokenizer.
# Usage (PowerShell, from this folder):   ./setup.ps1            # everything
#                                         ./setup.ps1 -SkipModels # packages only
param([switch]$SkipModels)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$free = (Get-PSDrive -Name (Get-Location).Drive.Name).Free / 1GB
Write-Host ("Free disk: {0:N1} GB (needs ~16 GB: models 9.7 + PyTorch/venv ~6)" -f $free)
if ($free -lt 16) { throw "Not enough free disk space." }

if (-not (Test-Path ".venv")) { py -3.12 -m venv .venv }
$py = ".\.venv\Scripts\python.exe"
& $py -m pip install --upgrade pip
& $py -m pip install torch torchaudio --index-url https://download.pytorch.org/whl/cu128
& $py -m pip install -r requirements.txt

if (-not $SkipModels) { & $py download_models.py }
& $py voicegen.py --check
