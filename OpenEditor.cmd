@echo off
setlocal
set "PROJECT=C:\UnrealProjects\Rein_Project\Rein_Project.uproject"
set "ENGINE=C:\Program Files\Epic Games\UE_5.8"
if not exist "%PROJECT%" (
    echo Project alias not found: %PROJECT%
    pause
    exit /b 1
)
start "" "%ENGINE%\Engine\Binaries\Win64\UnrealEditor.exe" "%PROJECT%"
