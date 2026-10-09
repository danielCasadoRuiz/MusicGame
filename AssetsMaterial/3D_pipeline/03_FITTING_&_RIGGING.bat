@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"

rem Normalitza ROOT sense barra final; evita que \ abans de la cometa es converteixi en una cometa literal
for %%I in ("%~dp0.") do set "ROOT=%%~fI"

set "PYTHON=C:\Python313\python.exe"
if not exist "%PYTHON%" set "PYTHON=python"

if "%~1"=="" (
  "%PYTHON%" "%ROOT%\_System\FittingAndRigging\controller.py" --root "%ROOT%"
) else (
  "%PYTHON%" "%ROOT%\_System\FittingAndRigging\controller.py" --root "%ROOT%" --asset "%~1"
)
set "ERR=%ERRORLEVEL%"

echo.
if not "%ERR%"=="0" (
  echo FittingAndRigging ha acabat amb error %ERR%.
) else (
  echo FittingAndRigging finalitzat.
)
pause
exit /b %ERR%
