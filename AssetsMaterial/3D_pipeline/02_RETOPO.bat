@echo off
setlocal
chcp 65001 >nul
pushd "%~dp0"

attrib +h "_System" >nul 2>nul

where py >nul 2>nul
if errorlevel 1 goto try_python
py -3 "%~dp0_System\retopo_worker.py"
goto done

:try_python
where python >nul 2>nul
if errorlevel 1 goto missing_python
python "%~dp0_System\retopo_worker.py"
goto done

:missing_python
echo.
echo ERROR: no trobo Python 3.

:done
echo.
popd
pause
