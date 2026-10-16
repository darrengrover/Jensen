# Shift Editor - UI test checklist

About 20 minutes, on a **test** database, for Shift Editor 1.0.2 (break times; no Delta columns;
no Shift Settings tab).

**Setup:** run `CentralOperatorShiftEditor.exe` from a folder whose `DBConnections.xml` points at the
test database. The database must have had the current `schema.sql` run on it (it adds `tblShiftBreaks`).
Test shifts are named `ZZTEST-UI ...`. The steps use machines **2115** and **2122** - use any two
machines with no other shift assignments instead, and substitute their numbers.

Tick each box if the result matches; note anything different.

## What is and isn't on screen

| # | Do this | Expected | OK? |
|---|---|---|---|
| E1 | Start the editor | Opens without errors. Two tabs only: **Shifts** and **Operator Settings** - there is no Shift Settings tab | ☐ |
| E2 | Look at the Shifts grid | Columns: Shift ID, Shift Name, Start Time, End Time, Active. **No Delta columns.** Nothing on any screen mentions Delta, grace minutes or auto-logout | ☐ |
| E3 | Look at the lower half of the Shifts tab | Two tabs: **Machines** and **Breaks** | ☐ |

## Shifts and machine assignments

| # | Do this | Expected | OK? |
|---|---|---|---|
| E4 | **Add Shift**, name `ZZTEST-UI A`, 08:00-10:00, **Save** | "Shifts saved successfully"; A is in the list, still selected, after the save | ☐ |
| E5 | Select A, Machines tab, **Add Machines...**: machine 2115, Monday + Tuesday, **Add to Shift**, **Save Assignments** | "Assignments saved successfully"; 2 assignments shown | ☐ |
| E6 | Add shift `ZZTEST-UI B` 09:00-11:00, Save. Select B, Add Machines: 2115, Monday, Save Assignments | **Refused** - "Overlap Conflicts Detected" naming A. Nothing saved (Refresh: B has 0 assignments) | ☐ |
| E7 | Add shift `ZZTEST-UI E` 10:00-12:00, Save. Select E, Add Machines: 2115, Monday, Save Assignments | Saved (10:00 end/start touch but don't overlap) | ☐ |
| E8 | Edit E's Start Time to 09:30, **Save** | **Refused** - "Cannot save shifts: the new times would overlap..." naming A on Monday. Refresh: E still starts 10:00 | ☐ |
| E9 | Add shifts `ZZTEST-UI C` 13:00-15:00 and `ZZTEST-UI D` 14:00-16:00, Save. Select C, Add Machines: 2122, Wednesday (**don't save**). Select D, Add Machines: 2122, Wednesday, **Save Assignments** | **Refused** - overlap between D and C's unsaved assignment on 2122 Wednesday. Then **Refresh** to discard | ☐ |
| E10 | Select B, clear its name (blank), **Save** | **"Save Failed"** message giving the likely cause (blank name); list reloaded; B still has its name | ☐ |
| E11 | Select B, type `9` as its Start Time, **Save** | **Refused** - "the start or end time is not a time of day". **Refresh** (Yes) to put 09:00 back | ☐ |

## Break times

| # | Do this | Expected | OK? |
|---|---|---|---|
| B1 | Select A, **Breaks** tab | Empty list, "0 breaks" | ☐ |
| B2 | **Add Break** | A row appears with a suggested time (09:00), ready to type; status bar shows unsaved changes | ☐ |
| B3 | Type `09:15`, Enter, **Save Breaks** | "Break times saved successfully"; 09:15 listed, "1 break"; still there after **Refresh** | ☐ |
| B4 | Add Break, type `10:00` (the shift's end), Enter, **Save Breaks** | **Refused** - "10:00 is not between the shift's start (08:00) and end (10:00)". Nothing saved | ☐ |
| B5 | Change that row to `09:15`, **Save Breaks** | **Refused** - "09:15 is entered more than once" | ☐ |
| B6 | Double-click that row, type `abc`, Enter | Red border, stays in edit; **Esc** puts the time back | ☐ |
| B7 | Change that row to `0830` (no colon), Enter, **Save Breaks** | Accepted as 08:30 and saved; list shows 08:30 then 09:15 | ☐ |
| B8 | Add Break (**don't save**), then click shift B in the top grid | Asked whether to discard the unsaved break times. **No**: A stays selected with the unsaved row. Click B again, **Yes**: B shown. Back on A: only 08:30 and 09:15 | ☐ |
| B9 | On A, Add Break (don't save), then click **Save** on the Shifts toolbar | "Save or refresh the break times before saving shifts". Click **Refresh** on the Breaks tab (Yes) | ☐ |
| B10 | Edit A's End Time to 09:00, **Save** | **Refused** - A "would leave its break at 09:15 outside the shift". **Refresh** shifts to put 10:00 back | ☐ |
| B11 | On A, select 08:30, **Remove Selected**, **Save Breaks** | Saved; only 09:15 left after Refresh | ☐ |
| B12 | Select A, **Delete Shift** | Prompt says "Delete selected shift(s) and their 2 machine assignment(s) and 1 break time(s)?"; Yes. A disappears at once (no Save needed); Refresh confirms it's gone | ☐ |

## Operator settings and clean-up

| # | Do this | Expected | OK? |
|---|---|---|---|
| O1 | Operator Settings: **Add Setting**, choose operator 1, tick Can Logout During Shift, **Save** | "Operator settings saved successfully"; row stays after Refresh | ☐ |
| O2 | Select that row, **Delete Setting**, Yes | Row gone at once (no Save needed); Refresh confirms | ☐ |
| O3 | **Clean up:** delete shifts B, C, D, E (each with its assignments) | No `ZZTEST-UI` shifts left after Refresh | ☐ |
