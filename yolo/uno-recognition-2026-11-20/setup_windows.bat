@echo off
setlocal
cd /d "%~dp0"

echo === UNO recognition service - one-time setup ===
echo.

where python >nul 2>nul
if errorlevel 1 (
  echo [ERROR] Python was not found on PATH.
  echo Install Python 3.11-3.13 from python.org and tick "Add python.exe to PATH".
  pause
  exit /b 1
)

python -c "import sys; sys.exit(0 if sys.version_info >= (3,11) else 1)"
if errorlevel 1 (
  echo [ERROR] Python 3.11 or newer is required. Current version:
  python -V
  pause
  exit /b 1
)

if not exist ".venv\Scripts\python.exe" (
  echo Creating a virtual environment in .venv ...
  python -m venv .venv
  if errorlevel 1 (
    echo [ERROR] venv creation failed.
    pause
    exit /b 1
  )
)

echo Installing dependencies. The first run downloads about 2 GB of PyTorch. Be patient.
".venv\Scripts\python.exe" -m pip install --upgrade pip
".venv\Scripts\python.exe" -m pip install -r requirements.txt
if errorlevel 1 (
  echo [ERROR] pip install failed. Scroll up for the first error line.
  pause
  exit /b 1
)

echo.
echo Setup finished. Next: run_server.bat
pause
