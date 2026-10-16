@echo off
rem =====================================================================
rem Roll back from the shift service to the current (non-shift) Central
rem Operator service. Run as Administrator. No installer or database change -
rem the non-shift service ignores the shift tables. Operators may need to
rem re-scan their cards.
rem
rem Set LEGACY to the same service name as in Switch_To_Shift.cmd.
rem =====================================================================
setlocal
set LEGACY=Disney_Central_Operator
set SHIFT=Cockpit_Central_Operator_Shift

net session >nul 2>&1
if errorlevel 1 (echo Run this script as Administrator. & exit /b 1)
sc query %LEGACY% >nul 2>&1
if errorlevel 1 (echo Service %LEGACY% not found - set LEGACY at the top of this script. & exit /b 1)

sc query %SHIFT% >nul 2>&1
if errorlevel 1 goto :startlegacy
echo Stopping %SHIFT% ...
sc stop %SHIFT% >nul 2>&1
call :waitstate %SHIFT% STOPPED
if errorlevel 1 (echo %SHIFT% did not stop - stop it from services.msc, then run this again. & exit /b 1)
sc config %SHIFT% start= disabled >nul

:startlegacy
echo Starting %LEGACY% ...
sc config %LEGACY% start= auto >nul
sc start %LEGACY% >nul 2>&1
call :waitstate %LEGACY% RUNNING
if errorlevel 1 (echo WARNING: %LEGACY% did not start - check the Application event log. & exit /b 2)

echo.
echo Rolled back: %LEGACY% is RUNNING, %SHIFT% is STOPPED and disabled.
exit /b 0

rem Wait up to 2 minutes for service %1 to reach state %2 (a stop can take a while with many readers)
:waitstate
for /L %%i in (1,1,120) do (
    sc query %1 | find "%2" >nul && exit /b 0
    timeout /t 1 /nobreak >nul
)
exit /b 1
