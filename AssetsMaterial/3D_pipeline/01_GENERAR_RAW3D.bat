@echo off
setlocal
chcp 65001 >nul
pushd "%~dp0"

attrib +h "_System" >nul 2>nul

where claude >nul 2>nul
if errorlevel 1 (
    echo.
    echo ERROR: no trobo Claude Code al PATH.
    echo Comprova primer: claude --version
    echo.
    goto done
)

where py >nul 2>nul
if errorlevel 1 goto try_python

py -3 -c "import requests, PIL, numpy, cv2" >nul 2>nul
if errorlevel 1 (
    echo Installing Python dependencies...
    py -3 -m pip install -r "%~dp0_System\requirements.txt"
    if errorlevel 1 goto install_error
)

py -3 "%~dp0_System\producer.py"
goto done

:try_python
where python >nul 2>nul
if errorlevel 1 goto missing_python

python -c "import requests, PIL, numpy, cv2" >nul 2>nul
if errorlevel 1 (
    echo Installing Python dependencies...
    python -m pip install -r "%~dp0_System\requirements.txt"
    if errorlevel 1 goto install_error
)

python "%~dp0_System\producer.py"
goto done

:missing_python
echo.
echo ERROR: no trobo Python 3.
goto done

:install_error
echo.
echo ERROR: no s'han pogut instal.lar les dependencies.

:done
echo.
popd
pause
