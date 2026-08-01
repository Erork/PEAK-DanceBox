@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-and-install.ps1" %*
if errorlevel 1 (
  echo.
  echo Build or installation failed. Read the error above.
  pause
  exit /b 1
)
echo.
echo Build and installation completed.
pause
