@echo off
setlocal
set "PROJECT=C:\UnrealProjects\Rein_Project\Rein_Project.uproject"
set "ENGINE=C:\Program Files\Epic Games\UE_5.8"
if not exist "%PROJECT%" (
    echo Project alias not found: %PROJECT%
    pause
    exit /b 1
)
call "%ENGINE%\Engine\Build\BatchFiles\Build.bat" Rein_ProjectEditor Win64 Development "-Project=%PROJECT%" -WaitMutex -NoHotReloadFromIDE
set "BUILD_RESULT=%ERRORLEVEL%"
pause
exit /b %BUILD_RESULT%
