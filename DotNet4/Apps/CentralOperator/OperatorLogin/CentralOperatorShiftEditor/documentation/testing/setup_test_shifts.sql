-- =============================================
-- Setup 10-Minute Shifts for Fast Testing
-- =============================================
-- !! DEV/TEST ONLY - DELETES ALL SHIFTS AND ASSIGNMENTS. NEVER RUN ON A CUSTOMER SYSTEM. !!
-- Stops before changing anything unless test machines 2123 and 2125 exist in JEGR_DB.
--
-- Creates ten-minute shifts from 06:00 to 22:00 with a 5-minute gap after each one
-- (06:00-06:10, 06:15-06:25, ...), so the no-shift (legacy) path is exercised every cycle.
-- All shifts have DeltaMinus=0, DeltaPlus=0 (no grace period - the Shift Editor no longer shows these)
-- Each shift has one break, 5 minutes after its start (everyone on the shift is logged out then)
-- Assigns machines 2123 and 2125 to all shifts
-- Test legacy behaviour on a reader attached to any other (unassigned) machine as well.
-- =============================================

USE JEGR_Utils;
GO

IF NOT EXISTS (SELECT 1 FROM JEGR_DB.dbo.tblMachines WHERE IdJensen = 2123)
   OR NOT EXISTS (SELECT 1 FROM JEGR_DB.dbo.tblMachines WHERE IdJensen = 2125)
BEGIN
    RAISERROR('Test machines 2123/2125 not found - this is not the test system. Script stopped, nothing changed.', 16, 1);
    SET NOEXEC ON;
END
GO

-- =============================================
-- Step 1: Delete all existing shift assignments
-- =============================================
PRINT 'Deleting all shift machine assignments...';
DELETE FROM tblShiftMachineAssignments;
GO

-- =============================================
-- Step 2: Delete all existing shifts (and their break times)
-- =============================================
PRINT 'Deleting all shifts and break times...';
DELETE FROM tblShiftBreaks;
DELETE FROM tblShifts;
GO

-- Reset identity seed
DBCC CHECKIDENT ('tblShifts', RESEED, 0);
GO

-- =============================================
-- Step 3: Create 10-minute shifts from 06:00 to 22:00
-- =============================================
PRINT 'Creating 10-minute shifts from 06:00 to 22:00...';

SET IDENTITY_INSERT tblShifts ON;

DECLARE @ShiftID INT = 1;
DECLARE @StartHour INT = 6;
DECLARE @EndHour INT = 22;
DECLARE @StartMinute INT = 0;
DECLARE @ShiftDuration INT = 10; -- 10 minutes
DECLARE @GapMinutes INT = 5;     -- no-shift gap after each shift (legacy logic applies)

DECLARE @CurrentTime TIME;
DECLARE @EndTime TIME;
DECLARE @ShiftName VARCHAR(50);

-- Generate shifts
WHILE @StartHour < @EndHour OR (@StartHour = @EndHour AND @StartMinute = 0)
BEGIN
    -- Calculate start and end times
    SET @CurrentTime = CONVERT(TIME,
        CAST(@StartHour AS VARCHAR(2)) + ':' +
        RIGHT('0' + CAST(@StartMinute AS VARCHAR(2)), 2) + ':00');

    -- Calculate end time (add 10 minutes)
    SET @EndTime = DATEADD(MINUTE, @ShiftDuration, @CurrentTime);

    -- Generate shift name
    SET @ShiftName = CONVERT(VARCHAR(5), @CurrentTime, 108) + '-' + CONVERT(VARCHAR(5), @EndTime, 108);

    -- Insert shift
    INSERT INTO tblShifts (ShiftID, ShiftName, StartTime, EndTime, DeltaMinus, DeltaPlus, IsActive, CreatedDate, ModifiedDate)
    VALUES (@ShiftID, @ShiftName, @CurrentTime, @EndTime, 0, 0, 1, GETDATE(), GETDATE());

    -- Move to next shift
    SET @ShiftID = @ShiftID + 1;
    SET @StartMinute = @StartMinute + @ShiftDuration + @GapMinutes;

    -- Handle hour rollover
    IF @StartMinute >= 60
    BEGIN
        SET @StartHour = @StartHour + 1;
        SET @StartMinute = 0;
    END
END

SET IDENTITY_INSERT tblShifts OFF;
GO

DECLARE @ShiftCount INT;
SELECT @ShiftCount = COUNT(*) FROM tblShifts;
PRINT 'Created ' + CAST(@ShiftCount AS VARCHAR(10)) + ' shifts successfully.';
GO

-- =============================================
-- Step 3b: One break per shift, 5 minutes after its start
-- =============================================
PRINT 'Adding a break 5 minutes into each shift...';
INSERT INTO tblShiftBreaks (ShiftID, BreakTime)
SELECT ShiftID, DATEADD(MINUTE, 5, StartTime) FROM tblShifts;
GO

-- =============================================
-- Step 4: Lookup machine RecNum values
-- =============================================
PRINT 'Looking up machine RecNum values...';

DECLARE @Machine2123RecNum INT;
DECLARE @Machine2125RecNum INT;

-- Get RecNum for IdJensen = 2123
SELECT @Machine2123RecNum = RecNum
FROM JEGR_DB.dbo.tblMachines
WHERE IdJensen = 2123;

-- Get RecNum for IdJensen = 2125
SELECT @Machine2125RecNum = RecNum
FROM JEGR_DB.dbo.tblMachines
WHERE IdJensen = 2125;

IF @Machine2123RecNum IS NULL
BEGIN
    PRINT 'ERROR: Could not find machine with IdJensen = 2123';
    RAISERROR('Machine 2123 not found', 16, 1);
END

IF @Machine2125RecNum IS NULL
BEGIN
    PRINT 'ERROR: Could not find machine with IdJensen = 2125';
    RAISERROR('Machine 2125 not found', 16, 1);
END

PRINT 'Machine 2123 RecNum: ' + CAST(@Machine2123RecNum AS VARCHAR(10));
PRINT 'Machine 2125 RecNum: ' + CAST(@Machine2125RecNum AS VARCHAR(10));

-- =============================================
-- Step 5: Assign machines to all shifts for all days
-- =============================================
PRINT 'Assigning machines to all shifts for all days of the week...';

-- Machine 2123 assignments for all days (0=Sunday through 6=Saturday)
INSERT INTO tblShiftMachineAssignments (ShiftID, MachineRecNum, DayOfWeek, IsActive, CreatedDate)
SELECT s.ShiftID, @Machine2123RecNum, d.DayOfWeek, 1, GETDATE()
FROM tblShifts s
CROSS JOIN (
    SELECT 0 AS DayOfWeek UNION ALL
    SELECT 1 UNION ALL
    SELECT 2 UNION ALL
    SELECT 3 UNION ALL
    SELECT 4 UNION ALL
    SELECT 5 UNION ALL
    SELECT 6
) d;

-- Machine 2125 assignments for all days
INSERT INTO tblShiftMachineAssignments (ShiftID, MachineRecNum, DayOfWeek, IsActive, CreatedDate)
SELECT s.ShiftID, @Machine2125RecNum, d.DayOfWeek, 1, GETDATE()
FROM tblShifts s
CROSS JOIN (
    SELECT 0 AS DayOfWeek UNION ALL
    SELECT 1 UNION ALL
    SELECT 2 UNION ALL
    SELECT 3 UNION ALL
    SELECT 4 UNION ALL
    SELECT 5 UNION ALL
    SELECT 6
) d;

GO

PRINT 'Machine assignments completed successfully.';
GO

-- =============================================
-- Step 6: Verify the setup
-- =============================================
PRINT '';
PRINT '=============================================';
PRINT 'Shift Setup Summary (10-Minute Shifts)';
PRINT '=============================================';

-- Count shifts
DECLARE @TotalShifts INT;
SELECT @TotalShifts = COUNT(*) FROM tblShifts;
PRINT 'Total Shifts Created: ' + CAST(@TotalShifts AS VARCHAR(10));

-- Count assignments
DECLARE @AssignmentCount INT;
SELECT @AssignmentCount = COUNT(*) FROM tblShiftMachineAssignments;
PRINT 'Total Machine Assignments: ' + CAST(@AssignmentCount AS VARCHAR(10));

PRINT '';
PRINT 'First 10 Shifts:';
SELECT TOP 10
    ShiftID,
    ShiftName,
    CONVERT(VARCHAR(8), StartTime, 108) AS StartTime,
    CONVERT(VARCHAR(8), EndTime, 108) AS EndTime,
    DeltaMinus,
    DeltaPlus,
    IsActive
FROM tblShifts
ORDER BY ShiftID;

PRINT '';
PRINT 'Last 10 Shifts:';
SELECT TOP 10
    ShiftID,
    ShiftName,
    CONVERT(VARCHAR(8), StartTime, 108) AS StartTime,
    CONVERT(VARCHAR(8), EndTime, 108) AS EndTime,
    DeltaMinus,
    DeltaPlus,
    IsActive
FROM tblShifts
ORDER BY ShiftID DESC;

PRINT '';
PRINT 'Sample Machine Assignments (first 20):';
SELECT TOP 20
    sma.AssignmentID,
    s.ShiftName,
    m.IdJensen AS MachineID,
    CASE sma.DayOfWeek
        WHEN 0 THEN 'Sunday'
        WHEN 1 THEN 'Monday'
        WHEN 2 THEN 'Tuesday'
        WHEN 3 THEN 'Wednesday'
        WHEN 4 THEN 'Thursday'
        WHEN 5 THEN 'Friday'
        WHEN 6 THEN 'Saturday'
    END AS DayOfWeek,
    sma.IsActive
FROM tblShiftMachineAssignments sma
INNER JOIN tblShifts s ON sma.ShiftID = s.ShiftID
INNER JOIN JEGR_DB.dbo.tblMachines m ON sma.MachineRecNum = m.RecNum
ORDER BY m.IdJensen, sma.DayOfWeek, s.ShiftID;

PRINT '';
PRINT '=============================================';
PRINT 'NOTE: With 10-minute shifts and 5-minute gaps, you can test:';
PRINT '  - Held to the shift: scanning out during a shift is refused (orange)';
PRINT '  - Break: 5 minutes into each shift everyone on it is logged out; log in again';
PRINT '  - Shift end: after 10 minutes everyone on the shift is logged out';
PRINT '  - Legacy login during the gap after each shift (e.g. 06:10-06:15)';
PRINT '  - Shift start: a login made in the gap is logged out when the next shift starts';
PRINT '  - Multiple shift cycles: Test entire day in ~4 hours';
PRINT '=============================================';
PRINT 'Shift setup completed successfully!';
PRINT '=============================================';
GO

SET NOEXEC OFF;
GO
