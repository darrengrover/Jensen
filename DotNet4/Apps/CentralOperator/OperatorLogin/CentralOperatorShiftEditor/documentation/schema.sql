-- =============================================
-- Central Operator Shift Management Schema
-- Database: JEGR_Utils (must be on the same SQL instance as JEGR_DB)
--
-- Safe to re-run: tables, indexes and the seed row are only created if missing;
-- the function, trigger and views hold no data and are dropped and re-created.
-- Run on a database created before break times were added (October 2026), it also upgrades it
-- once: see "ONE-TIME UPGRADE" below.
-- Additive only - nothing in JEGR_DB is created or altered, and the non-shift
-- service never reads these objects. To remove them see rollback_schema.sql.
-- If JEGR_Utils doesn't exist the script stops with "script stopped, nothing changed";
-- any errors listed after that message are follow-on compile errors and can be ignored.
-- =============================================

-- Stop (without running anything else) if JEGR_Utils doesn't exist
IF DB_ID('JEGR_Utils') IS NULL
BEGIN
    RAISERROR('Database JEGR_Utils not found on this instance - script stopped, nothing changed.', 16, 1);
    SET NOEXEC ON;
END
GO
USE JEGR_Utils;
GO
IF DB_ID('JEGR_DB') IS NULL
    RAISERROR('Warning: JEGR_DB not found on this instance - the shift service and editor expect both databases on the same instance.', 10, 1);
GO

-- Core Shifts Table
IF OBJECT_ID('dbo.tblShifts', 'U') IS NULL
CREATE TABLE dbo.tblShifts (
    ShiftID int IDENTITY(1,1) PRIMARY KEY,
    ShiftName varchar(50) NOT NULL,
    StartTime time NOT NULL,
    EndTime time NOT NULL,
    -- Grace minutes either side of the shift end. Normally 0 and not shown in the Shift Editor;
    -- only changed by SQL (Ge-Mac support).
    DeltaMinus int NOT NULL DEFAULT 0, -- minutes before shift end operator can logout
    DeltaPlus int NOT NULL DEFAULT 0,  -- minutes after shift end operator can logout
    CrossesMidnight AS (CASE WHEN EndTime < StartTime THEN 1 ELSE 0 END), -- computed column
    IsActive bit NOT NULL DEFAULT 1,
    CreatedDate datetime NOT NULL DEFAULT GETDATE(),
    ModifiedDate datetime NOT NULL DEFAULT GETDATE(),

    CONSTRAINT CK_tblShifts_DeltaValues CHECK (DeltaMinus >= 0 AND DeltaPlus >= 0),
    CONSTRAINT CK_tblShifts_ShiftName CHECK (LEN(LTRIM(RTRIM(ShiftName))) > 0)
);
GO

-- Machine-Shift Assignments with Day of Week
IF OBJECT_ID('dbo.tblShiftMachineAssignments', 'U') IS NULL
CREATE TABLE dbo.tblShiftMachineAssignments (
    AssignmentID int IDENTITY(1,1) PRIMARY KEY,
    ShiftID int NOT NULL,
    MachineRecNum int NOT NULL, -- FK to JEGR_DB machine table RecNum
    DayOfWeek tinyint NOT NULL, -- 0=Sunday, 1=Monday, 2=Tuesday, etc.
    IsActive bit NOT NULL DEFAULT 1,
    CreatedDate datetime NOT NULL DEFAULT GETDATE(),

    CONSTRAINT FK_tblShiftMachineAssignments_ShiftID
        FOREIGN KEY (ShiftID) REFERENCES dbo.tblShifts(ShiftID),
    CONSTRAINT CK_tblShiftMachineAssignments_DayOfWeek
        CHECK (DayOfWeek BETWEEN 0 AND 6),

    -- Unique constraint to prevent duplicate assignments
    CONSTRAINT UQ_tblShiftMachineAssignments_Machine_Day_Shift
        UNIQUE (MachineRecNum, DayOfWeek, ShiftID)
);
GO

-- System-wide Settings
IF OBJECT_ID('dbo.tblShiftSettings', 'U') IS NULL
CREATE TABLE dbo.tblShiftSettings (
    SettingID int IDENTITY(1,1) PRIMARY KEY,
    SettingName varchar(50) NOT NULL UNIQUE,
    SettingValue varchar(100) NOT NULL,
    Detail varchar(255) NULL,
    ModifiedDate datetime NOT NULL DEFAULT GETDATE(),

    CONSTRAINT CK_tblShiftSettings_SettingName
        CHECK (LEN(LTRIM(RTRIM(SettingName))) > 0)
);
GO

-- Per-Operator Settings
IF OBJECT_ID('dbo.tblOperatorSettings', 'U') IS NULL
CREATE TABLE dbo.tblOperatorSettings (
    OperatorSettingID int IDENTITY(1,1) PRIMARY KEY,
    OperatorRecNum int NOT NULL UNIQUE, -- FK to JEGR_DB operator table RecNum
    CanReplaceOperator bit NOT NULL DEFAULT 0, -- the "operator replace" setting for supervisors
    CanLogoutShift bit NOT NULL DEFAULT 0, -- allows operator to logout during shift regardless of delta
    ModifiedDate datetime NOT NULL DEFAULT GETDATE()
);
GO

-- =============================================
-- ONE-TIME UPGRADE of a database created before break times were added (October 2026).
-- Runs only while tblShiftBreaks doesn't exist yet, so a later re-run never undoes a value
-- that was changed on purpose:
--   * AutoLogoutAtShiftEnd is turned on (it is now on unless switched off by SQL)
--   * the grace minutes on existing shifts are set to 0 (the Shift Editor no longer shows them)
-- =============================================
IF OBJECT_ID('dbo.tblShiftBreaks', 'U') IS NULL
BEGIN
    UPDATE dbo.tblShiftSettings SET SettingValue = 'True', ModifiedDate = GETDATE()
    WHERE SettingName = 'AutoLogoutAtShiftEnd' AND SettingValue <> 'True';

    UPDATE dbo.tblShifts SET DeltaMinus = 0, DeltaPlus = 0, ModifiedDate = GETDATE()
    WHERE DeltaMinus <> 0 OR DeltaPlus <> 0;
END
GO

-- Break start times within a shift. At each one everyone logged in on the shift is logged out;
-- logging back in is up to the operator. A break has no end time.
-- Deleting a shift deletes its breaks.
IF OBJECT_ID('dbo.tblShiftBreaks', 'U') IS NULL
CREATE TABLE dbo.tblShiftBreaks (
    BreakID int IDENTITY(1,1) PRIMARY KEY,
    ShiftID int NOT NULL,
    BreakTime time(0) NOT NULL, -- time of day, between the shift's start and end
    CreatedDate datetime NOT NULL DEFAULT GETDATE(),

    CONSTRAINT FK_tblShiftBreaks_ShiftID
        FOREIGN KEY (ShiftID) REFERENCES dbo.tblShifts(ShiftID) ON DELETE CASCADE,

    -- Unique constraint to prevent the same break time twice on a shift
    CONSTRAINT UQ_tblShiftBreaks_Shift_BreakTime
        UNIQUE (ShiftID, BreakTime)
);
GO

-- =============================================
-- INDEXES FOR PERFORMANCE
-- =============================================

-- Index for quick shift lookups by time
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tblShifts_Times' AND object_id = OBJECT_ID('dbo.tblShifts'))
    CREATE INDEX IX_tblShifts_Times ON dbo.tblShifts (StartTime, EndTime, IsActive);

-- Index for machine assignment lookups
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tblShiftMachineAssignments_Machine_Day' AND object_id = OBJECT_ID('dbo.tblShiftMachineAssignments'))
    CREATE INDEX IX_tblShiftMachineAssignments_Machine_Day
        ON dbo.tblShiftMachineAssignments (MachineRecNum, DayOfWeek, IsActive);

-- Index for operator settings lookups
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tblOperatorSettings_OperatorRecNum' AND object_id = OBJECT_ID('dbo.tblOperatorSettings'))
    CREATE INDEX IX_tblOperatorSettings_OperatorRecNum
        ON dbo.tblOperatorSettings (OperatorRecNum);
GO

-- =============================================
-- VALIDATION FUNCTION AND TRIGGER FOR SHIFT OVERLAPS
-- (dropped and re-created so re-running picks up any changes)
-- =============================================

IF OBJECT_ID('dbo.tr_tblShiftMachineAssignments_ValidateOverlap', 'TR') IS NOT NULL
    DROP TRIGGER dbo.tr_tblShiftMachineAssignments_ValidateOverlap;
IF OBJECT_ID('dbo.fn_CheckShiftOverlap', 'FN') IS NOT NULL
    DROP FUNCTION dbo.fn_CheckShiftOverlap;
GO

CREATE FUNCTION dbo.fn_CheckShiftOverlap
(
    @MachineRecNum int,
    @DayOfWeek tinyint,
    @StartTime time,
    @EndTime time,
    @ExcludeAssignmentID int = NULL
)
RETURNS bit
AS
BEGIN
    DECLARE @HasOverlap bit = 0;

    -- Check for overlaps with existing shifts for the same machine and day
    IF EXISTS (
        SELECT 1
        FROM dbo.tblShiftMachineAssignments sma
        INNER JOIN dbo.tblShifts s ON sma.ShiftID = s.ShiftID
        WHERE sma.MachineRecNum = @MachineRecNum
          AND sma.DayOfWeek = @DayOfWeek
          AND sma.IsActive = 1
          AND s.IsActive = 1
          AND (@ExcludeAssignmentID IS NULL OR sma.AssignmentID != @ExcludeAssignmentID)
          AND (
              -- Case 1: Neither shift crosses midnight
              (s.StartTime <= s.EndTime AND @StartTime <= @EndTime AND
               NOT (@EndTime <= s.StartTime OR @StartTime >= s.EndTime))
              OR
              -- Case 2: Existing shift crosses midnight, new shift doesn't
              (s.StartTime > s.EndTime AND @StartTime <= @EndTime AND
               NOT (@EndTime <= s.StartTime AND @StartTime >= s.EndTime))
              OR
              -- Case 3: New shift crosses midnight, existing doesn't
              (s.StartTime <= s.EndTime AND @StartTime > @EndTime AND
               NOT (s.EndTime <= @StartTime AND s.StartTime >= @EndTime))
              OR
              -- Case 4: Both shifts cross midnight
              (s.StartTime > s.EndTime AND @StartTime > @EndTime)
          )
    )
    BEGIN
        SET @HasOverlap = 1;
    END

    RETURN @HasOverlap;
END;
GO

CREATE TRIGGER dbo.tr_tblShiftMachineAssignments_ValidateOverlap
ON dbo.tblShiftMachineAssignments
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    -- Check each inserted/updated record
    IF EXISTS (
        SELECT 1
        FROM inserted i
        INNER JOIN dbo.tblShifts s ON i.ShiftID = s.ShiftID
        WHERE dbo.fn_CheckShiftOverlap(i.MachineRecNum, i.DayOfWeek, s.StartTime, s.EndTime, i.AssignmentID) = 1
    )
    BEGIN
        RAISERROR('Shift assignment would create overlapping shifts for the same machine on the same day.', 16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END
END;
GO

-- =============================================
-- INITIAL SYSTEM SETTINGS
-- AutoLogoutAtShiftEnd: operators are logged out automatically when their shift ends.
-- On by default - the service also treats a missing row as on. It is not shown in the
-- Shift Editor; to switch it off (Ge-Mac support only):
--   UPDATE JEGR_Utils.dbo.tblShiftSettings SET SettingValue = 'False', ModifiedDate = GETDATE()
--   WHERE SettingName = 'AutoLogoutAtShiftEnd';
-- The service picks the change up within about a minute.
-- =============================================

IF NOT EXISTS (SELECT 1 FROM dbo.tblShiftSettings WHERE SettingName = 'AutoLogoutAtShiftEnd')
    INSERT INTO dbo.tblShiftSettings (SettingName, SettingValue, Detail)
    VALUES
        ('AutoLogoutAtShiftEnd', 'True', 'Automatically log out operators when their shift ends');
GO

-- =============================================
-- UTILITY VIEWS FOR EASY QUERYING (not used by the service or editor)
-- DayOfWeek is stored as 0=Sunday..6=Saturday (.NET DayOfWeek). Today's value is
-- (DATEPART(weekday, GETDATE()) + @@DATEFIRST - 1) % 7, which is independent of SET DATEFIRST.
-- =============================================

IF OBJECT_ID('dbo.vw_CurrentActiveShifts', 'V') IS NOT NULL
    DROP VIEW dbo.vw_CurrentActiveShifts;
IF OBJECT_ID('dbo.vw_MachineShiftDetails', 'V') IS NOT NULL
    DROP VIEW dbo.vw_MachineShiftDetails;
GO

-- View to get today's shifts for all machines
-- (like the service, the after-midnight part of an overnight shift only shows if the shift
-- is also assigned to the following day)
CREATE VIEW dbo.vw_CurrentActiveShifts
AS
SELECT
    sma.MachineRecNum,
    s.ShiftID,
    s.ShiftName,
    s.StartTime,
    s.EndTime,
    s.DeltaMinus,
    s.DeltaPlus,
    s.CrossesMidnight,
    sma.DayOfWeek,
    CASE
        WHEN s.StartTime <= s.EndTime THEN
            -- Normal shift
            CASE WHEN CAST(GETDATE() AS time) BETWEEN s.StartTime AND s.EndTime THEN 1 ELSE 0 END
        ELSE
            -- Cross-midnight shift
            CASE WHEN CAST(GETDATE() AS time) >= s.StartTime OR CAST(GETDATE() AS time) <= s.EndTime THEN 1 ELSE 0 END
    END AS IsCurrentlyActive
FROM dbo.tblShiftMachineAssignments sma
INNER JOIN dbo.tblShifts s ON sma.ShiftID = s.ShiftID
WHERE sma.DayOfWeek = (DATEPART(weekday, GETDATE()) + @@DATEFIRST - 1) % 7
  AND sma.IsActive = 1
  AND s.IsActive = 1;
GO

-- View to get all machine-shift assignments with shift details
CREATE VIEW dbo.vw_MachineShiftDetails
AS
SELECT
    sma.AssignmentID,
    sma.MachineRecNum,
    s.ShiftID,
    s.ShiftName,
    s.StartTime,
    s.EndTime,
    s.DeltaMinus,
    s.DeltaPlus,
    s.CrossesMidnight,
    sma.DayOfWeek,
    CASE sma.DayOfWeek
        WHEN 0 THEN 'Sunday'
        WHEN 1 THEN 'Monday'
        WHEN 2 THEN 'Tuesday'
        WHEN 3 THEN 'Wednesday'
        WHEN 4 THEN 'Thursday'
        WHEN 5 THEN 'Friday'
        WHEN 6 THEN 'Saturday'
    END AS DayOfWeekName,
    sma.IsActive AS AssignmentActive,
    s.IsActive AS ShiftActive,
    sma.CreatedDate
FROM dbo.tblShiftMachineAssignments sma
INNER JOIN dbo.tblShifts s ON sma.ShiftID = s.ShiftID;
GO

SET NOEXEC OFF;
GO
