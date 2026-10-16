@echo off
rem =====================================================================
rem Switch card-reader logins from the current (non-shift) Central Operator
rem service to the shift service. Run as Administrator.
rem
rem Set LEGACY to the current service name recorded in the runbook baseline
rem step (sc query state= all ^| findstr /i central_operator).
rem The two services must never run at the same time - this script only
rem starts the shift service once the current one has stopped.
rem Undo with Rollback_To_Legacy.cmd.
rem =====================================================================
setlocal
set LEGACY=Disney_Central_Operator
set SHIFT=Cockpit_Central_Operator_Shift

net session >nul 2>&1
if errorlevel 1 (echo Run this script as Administrator. & exit /b 1)
sc query %LEGACY% >nul 2>&1
if errorlevel 1 (echo Service %LEGACY% not found - set LEGACY at the top of this script. & exit /b 1)
sc query %SHIFT% >nul 2>&1
if errorlevel 1 (echo Service %SHIFT% not found - install CentralLoginServiceSetup_Shift.msi first. & exit /b 1)

echo Stopping %LEGACY% ...
sc stop %LEGACY% >nul 2>&1
call :waitstate %LEGACY% STOPPED
if errorlevel 1 (echo %LEGACY% did not stop - nothing else changed. & exit /b 1)
sc config %LEGACY% start= disabled >nul

echo Starting %SHIFT% ...
sc config %SHIFT% start= auto >nul
sc start %SHIFT% >nul 2>&1
call :waitstate %SHIFT% RUNNING
if errorlevel 1 goto :failed

echo.
echo Switched: %SHIFT% is RUNNING, %LEGACY% is STOPPED and disabled.
exit /b 0

:failed
echo.
echo %SHIFT% did not start - switching back to %LEGACY%.
echo Check the Application event log for %SHIFT% errors (e.g. the connection string).
sc stop %SHIFT% >nul 2>&1
call :waitstate %SHIFT% STOPPED
sc config %SHIFT% start= disabled >nul
sc config %LEGACY% start= auto >nul
sc start %LEGACY% >nul 2>&1
call :waitstate %LEGACY% RUNNING
if errorlevel 1 (echo WARNING: %LEGACY% did not restart - start it from services.msc. & exit /b 2)
echo %LEGACY% is running again.
exit /b 1

rem Wait up to 2 minutes for service %1 to reach state %2 (a stop can take a while with many readers)
:waitstate
for /L %%i in (1,1,120) do (
    sc query %1 | find "%2" >nul && exit /b 0
    timeout /t 1 /nobreak >nul
)
exit /b 1
