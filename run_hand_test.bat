@echo off
setlocal
set "UNO_HAND_ENHANCE=1"
set "UNO_CAPTURE_DIR=%~dp0.debug\recognition_frames"
set "UNO_CAPTURE_MAX=150"
call "%~dp0run_ai_server.bat"
