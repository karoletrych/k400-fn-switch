@echo off
schtasks /delete /tn "K400 Fn Switch" /f
if errorlevel 1 (
    echo The scheduled task was not installed.
) else (
    echo Removed the "K400 Fn Switch" scheduled task.
)
pause
