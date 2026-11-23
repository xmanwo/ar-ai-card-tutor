@echo off
setlocal
cd /d "%~dp0"

if not exist ".venv\Scripts\python.exe" (
  echo [ERROR] No virtual environment found. Run setup_windows.bat first.
  pause
  exit /b 1
)

set "UNO_YOLO_DIR=%~dp0"
if not defined UNO_MODEL set "UNO_MODEL=%~dp0models\best.pt"

echo Starting the recognition service on port 8000.
echo   model : %UNO_MODEL%
if defined UNO_SCALES echo   scales: %UNO_SCALES%
echo   headset reaches it over USB (adb reverse) or the local network.
echo Keep this window open. Press Ctrl+C to stop.
echo.

".venv\Scripts\python.exe" -m uvicorn server:app --host 0.0.0.0 --port 8000
pause
