@echo off
setlocal
set "EXE=%~dp0K400Fn.exe"

if not exist "%EXE%" (
    echo K400Fn.exe was not found next to this script.
    pause
    exit /b 1
)

schtasks /create /tn "K400 Fn Switch" /tr "\"%EXE%\" auto --quiet" /sc minute /mo 15 /f
if errorlevel 1 (
    echo Could not create the scheduled task.
) else (
    echo Installed. F-keys and touchpad scrolling will be re-applied every 15 minutes
    echo while you are logged on.
    echo Remove it with "Remove auto F-keys.cmd".
)
pause
