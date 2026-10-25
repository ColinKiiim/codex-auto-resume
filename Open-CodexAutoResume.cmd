@echo off
if exist "%~dp0release\CodexAutoResume.exe" (
    start "" "%~dp0release\CodexAutoResume.exe"
) else (
    start "" "%~dp0dist\CodexAutoResume.exe"
)
