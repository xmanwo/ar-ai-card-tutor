@echo off
setlocal
rem Recognition-only demo: no DeepSeek key and no /tutor router.
rem Scanning, card review and the local rules panel all work without AI.
set "UNO_MODEL=%~dp0yolo\uno-recognition-2026-11-20\models\best_hand_candidate_20261121.pt"
if not exist "%UNO_MODEL%" (
    echo Trained candidate model not found: %UNO_MODEL%
    pause
    exit /b 1
)
set "UNO_SCALES=256,416,512,640"
set "UNO_CONF=0.25"
set "UNO_DEDUPE=1"
set "UNO_HAND_ENHANCE=0"
call "%~dp0yolo\uno-recognition-2026-11-20\run_server.bat"
exit /b %ERRORLEVEL%
