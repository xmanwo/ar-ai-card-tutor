@echo off
setlocal
rem An explicit ADB_EXE takes priority, then SDK environment variables, PATH and Unity defaults.
if not defined ADB_EXE if defined ANDROID_SDK_ROOT if exist "%ANDROID_SDK_ROOT%\platform-tools\adb.exe" set "ADB_EXE=%ANDROID_SDK_ROOT%\platform-tools\adb.exe"
if not defined ADB_EXE if defined ANDROID_HOME if exist "%ANDROID_HOME%\platform-tools\adb.exe" set "ADB_EXE=%ANDROID_HOME%\platform-tools\adb.exe"
if not defined ADB_EXE for /f "delims=" %%A in ('where adb 2^>nul') do if not defined ADB_EXE set "ADB_EXE=%%A"
for %%A in ("F:\UNITY\Hub\Editor\6000.3.25f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" "%ProgramFiles%\Unity\Hub\Editor\6000.3.25f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe") do if not defined ADB_EXE if exist "%%~A" set "ADB_EXE=%%~A"
if not defined ADB_EXE (
    echo ADB not found. Set ADB_EXE to your Unity Android SDK platform-tools\adb.exe.
    pause
    exit /b 1
)
if not exist "%ADB_EXE%" (
    echo ADB_EXE does not point to an existing adb.exe. Check the Unity Android SDK installation.
    pause
    exit /b 1
)
"%ADB_EXE%" devices
"%ADB_EXE%" -d reverse tcp:8000 tcp:8000
if errorlevel 1 (
    echo.
    echo Connect the Quest by USB and allow USB debugging inside the headset.
    echo Then run this file again. Only one USB Android device should be connected.
    pause
    exit /b 1
)
echo.
echo USB forwarding ready: Quest port 8000 to PC port 8000.
pause
