-- =============================================
-- Central Operator Shift Management - REMOVE schema
-- Database: JEGR_Utils
--
-- !! DESTRUCTIVE: permanently deletes all shifts, break times, machine assignments, shift
-- !! settings and operator shift permissions. Back up JEGR_Utils first.
--
-- NOT needed to roll back to the non-shift service - it never reads these objects.
-- Only run this to remove shift management completely, after the shift service
-- (Cockpit_Central_Operator_Shift) has been stopped or uninstalled.
-- Safe to re-run: each object is only dropped if it exists.
-- =============================================

IF DB_ID('JEGR_Utils') IS NULL
BEGIN
    RAISERROR('Database JEGR_Utils not found on this instance - script stopped, nothing changed.', 16, 1);
    SET NOEXEC ON;
END
GO
USE JEGR_Utils;
GO

IF OBJECT_ID('dbo.vw_CurrentActiveShifts', 'V') IS NOT NULL
    DROP VIEW dbo.vw_CurrentActiveShifts;
IF OBJECT_ID('dbo.vw_MachineShiftDetails', 'V') IS NOT NULL
    DROP VIEW dbo.vw_MachineShiftDetails;
IF OBJECT_ID('dbo.tr_tblShiftMachineAssignments_ValidateOverlap', 'TR') IS NOT NULL
    DROP TRIGGER dbo.tr_tblShiftMachineAssignments_ValidateOverlap;
IF OBJECT_ID('dbo.fn_CheckShiftOverlap', 'FN') IS NOT NULL
    DROP FUNCTION dbo.fn_CheckShiftOverlap;
GO

-- Assignments and breaks first (foreign keys to tblShifts)
IF OBJECT_ID('dbo.tblShiftMachineAssignments', 'U') IS NOT NULL
    DROP TABLE dbo.tblShiftMachineAssignments;
IF OBJECT_ID('dbo.tblShiftBreaks', 'U') IS NOT NULL
    DROP TABLE dbo.tblShiftBreaks;
IF OBJECT_ID('dbo.tblShifts', 'U') IS NOT NULL
    DROP TABLE dbo.tblShifts;
IF OBJECT_ID('dbo.tblShiftSettings', 'U') IS NOT NULL
    DROP TABLE dbo.tblShiftSettings;
IF OBJECT_ID('dbo.tblOperatorSettings', 'U') IS NOT NULL
    DROP TABLE dbo.tblOperatorSettings;
GO

SET NOEXEC OFF;
GO
