@echo off
node "%~dp0import-suno.cjs"
set "result=%errorlevel%"
if not "%result%"=="0" pause
exit /b %result%
