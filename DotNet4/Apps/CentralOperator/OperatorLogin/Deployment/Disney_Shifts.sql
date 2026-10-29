-- =============================================
-- Disney - 1st and 2nd shift on every machine that has a card reader
-- Database: JEGR_Utils (same SQL instance as JEGR_DB). Run schema.sql first.
--
-- What it does
--   * Creates two shifts, "1st Shift" and "2nd Shift" (or updates them if they already exist), with
--     the start and end times and break start times set under SETTINGS below.
--   * Assigns both shifts, on the days listed, to every machine with a card reader: each
--     Machine_idJensen in JEGR_DB.dbo.tblTagReaderLocations, except any listed in @ExcludeMachines.
--     A reader whose machine isn't in JEGR_DB.dbo.tblMachines is listed and skipped.
--
-- PREVIEW FIRST: as supplied (@Apply = 0) it checks everything, shows what it would save, then
-- undoes it - nothing is changed. Check the machine list, then set @Apply = 1 and run it again.
--
-- Safe to re-run, for example to change an end time: the two shifts are found by name, their times
-- and break times are set to the values below, and any missing machine assignments are added.
-- It never removes a machine assignment and never changes any other shift. It stops without
-- changing anything if a time is wrong, a break is outside its shift, or a shift would overlap
-- the other one or another shift on the same machine and day.
-- Times can also be changed later in the Shift Editor (Shifts tab and Breaks tab).
--
-- Breaks: only the start of a break is stored. At that time everyone logged in on the shift is
-- logged out, and logs in again when they come back. The break end times below are notes only.
--
-- Also note
--   * Between the end of the 2nd shift and the start of the 1st, machines have no shift and
--     work as before. Anyone still logged in at 05:00 is logged out when the 1st shift starts.
--   * If the 2nd shift ever ends after 23:15: the service's fixed-time logout (AutoLogout /
--     AutoLogoutTime in CentralLoginService.exe.config, default 23:15) would log everyone out at
--     that time - move AutoLogoutTime or set AutoLogout to False.
--   * If the shift service is already running, changes reach it within about a minute. Adding a
--     machine during one of its shifts logs out anyone logged in on that machine without a shift.
-- =============================================

SET NOCOUNT ON;
IF DB_ID('JEGR_Utils') IS NULL OR DB_ID('JEGR_DB') IS NULL
BEGIN
    RAISERROR('JEGR_Utils or JEGR_DB not found on this instance - script stopped, nothing changed.', 16, 1);
    SET NOEXEC ON;
END
GO
USE JEGR_Utils;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

-- =============================================
-- SETTINGS - change these
-- =============================================
DECLARE @Apply bit = 0;                       -- 0 = preview only, nothing saved; 1 = save

DECLARE @Shift1Name  varchar(50) = '1st Shift';
DECLARE @Shift1Start time(0)     = '05:00';
DECLARE @Shift1End   time(0)     = '14:00';  -- 2pm

DECLARE @Shift2Name  varchar(50) = '2nd Shift';
DECLARE @Shift2Start time(0)     = '14:00';  -- 2pm
DECLARE @Shift2End   time(0)     = '23:00';  -- 11pm

-- Break start times, 24-hour clock. Add or remove rows as needed.
DECLARE @Breaks TABLE (ShiftNo tinyint NOT NULL, BreakTime time(0) NOT NULL, Note varchar(50) NULL);
INSERT INTO @Breaks (ShiftNo, BreakTime, Note) VALUES
    (1, '07:15', '1st break, to 07:30'),
    (1, '09:30', '2nd break, to 09:45'),
    (1, '11:30', 'Lunch, to 12:00'),
    (2, '16:00', '1st break, to 16:15'),
    (2, '18:00', 'Lunch, to 18:30'),
    (2, '20:15', '2nd break, to 20:30');

-- Days the shifts run: 0 = Sunday, 1 = Monday ... 6 = Saturday. Remove days that don't apply.
DECLARE @Days TABLE (DayOfWeek int NOT NULL);
INSERT INTO @Days (DayOfWeek) VALUES (0), (1), (2), (3), (4), (5), (6);

-- Machines to leave out, by Machine_idJensen. None for now.
DECLARE @ExcludeMachines TABLE (Machine_idJensen int NOT NULL);
-- INSERT INTO @ExcludeMachines (Machine_idJensen) VALUES (1234), (5678);

-- =============================================
-- Nothing to change below this line
-- =============================================
IF OBJECT_ID('dbo.tblShifts', 'U') IS NULL
   OR OBJECT_ID('dbo.tblShiftMachineAssignments', 'U') IS NULL
   OR OBJECT_ID('dbo.tblShiftBreaks', 'U') IS NULL
BEGIN
    RAISERROR('The shift tables (including tblShiftBreaks) are missing - run schema.sql first. Nothing changed.', 16, 1);
    RETURN;
END

DECLARE @Problems TABLE (Seq int IDENTITY(1,1), Problem nvarchar(1000) NOT NULL);
DECLARE @Shifts TABLE (ShiftNo tinyint NOT NULL, ShiftName varchar(50) NOT NULL,
                       StartTime time(0) NOT NULL, EndTime time(0) NOT NULL, ShiftID int NULL);
INSERT INTO @Shifts (ShiftNo, ShiftName, StartTime, EndTime) VALUES
    (1, LTRIM(RTRIM(ISNULL(@Shift1Name, ''))), @Shift1Start, @Shift1End),
    (2, LTRIM(RTRIM(ISNULL(@Shift2Name, ''))), @Shift2Start, @Shift2End);

-- ---- Times, names, breaks and days ----
INSERT INTO @Problems (Problem)
SELECT 'Shift ' + CAST(ShiftNo AS varchar(1)) + ' has no name.' FROM @Shifts WHERE ShiftName = '';

IF EXISTS (SELECT 1 FROM @Shifts a JOIN @Shifts b ON a.ShiftNo = 1 AND b.ShiftNo = 2 AND a.ShiftName = b.ShiftName)
    INSERT INTO @Problems (Problem) VALUES ('The two shifts need different names.');

INSERT INTO @Problems (Problem)
SELECT ShiftName + ': the start and end are the same time.' FROM @Shifts WHERE StartTime = EndTime;

INSERT INTO @Problems (Problem)
SELECT ShiftName + ': times must be whole minutes.' FROM @Shifts
WHERE DATEPART(second, StartTime) <> 0 OR DATEPART(second, EndTime) <> 0;

-- Overlap rule (as in the Shift Editor and the database trigger): a shift whose end is earlier
-- than its start runs past midnight; the end of one shift may equal the start of the next.
INSERT INTO @Problems (Problem)
SELECT a.ShiftName + ' (' + CONVERT(char(5), a.StartTime, 108) + '-' + CONVERT(char(5), a.EndTime, 108)
       + ') and ' + b.ShiftName + ' (' + CONVERT(char(5), b.StartTime, 108) + '-' + CONVERT(char(5), b.EndTime, 108)
       + ') overlap - they run on the same machines, so one must end before the other starts.'
FROM @Shifts a
JOIN @Shifts b ON a.ShiftNo = 1 AND b.ShiftNo = 2
CROSS APPLY (SELECT CASE
        WHEN a.StartTime < a.EndTime AND b.StartTime < b.EndTime
            THEN CASE WHEN a.StartTime < b.EndTime AND b.StartTime < a.EndTime THEN 1 ELSE 0 END
        WHEN a.StartTime < a.EndTime AND b.StartTime > b.EndTime
            THEN CASE WHEN b.StartTime < a.EndTime OR a.StartTime < b.EndTime THEN 1 ELSE 0 END
        WHEN a.StartTime > a.EndTime AND b.StartTime < b.EndTime
            THEN CASE WHEN a.StartTime < b.EndTime OR b.StartTime < a.EndTime THEN 1 ELSE 0 END
        ELSE 1 END AS Overlaps) o
WHERE a.StartTime <> a.EndTime AND b.StartTime <> b.EndTime AND o.Overlaps = 1;

INSERT INTO @Problems (Problem)
SELECT 'Break ' + CONVERT(char(5), BreakTime, 108) + ' is for shift ' + CAST(ShiftNo AS varchar(3))
       + ' - use 1 or 2.'
FROM @Breaks WHERE ShiftNo NOT IN (1, 2);

INSERT INTO @Problems (Problem)
SELECT 'Break ' + CONVERT(char(5), b.BreakTime, 108) + ' is not between the start (' + CONVERT(char(5), s.StartTime, 108)
       + ') and end (' + CONVERT(char(5), s.EndTime, 108) + ') of ' + s.ShiftName + '.'
FROM @Breaks b JOIN @Shifts s ON s.ShiftNo = b.ShiftNo
WHERE s.StartTime <> s.EndTime
  AND NOT (   (s.StartTime < s.EndTime AND b.BreakTime > s.StartTime AND b.BreakTime < s.EndTime)
           OR (s.StartTime > s.EndTime AND (b.BreakTime > s.StartTime OR b.BreakTime < s.EndTime)));

INSERT INTO @Problems (Problem)
SELECT 'Break ' + CONVERT(char(5), BreakTime, 108) + ' is listed more than once for shift '
       + CAST(ShiftNo AS varchar(3)) + '.'
FROM @Breaks GROUP BY ShiftNo, BreakTime HAVING COUNT(*) > 1;

INSERT INTO @Problems (Problem)
SELECT 'Break ' + CONVERT(char(5), BreakTime, 108) + ': break times must be whole minutes.'
FROM @Breaks WHERE DATEPART(second, BreakTime) <> 0;

IF NOT EXISTS (SELECT 1 FROM @Days)
    INSERT INTO @Problems (Problem) VALUES ('No days are listed.');
INSERT INTO @Problems (Problem)
SELECT 'Day ' + CAST(DayOfWeek AS varchar(10)) + ' is not a day - use 0 (Sunday) to 6 (Saturday).'
FROM @Days WHERE DayOfWeek NOT BETWEEN 0 AND 6;

-- ---- The two shifts, if they already exist ----
INSERT INTO @Problems (Problem)
SELECT 'There is more than one shift called "' + s.ShiftName + '" - rename or delete the extra one in the Shift Editor.'
FROM @Shifts s JOIN dbo.tblShifts t ON t.ShiftName = s.ShiftName
GROUP BY s.ShiftName HAVING COUNT(*) > 1;

UPDATE s SET ShiftID = t.ShiftID
FROM @Shifts s JOIN dbo.tblShifts t ON t.ShiftName = s.ShiftName;

-- ---- Machines: every Machine_idJensen that has a card reader ----
DECLARE @Readers TABLE (Machine_idJensen int NOT NULL, Readers int NOT NULL);
INSERT INTO @Readers (Machine_idJensen, Readers)
SELECT Machine_idJensen, COUNT(*) FROM JEGR_DB.dbo.tblTagReaderLocations GROUP BY Machine_idJensen;

DECLARE @Machines TABLE (Machine_idJensen int NOT NULL, MachineRecNum int NOT NULL, Readers int NOT NULL);
INSERT INTO @Machines (Machine_idJensen, MachineRecNum, Readers)
SELECT r.Machine_idJensen, m.RecNum, r.Readers
FROM @Readers r JOIN JEGR_DB.dbo.tblMachines m ON m.IdJensen = r.Machine_idJensen
WHERE r.Machine_idJensen NOT IN (SELECT Machine_idJensen FROM @ExcludeMachines);

IF NOT EXISTS (SELECT 1 FROM @Machines)
    INSERT INTO @Problems (Problem) VALUES ('No machines found: no reader in JEGR_DB.dbo.tblTagReaderLocations has a machine in JEGR_DB.dbo.tblMachines (after exclusions).');

-- What the shifts will be assigned to
DECLARE @Target TABLE (ShiftNo tinyint NOT NULL, MachineRecNum int NOT NULL, DayOfWeek int NOT NULL);
INSERT INTO @Target (ShiftNo, MachineRecNum, DayOfWeek)
SELECT s.ShiftNo, m.MachineRecNum, d.DayOfWeek
FROM @Shifts s
CROSS JOIN (SELECT DISTINCT MachineRecNum FROM @Machines) m
CROSS JOIN (SELECT DISTINCT DayOfWeek FROM @Days WHERE DayOfWeek BETWEEN 0 AND 6) d;

-- ---- Overlaps with any other shift on the same machine and day ----
-- (checks the new assignments and any the two shifts already have, using the new times)
DECLARE @Check TABLE (ShiftNo tinyint NOT NULL, MachineRecNum int NOT NULL, DayOfWeek int NOT NULL);
INSERT INTO @Check (ShiftNo, MachineRecNum, DayOfWeek)
SELECT ShiftNo, MachineRecNum, DayOfWeek FROM @Target
UNION
SELECT s.ShiftNo, a.MachineRecNum, a.DayOfWeek
FROM @Shifts s JOIN dbo.tblShiftMachineAssignments a ON a.ShiftID = s.ShiftID
WHERE a.IsActive = 1;

DECLARE @Overlaps TABLE (ShiftNo tinyint, MachineIdJensen int, DayOfWeek int,
                         OtherShift varchar(50), OtherStart time(0), OtherEnd time(0));
INSERT INTO @Overlaps (ShiftNo, MachineIdJensen, DayOfWeek, OtherShift, OtherStart, OtherEnd)
SELECT c.ShiftNo, m.IdJensen, c.DayOfWeek, os.ShiftName, os.StartTime, os.EndTime
FROM @Check c
JOIN @Shifts s ON s.ShiftNo = c.ShiftNo
JOIN dbo.tblShiftMachineAssignments a
     ON a.MachineRecNum = c.MachineRecNum AND a.DayOfWeek = c.DayOfWeek AND a.IsActive = 1
JOIN dbo.tblShifts os ON os.ShiftID = a.ShiftID AND os.IsActive = 1
LEFT JOIN JEGR_DB.dbo.tblMachines m ON m.RecNum = c.MachineRecNum
CROSS APPLY (SELECT CASE
        WHEN s.StartTime < s.EndTime AND os.StartTime < os.EndTime
            THEN CASE WHEN s.StartTime < os.EndTime AND os.StartTime < s.EndTime THEN 1 ELSE 0 END
        WHEN s.StartTime < s.EndTime AND os.StartTime > os.EndTime
            THEN CASE WHEN os.StartTime < s.EndTime OR s.StartTime < os.EndTime THEN 1 ELSE 0 END
        WHEN s.StartTime > s.EndTime AND os.StartTime < os.EndTime
            THEN CASE WHEN s.StartTime < os.EndTime OR os.StartTime < s.EndTime THEN 1 ELSE 0 END
        ELSE 1 END AS Overlaps) o
WHERE os.ShiftID NOT IN (SELECT ShiftID FROM @Shifts WHERE ShiftID IS NOT NULL)
  AND s.StartTime <> s.EndTime AND os.StartTime <> os.EndTime
  AND o.Overlaps = 1;

INSERT INTO @Problems (Problem)
SELECT s.ShiftName + ' would overlap ' + CAST(g.Others AS varchar(10)) + ' other shift(s) on machine '
       + ISNULL(CAST(g.MachineIdJensen AS varchar(10)), '?') + ', e.g. "' + e.OtherShift + '" ('
       + CONVERT(char(5), e.OtherStart, 108) + '-' + CONVERT(char(5), e.OtherEnd, 108) + ') on '
       + SUBSTRING('SunMonTueWedThuFriSat', e.DayOfWeek * 3 + 1, 3) + '.'
FROM (SELECT ShiftNo, MachineIdJensen, COUNT(DISTINCT OtherShift) AS Others
      FROM @Overlaps GROUP BY ShiftNo, MachineIdJensen) g
JOIN @Shifts s ON s.ShiftNo = g.ShiftNo
CROSS APPLY (SELECT TOP (1) v.OtherShift, v.OtherStart, v.OtherEnd, v.DayOfWeek FROM @Overlaps v
             WHERE v.ShiftNo = g.ShiftNo AND (v.MachineIdJensen = g.MachineIdJensen OR (v.MachineIdJensen IS NULL AND g.MachineIdJensen IS NULL))
             ORDER BY v.DayOfWeek, v.OtherStart) e
ORDER BY g.ShiftNo, g.MachineIdJensen;

IF EXISTS (SELECT 1 FROM @Overlaps)
    INSERT INTO @Problems (Problem) VALUES ('For the overlaps, see the Overlaps list. Change the times above, or remove '
        + 'the other shifts'' machine assignments in the Shift Editor, or leave those machines out with @ExcludeMachines.');

-- ---- Stop here if anything is wrong ----
IF EXISTS (SELECT 1 FROM @Problems)
BEGIN
    DECLARE @seq int = 0, @problem nvarchar(1000);
    PRINT N'PROBLEMS:';
    WHILE 1 = 1
    BEGIN
        SELECT TOP (1) @seq = Seq, @problem = Problem FROM @Problems WHERE Seq > @seq ORDER BY Seq;
        IF @@ROWCOUNT = 0 BREAK;
        PRINT N'  - ' + @problem;
    END
    IF EXISTS (SELECT 1 FROM @Overlaps)
        SELECT TOP (100) s.ShiftName AS [Shift], v.MachineIdJensen AS [Machine],
               SUBSTRING('SunMonTueWedThuFriSat', v.DayOfWeek * 3 + 1, 3) AS [Day],
               v.OtherShift AS [Overlaps shift],
               CONVERT(char(5), v.OtherStart, 108) AS [Its start], CONVERT(char(5), v.OtherEnd, 108) AS [Its end]
        FROM @Overlaps v JOIN @Shifts s ON s.ShiftNo = v.ShiftNo
        ORDER BY v.ShiftNo, v.MachineIdJensen, v.DayOfWeek;
    RAISERROR('Script stopped - nothing changed. Fix the problems listed above and run it again.', 16, 1);
    RETURN;
END

-- ---- Save (or preview) ----
DECLARE @ShiftsAdded int = 0, @ShiftsUpdated int = 0, @BreaksAdded int = 0, @BreaksRemoved int = 0,
        @AssignmentsAdded int = 0, @AssignmentsReactivated int = 0;

BEGIN TRY
    BEGIN TRANSACTION;

    UPDATE t SET StartTime = s.StartTime, EndTime = s.EndTime, IsActive = 1, ModifiedDate = GETDATE()
    FROM dbo.tblShifts t JOIN @Shifts s ON s.ShiftID = t.ShiftID
    WHERE t.StartTime <> s.StartTime OR t.EndTime <> s.EndTime OR t.IsActive = 0;
    SET @ShiftsUpdated = @@ROWCOUNT;

    INSERT INTO dbo.tblShifts (ShiftName, StartTime, EndTime, DeltaMinus, DeltaPlus, IsActive)
    SELECT ShiftName, StartTime, EndTime, 0, 0, 1 FROM @Shifts WHERE ShiftID IS NULL ORDER BY ShiftNo;
    SET @ShiftsAdded = @@ROWCOUNT;

    UPDATE s SET ShiftID = t.ShiftID
    FROM @Shifts s JOIN dbo.tblShifts t ON t.ShiftName = s.ShiftName
    WHERE s.ShiftID IS NULL;

    -- Each shift's break times become exactly the list above
    DELETE b
    FROM dbo.tblShiftBreaks b JOIN @Shifts s ON s.ShiftID = b.ShiftID
    WHERE NOT EXISTS (SELECT 1 FROM @Breaks x WHERE x.ShiftNo = s.ShiftNo AND x.BreakTime = b.BreakTime);
    SET @BreaksRemoved = @@ROWCOUNT;

    INSERT INTO dbo.tblShiftBreaks (ShiftID, BreakTime)
    SELECT DISTINCT s.ShiftID, x.BreakTime
    FROM @Breaks x JOIN @Shifts s ON s.ShiftNo = x.ShiftNo
    WHERE NOT EXISTS (SELECT 1 FROM dbo.tblShiftBreaks b WHERE b.ShiftID = s.ShiftID AND b.BreakTime = x.BreakTime);
    SET @BreaksAdded = @@ROWCOUNT;

    -- Machine assignments: switch back on any that were switched off, add the missing ones
    UPDATE a SET IsActive = 1
    FROM dbo.tblShiftMachineAssignments a
    JOIN @Shifts s ON s.ShiftID = a.ShiftID
    JOIN @Target t ON t.ShiftNo = s.ShiftNo AND t.MachineRecNum = a.MachineRecNum AND t.DayOfWeek = a.DayOfWeek
    WHERE a.IsActive = 0;
    SET @AssignmentsReactivated = @@ROWCOUNT;

    INSERT INTO dbo.tblShiftMachineAssignments (ShiftID, MachineRecNum, DayOfWeek, IsActive)
    SELECT s.ShiftID, t.MachineRecNum, t.DayOfWeek, 1
    FROM @Target t JOIN @Shifts s ON s.ShiftNo = t.ShiftNo
    WHERE NOT EXISTS (SELECT 1 FROM dbo.tblShiftMachineAssignments a
                      WHERE a.ShiftID = s.ShiftID AND a.MachineRecNum = t.MachineRecNum AND a.DayOfWeek = t.DayOfWeek);
    SET @AssignmentsAdded = @@ROWCOUNT;

    -- ---- Report ----
    DECLARE @machineList varchar(max), @skippedList varchar(max), @excludedList varchar(max);
    SELECT @machineList = STUFF((SELECT ', ' + CAST(Machine_idJensen AS varchar(10)) AS [text()]
                                 FROM @Machines GROUP BY Machine_idJensen ORDER BY Machine_idJensen
                                 FOR XML PATH('')), 1, 2, '');
    SELECT @skippedList = STUFF((SELECT ', ' + CAST(r.Machine_idJensen AS varchar(10)) AS [text()]
                                 FROM @Readers r
                                 WHERE NOT EXISTS (SELECT 1 FROM JEGR_DB.dbo.tblMachines m WHERE m.IdJensen = r.Machine_idJensen)
                                 ORDER BY r.Machine_idJensen FOR XML PATH('')), 1, 2, '');
    SELECT @excludedList = STUFF((SELECT ', ' + CAST(Machine_idJensen AS varchar(10)) AS [text()]
                                  FROM @ExcludeMachines GROUP BY Machine_idJensen ORDER BY Machine_idJensen
                                  FOR XML PATH('')), 1, 2, '');

    DECLARE @machineCount int = (SELECT COUNT(DISTINCT Machine_idJensen) FROM @Machines);
    PRINT 'Machines with card readers: ' + CAST(@machineCount AS varchar(10)) + ' (' + ISNULL(@machineList, '') + ')';
    IF @skippedList IS NOT NULL
        PRINT 'Skipped - reader''s machine is not in JEGR_DB.dbo.tblMachines: ' + @skippedList;
    IF @excludedList IS NOT NULL
        PRINT 'Left out (@ExcludeMachines): ' + @excludedList;
    PRINT 'Shifts added: ' + CAST(@ShiftsAdded AS varchar(10)) + ', updated: ' + CAST(@ShiftsUpdated AS varchar(10));
    PRINT 'Break times added: ' + CAST(@BreaksAdded AS varchar(10)) + ', removed: ' + CAST(@BreaksRemoved AS varchar(10));
    PRINT 'Machine assignments added: ' + CAST(@AssignmentsAdded AS varchar(10))
          + CASE WHEN @AssignmentsReactivated > 0 THEN ', switched back on: ' + CAST(@AssignmentsReactivated AS varchar(10)) ELSE '' END;

    IF EXISTS (SELECT 1 FROM @Shifts WHERE StartTime > EndTime AND EndTime > '00:00')
       AND (SELECT COUNT(DISTINCT DayOfWeek) FROM @Days) < 7
        PRINT 'Check: a shift runs past midnight but not every day is listed. The part after midnight only '
              + 'applies on a day that is listed too.';
    IF EXISTS (SELECT 1 FROM @Shifts
               WHERE (StartTime < EndTime AND StartTime <= '23:15' AND EndTime > '23:15')
                  OR (StartTime > EndTime AND ('23:15' >= StartTime OR '23:15' < EndTime)))
        PRINT 'Check: a shift is running at 23:15 - if AutoLogout is on in the service config, everyone is logged '
              + 'out at AutoLogoutTime (default 23:15). Move AutoLogoutTime or set AutoLogout to False.';

    SELECT t.ShiftName AS [Shift],
           CONVERT(char(5), t.StartTime, 108) AS [Start],
           CONVERT(char(5), t.EndTime, 108) AS [End],
           STUFF((SELECT ', ' + CONVERT(char(5), b.BreakTime, 108) AS [text()]
                  FROM dbo.tblShiftBreaks b WHERE b.ShiftID = t.ShiftID ORDER BY b.BreakTime
                  FOR XML PATH('')), 1, 2, '') AS [Break start times],
           (SELECT COUNT(DISTINCT a.MachineRecNum) FROM dbo.tblShiftMachineAssignments a
            WHERE a.ShiftID = t.ShiftID AND a.IsActive = 1) AS [Machines],
           STUFF((SELECT ' ' + SUBSTRING('SunMonTueWedThuFriSat', d.DayOfWeek * 3 + 1, 3) AS [text()]
                  FROM (SELECT DISTINCT a.DayOfWeek FROM dbo.tblShiftMachineAssignments a
                        WHERE a.ShiftID = t.ShiftID AND a.IsActive = 1) d
                  ORDER BY d.DayOfWeek FOR XML PATH('')), 1, 1, '') AS [Days]
    FROM dbo.tblShifts t JOIN @Shifts s ON s.ShiftID = t.ShiftID
    ORDER BY s.ShiftNo;

    SELECT Machine_idJensen AS [Machine], MAX(Readers) AS [Card readers]
    FROM @Machines GROUP BY Machine_idJensen ORDER BY Machine_idJensen;

    IF @Apply = 1
    BEGIN
        COMMIT TRANSACTION;
        PRINT 'SAVED.';
    END
    ELSE
    BEGIN
        ROLLBACK TRANSACTION;
        PRINT 'PREVIEW ONLY - nothing was saved. Check the lists above, then set @Apply = 1 at the top and run again.';
    END
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    DECLARE @err nvarchar(2048) = ERROR_MESSAGE();
    RAISERROR('Script stopped - nothing changed: %s', 16, 1, @err);
END CATCH
GO

SET NOEXEC OFF;
GO
