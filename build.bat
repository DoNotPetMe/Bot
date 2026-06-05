@echo off
REM ============================================================================
REM  REPOBot one-click build for the Steam version of R.E.P.O. (Windows)
REM
REM  Just double-click this file. It will:
REM    1. find your Steam R.E.P.O. install automatically
REM    2. install the .NET SDK if you don't have it
REM    3. build the mod
REM    4. copy REPOBot.dll into BepInEx\plugins for you
REM
REM  (It just launches build.ps1, which does the real work.)
REM ============================================================================
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
echo.
pause
