@echo off
rem Launches a local multiplayer test: a host plus clients, each on its own test profile and log.
rem Usage: launch-test.bat [players] [extra game args]
rem   launch-test.bat              host + 2 clients, enemies with 1 HP
rem   launch-test.bat 4            host + 3 clients
rem   launch-test.bat 3 --seed X   extra arguments are passed to every copy
rem Set STS2_DIR to use a game install other than the default Steam location.

setlocal EnableDelayedExpansion
if "%STS2_DIR%"=="" set "STS2_DIR=C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2"
set "PLAYERS=%~1"
if "%PLAYERS%"=="" set "PLAYERS=3"
set "EXTRA="
shift
:collect
if not "%~1"=="" (
  set "EXTRA=!EXTRA! %1"
  shift
  goto collect
)
set "LOGS=%APPDATA%\SlayTheSpire2\logs"
set "COMMON=--force-steam off --hab-enemies-1hp%EXTRA%"

cd /d "%STS2_DIR%" || (echo Game not found at "%STS2_DIR%" & exit /b 1)

echo Starting host...
start "" SlayTheSpire2.exe %COMMON% --fastmp host_standard --log-file "%LOGS%\test_host.log"
rem Give the host time to start listening before anyone joins.
timeout /t 8 /nobreak >nul

set /a LAST=%PLAYERS%-1
for /l %%i in (1,1,%LAST%) do (
  echo Starting client %%i000...
  start "" SlayTheSpire2.exe %COMMON% --fastmp join --clientId %%i000 --log-file "%LOGS%\test_client%%i000.log"
  rem Copies starting at the same moment can trip over each other's startup.
  timeout /t 5 /nobreak >nul
)
echo Started %PLAYERS% copies. Logs: %LOGS%\test_*.log
