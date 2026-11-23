@echo off
setlocal
set "UNO_MODEL=%~dp0yolo\uno-recognition-2026-11-20\models\best_hand_candidate_20261121.pt"
if not exist "%UNO_MODEL%" (
    echo Trained candidate model not found: %UNO_MODEL%
    pause
    exit /b 1
)
rem Four-scale inference is the default trial: better recall without rotated views.
set "UNO_SCALES=256,416,512,640"
set "UNO_CONF=0.25"
set "UNO_DEDUPE=1"
set "UNO_HAND_ENHANCE=0"
rem Optional: run_trained_hand_test.bat enhanced to test slower rotated views.
if /I "%~1"=="enhanced" set "UNO_HAND_ENHANCE=1"
set "UNO_CAPTURE_DIR=%~dp0.debug\recognition_frames_candidate"
set "UNO_CAPTURE_MAX=150"
call "%~dp0run_ai_server.bat"
exit /b %ERRORLEVEL%
