@echo off
setlocal
chcp 65001 >nul
set "PYTHON_EXE=%~dp0yolo\uno-recognition-2026-11-20\.venv\Scripts\python.exe"
if not exist "%PYTHON_EXE%" (
    echo Python environment not found. Please check the recognition service setup.
    pause
    exit /b 1
)
"%PYTHON_EXE%" -X utf8 "%~dp0tools\check_deepseek.py"
set "CHECK_RESULT=%ERRORLEVEL%"
echo.
pause
exit /b %CHECK_RESULT%
