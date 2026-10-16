# Shift Management - Install and Rollback Runbook

Installs the shift version of the Central Operator service **alongside** the current (non-shift)
service, then switches between them with a script. The current service, its folder and its
config are never changed, so rollback is one script, takes about a minute, and needs no installer
or database restore.

## How the isolation works

| | Current service (Sep 2025 build, v1.1.51) | Shift service |
|---|---|---|
| Service name | `Disney_Central_Operator` (confirm in step 1) | `Cockpit_Central_Operator_Shift` |
| Folder | `C:\Program Files (x86)\Jensen\CentralLoginServiceSetup\` | `C:\Program Files (x86)\Jensen\CentralLoginServiceSetup_Shift\` |
| Installer identity | its own UpgradeCode | different UpgradeCode - never upgrades or removes the current one |

Each service loads its own private copies of every DLL from its own folder (nothing goes in the
GAC). They share the database: login/logout records written by one are read by the other on start.

**The two services must never run at the same time** - both would drive the same card readers.
The scripts only start one after the other has stopped, and disable whichever isn't live.

## Before going to site

- [ ] Release MSI built from this source (Visual Studio, `Release | Mixed Platforms`,
      right-click `CentralLoginServiceSetup_Shift` > Build). The MSI is version 1.3.0; check `CentralLoginService.exe`
      is file version 1.3.0.0 or later and `TPL.TagReaders.dll` 1.0.4 or later. The MSI packages one `Newtonsoft.Json.dll` (13.x) -
      a build warning about two objects with the same target location means an old `DynamicDataLayer` was picked up.
- [ ] Shift Editor Release build is version 1.0.2.0 or later (break times; no Delta columns or Shift Settings tab)
- [ ] `SHIFT_MANAGEMENT_TEST_PLAN.md` Parts C (legacy parity), A and D passed on the test system
- [ ] `Switch_To_Shift.cmd` and `Rollback_To_Legacy.cmd` run both ways on a test machine or VM
- [ ] Files: `CentralLoginServiceSetup_Shift.msi`, `schema.sql`, `rollback_schema.sql`, the two scripts,
      the Shift Editor installer `CentralOperatorShiftEditorSetup.msi` (version 1.0.2) and its `setup.exe`,
      a `DBConnections.xml` with placeholders (if the site doesn't have its own), and the support tool
      `CardReaderMonitor.exe`
- [ ] **Never** take `documentation\testing\setup_test_shifts.sql` - it deletes all shifts
- [ ] **Never** install the non-shift `CentralLoginServiceSetup.msi` (v1.2.0) on site - it would share the
      current install's folder under a different product identity

## Install (steps 1-5 can be done days before cutover - the live service is unaffected)

1. **Baseline**
   - `sc query state= all | findstr /i central_operator` - note the current service name
   - `sc qc <name>` - note `BINARY_PATH_NAME` and `START_TYPE`
   - Note the version in Programs and Features
   - Zip the whole current install folder, including `CentralLoginService.exe.config`
   - Back up `JEGR_Utils` and `JEGR_DB`
2. **Database** - run `schema.sql` against the SQL instance holding `JEGR_DB` (it creates the shift
   tables in `JEGR_Utils`, adds nothing to `JEGR_DB`, and is safe to re-run). The service and the
   Shift Editor both need the current version of the script: it adds the break-times table
   `tblShiftBreaks`. Run on a database that already has the earlier shift tables, it upgrades them once
   (sets `AutoLogoutAtShiftEnd` to True and the grace minutes on existing shifts to 0).
3. **Install** `CentralLoginServiceSetup_Shift.msi`. It creates `Cockpit_Central_Operator_Shift` (not started).
4. **Configure** `...\CentralLoginServiceSetup_Shift\CentralLoginService.exe.config`:
   - Copy the connection string (`JEGRConnection`) and the `DebugMode`, `AutoLogout`, `AutoLogoutTime`
     and `UseOperatorPermissions` values from the current service's config
   - Check `ServiceName` = `Cockpit_Central_Operator_Shift` and `UseShiftManagement` = `True`
   - **Night shifts:** the fixed-time auto-logout (`AutoLogout` / `AutoLogoutTime`, default 23:15) still runs
     in shift mode and would log out night-shift operators - set `AutoLogout` = `False` or move the time
   - Run `sc qc Cockpit_Central_Operator_Shift` - `BINARY_PATH_NAME` must be in the `_Shift` folder
5. **Shift Editor** - on each PC that needs it
   - Run `CentralOperatorShiftEditorSetup.msi` as Administrator (or its `setup.exe`, which installs
     .NET Framework 4.8 first if the PC doesn't have it). It installs to
     `C:\Program Files (x86)\Jensen\CentralOperatorShiftEditor\` with a Start-menu and a desktop shortcut,
     both starting in that folder
   - Copy the site's `DBConnections.xml` into that folder - the editor reads it from there. If the site
     doesn't have one yet, use the copy in the kit's `4_Shift_Editor` folder and replace its `SET_SQL_...`
     placeholders with the site connection. The installer never installs or changes this file, so
     upgrading or uninstalling the editor leaves it in place
   - Enter the pilot shifts, machine assignments, break start times and operator permissions
   - Overnight shifts (e.g. 22:00-06:00) must be assigned to **both** days they touch
   - The editor has no login - only install it on the PCs of people who should change shifts
   - Two things are deliberately **not** in the editor and are changed by SQL only (see
     "Settings that are not in the Shift Editor" below): logout at shift end, which is on, and the
     grace minutes, which are 0

## Cutover (in a quiet window)

6. Edit `LEGACY=` at the top of both scripts to the name from step 1, then run
   **`Switch_To_Shift.cmd` as Administrator**. If the shift service fails to start, the script
   switches back to the current service by itself. The scripts wait up to 2 minutes for a service to
   stop or start; if one doesn't stop in that time the script stops there and changes nothing else.
7. **Smoke test** on a pilot machine *and* an unassigned machine:
   - Pilot machine during a shift: login (green); scanning again or at another station is refused
     (orange) - operators are held to the shift; a CanLogoutShift operator can log out
   - Add a break a few minutes ahead in the Shift Editor: at that time everyone on the shift is logged
     out (red); log in again
   - At the shift end everyone on it is logged out
   - Unassigned machine: login, logout and moving work as they always have
   - Restart the shift service during a shift and check logins are kept and still held
   - Changes made in the Shift Editor reach the service within about a minute - no restart needed

## Support tool: Card Reader Monitor

`CardReaderMonitor.exe` is a single file with nothing to install (it includes the .NET runtime it needs).
Copy it to the server that runs the shift service - for example to
`C:\Program Files (x86)\Jensen\CardReaderMonitor\` - and run it there. It shows each card reader's
connection, LED colour and logged-in operator as the running shift service sees them, and changes
nothing. It only works on the same machine as the service, and only with the shift service (the
current service doesn't publish reader status).

## Rollback (about 1 minute)

Run **`Rollback_To_Legacy.cmd` as Administrator**. It stops and disables the shift service, then
re-enables and starts the current one. No installer, no database change. Operators may need to
re-scan. The shift tables stay in `JEGR_Utils` and are ignored.

The current (non-shift) service brings its card readers online slowly after a start - the last ones
after about a minute and a half on the test system - so allow a couple of minutes after a rollback
before judging that a reader isn't working. The shift service brings them online within seconds.

## Settings that are not in the Shift Editor

Kept out of the editor on purpose (agreed with Disney, October 2026). Both live in `JEGR_Utils`, are
read by the service about once a minute, and should only be changed by Ge-Mac support.

- **Logout at shift end** - on. To switch it off (operators then stay logged in at shift end and are
  carried into the following shift):
  `UPDATE JEGR_Utils.dbo.tblShiftSettings SET SettingValue = 'False', ModifiedDate = GETDATE() WHERE SettingName = 'AutoLogoutAtShiftEnd'`
  (`'True'` switches it back on; a missing row also counts as on). The service logs
  "[AutoLogout] Logout at shift end is OFF" / "ON" in the Application event log when it sees the change.
- **Grace minutes (Delta)** - 0 on every shift, so operators can't log out or move during a shift.
  To allow it for some minutes before and after a shift's end:
  `UPDATE JEGR_Utils.dbo.tblShifts SET DeltaMinus = 5, DeltaPlus = 5 WHERE ShiftName = '...'`

Breaks, the shift start rule and the hold during a shift don't depend on either setting.

## Later

- **Installing a newer shift build during the pilot:** back up the `_Shift` config, uninstall
  "CentralLoginServiceSetup_Shift" in Programs and Features, install the new MSI, restore the config.
  Don't install over the top.
- **Removing shift management permanently:** run the rollback, uninstall "CentralLoginServiceSetup_Shift",
  then optionally back up `JEGR_Utils` and run `rollback_schema.sql` (deletes all shift data).
- **Never** uninstall or reinstall the current non-shift product - uninstall deletes its site-edited config.
