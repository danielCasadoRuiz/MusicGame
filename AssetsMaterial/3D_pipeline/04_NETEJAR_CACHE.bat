@echo off
setlocal
chcp 65001 >nul
echo.
echo === NETEJA DE CACHE DEL PIPELINE ===
echo Tanca les tasques del pipeline i Blender abans de continuar.
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0_System\clear_pipeline_cache.ps1" %*
set "cleanupExit=%errorlevel%"
echo.
pause
exit /b %cleanupExit%
