# Shift Editor User Manual

**Version:** 1.1
**Last Updated:** October 2026
**Application:** Central Operator Shift Editor (1.0.2)

---

## Table of Contents

1. [Introduction](#introduction)
2. [How Shifts Affect Operators](#how-shifts-affect-operators)
3. [Getting Started](#getting-started)
4. [Managing Shifts](#managing-shifts)
5. [Assigning Machines to Shifts](#assigning-machines-to-shifts)
6. [Setting Break Times](#setting-break-times)
7. [Configuring Operator Permissions](#configuring-operator-permissions)
8. [Tips & Best Practices](#tips--best-practices)
9. [Troubleshooting](#troubleshooting)

---

## Introduction

### What is the Shift Editor?

The Shift Editor is a configuration tool that allows you to define work shifts, assign machines to those shifts, set break times and configure operator permissions. This determines when operators can log in and out of machines throughout the facility.

### Who Should Use This Application?

- Facility Managers
- Shift Supervisors
- Production Coordinators
- System Administrators

### Key Capabilities

- Create and manage work shifts with specific start and end times
- Assign multiple machines to shifts for different days of the week
- Set break start times for each shift
- Set operator-level permissions for special access privileges

---

## How Shifts Affect Operators

While a shift is running on a machine, the card readers on that machine follow these rules:

- **Logging in** during the shift puts the operator on that shift.
- **Operators stay logged in for the shift.** Scanning again at their own station does not log them
  out, and scanning at another station does not move them - the reader flashes orange and nothing
  changes. (Operators with special permissions are the exception - see
  [Configuring Operator Permissions](#configuring-operator-permissions).)
- **At each break start time** everyone logged in on the shift is logged out automatically. When they
  come back they log in again - at the same station or a different one.
- **At the end of the shift** everyone still logged in on it is logged out automatically.
- **When a shift starts**, anyone already logged in on the machine from before the shift is logged
  out and logs in again to join the shift. An operator who scans in a few seconds *before* the shift
  starts will be logged out at the start and has to scan again.

A machine with no shift running - a machine that isn't assigned to any shift, or an assigned machine
between its shifts - works exactly as it did before shift management: operators log in and out
freely.

---

## Getting Started

### Opening the Application

Launch the **Central Operator Shift Editor** from your desktop or start menu.

### Understanding the Interface

The application has two main tabs:

1. **Shifts** - Create and manage shifts. The lower half of this tab shows the selected shift's
   **Machines** and **Breaks**
2. **Operator Settings** - Manage operator-specific permissions

### Status Bar

At the bottom of the window:
- **Status** - Shows "Ready" or "Modified"
- **Unsaved Changes Warning** - Alerts you when changes haven't been saved
- **Window Title** - An asterisk (*) appears when you have unsaved changes

### When Changes Take Effect

Saved changes to shifts, machine assignments, break times and operator permissions are picked up by
the card-reader service within about a minute. There is no need to restart anything.

---

## Managing Shifts

### Understanding Shifts

A **shift** is a defined time period when operators work on specific machines. Each shift has:

- **Shift Name** - A descriptive name (e.g., "Day Shift", "Night Shift")
- **Start Time** - When the shift begins (e.g., 08:00)
- **End Time** - When the shift ends (e.g., 16:00)
- **Active Status** - Whether the shift is currently enabled

### Creating a New Shift

1. Go to the **Shifts** tab
2. Click the **Add Shift** button in the toolbar
3. A new row appears in the grid with default values
4. Fill in the shift details:
   - Click on **Shift Name** and type a descriptive name
   - Click on **Start Time** and enter time in HH:mm format (e.g., 06:00)
   - Click on **End Time** and enter time in HH:mm format (e.g., 14:00)
   - Check or uncheck the **Active** checkbox
5. Click the **Save** button
6. A success message confirms the shift has been created

Always type times with a colon (06:00, not 6). If a time isn't a valid time of day, Save tells you
which shift to correct and nothing is saved.

### Editing an Existing Shift

1. Go to the **Shifts** tab
2. Click on any field in the shift row you want to edit
3. Make your changes
4. Click the **Save** button
5. Confirm the success message

If the shift has break times, its new start and end must still contain them. If a break would end
up outside the shift, Save tells you which one - change or remove that break first (Breaks tab), then
save the shift.

**Editing a shift that is running:** the change reaches the card readers within about a minute.
Moving the end time earlier than the current time logs out everyone on the shift.

### Deleting a Shift

1. Go to the **Shifts** tab
2. Click on the shift row you want to delete (it will be highlighted)
3. Click the **Delete Shift** button
4. A confirmation dialog appears - if the shift has machine assignments or break times it tells you how many will be deleted with it
5. Click **Yes** to confirm deletion
6. The shift, its machine assignments and its break times are deleted from the database straight away - there is no need to click **Save**

If you have unsaved changes to machine assignments or break times, you'll be asked to save or refresh them first.

### Special Case: Midnight-Crossing Shifts

Some shifts span midnight (e.g., 22:00 to 06:00 the next day). Enter the times as you would normally:

- Start Time: 22:00
- End Time: 06:00

The system understands that 06:00 is the next day.

**Important:** machine assignments are matched on the *current* day of the week. For the part of an
overnight shift after midnight to apply, assign the shift to **both** days it touches - e.g. a Monday
22:00-06:00 shift must also be assigned to Tuesday. Without that, from midnight to 06:00 the machine
behaves as if it has no shift.

---

## Assigning Machines to Shifts

### Understanding Machine Assignments

Once you create a shift, you must assign machines to it. This tells the system which machines operate on which shifts, and on which days of the week.

### Adding Machines to a Shift

1. Go to the **Shifts** tab
2. Click on a shift in the upper grid to select it
3. In the lower panel, the **Machines** tab shows current machine assignments for that shift
4. Click the **Add Machines...** button
5. The **Add Machines** dialog opens

#### In the Add Machines Dialog:

6. **Search for machines** (optional):
   - Type in the search box to filter the machine list
   - Example: Type "100" to find Machine 100

7. **Select machines**:
   - Check the boxes next to the machines you want to assign
   - You can select multiple machines at once
   - The summary shows "X machine(s) selected"

8. **Select days of the week**:
   - Check individual days (Sunday through Saturday)
   - OR use quick-select buttons:
     - **All Days** - Select all 7 days
     - **Weekdays** - Select Monday through Friday
     - **Weekends** - Select Saturday and Sunday

9. **Review your selection**:
   - Summary shows: "5 machines × 2 days = 10 assignments"

10. Click **Add to Shift**

11. New assignments appear in the lower panel (highlighted in yellow)

12. Click **Save Assignments** to finalize

13. A success message confirms the assignments have been saved

### Removing Machine Assignments

1. Select the shift from the upper grid
2. In the lower panel (**Machines** tab), click on one or more machine assignment rows
3. Click the **Remove Selected** button and confirm
4. The assignments are deleted from the database straight away - there is no need to click **Save Assignments**
5. Confirm the success message

### Important Notes About Machine Assignments

- **No Overlaps**: You cannot assign overlapping shifts to the same machine on the same day
  - Example: If Machine 100 already has Shift A (06:00-14:00) on Monday, you cannot assign Shift B (12:00-20:00) on Monday
  - The system will display an error if you try - including when you change the times of a shift that is already assigned

- **Machines without assignments** work exactly as before shift management - operators log in and out freely.
  The same applies on an assigned machine at times when none of its shifts is running.

- **Adding a machine to a shift that is already running** logs out everyone on that machine within
  about a minute (they were logged in from before the shift, as far as the system is concerned). They
  log in again to join the shift. Where possible, add machines before the shift starts.

- **Multiple Machines**: You can assign the same shift to many machines at once

- **Day-Specific**: Each machine assignment is tied to specific days of the week

---

## Setting Break Times

### Understanding Breaks

A shift can have any number of **break start times**. When a break start time is reached, everyone
logged in on that shift is logged out - on every machine assigned to the shift, including operators
with special permissions. The card reader LEDs turn red.

A break has no end time. When operators come back they log in again by scanning their card, at the
same station or at a different one. Someone who logs in again straight away stays logged in - the
same break does not log them out a second time.

A shift with no break times works as before: operators stay logged in until the end of the shift.

### Adding a Break

1. Go to the **Shifts** tab
2. Click on a shift in the upper grid to select it (a new shift must be saved first)
3. In the lower panel, click the **Breaks** tab
4. Click **Add Break** - a new row appears with a suggested time, ready for typing
5. Type the break start time in HH:mm format (e.g., 10:00) and press Enter
6. Repeat for further breaks
7. Click **Save Breaks**
8. A success message confirms the break times have been saved

### Changing or Removing a Break

- **To change a time:** double-click the time, type the new one, press Enter, then click **Save Breaks**
- **To remove breaks:** click one or more rows, click **Remove Selected**, then click **Save Breaks**

Nothing on the Breaks tab is written to the database until you click **Save Breaks**. **Refresh**
reloads the saved break times and drops unsaved changes. If you select a different shift while
break changes are unsaved, you are asked whether to discard them.

### Rules for Break Times

- A break must fall **between** the shift's start and end times - not at the start or end itself.
  For an overnight shift (e.g. 22:00-06:00) that means after 22:00 or before 06:00
- Each time can appear only once per shift
- Times are in whole minutes

If a rule is broken, **Save Breaks** tells you which time to correct and nothing is saved.

### When Break Changes Take Effect

Break times are picked up within about a minute of **Save Breaks**, including on a shift that is
running. A break time that is added or changed to a time up to five minutes in the past still logs
operators out; anything further in the past does nothing until the next day.

---

## Configuring Operator Permissions

### Understanding Operator Permissions

Some operators may need special privileges that bypass normal shift rules. The Operator Settings tab allows you to grant these permissions.

### Available Permissions

1. **Can Replace Operator**
   - Allows this operator to replace other operators at any station
   - Useful for supervisors or relief operators
   - When they scan at an occupied station, all other operators are logged out

2. **Can Logout During Shift**
   - Allows this operator to logout or move to another station at any time during a shift
   - Bypasses normal shift logout restrictions
   - Useful for supervisors or maintenance personnel who need flexibility

Neither permission changes breaks or the end of the shift: operators with these permissions are
logged out at break start times and at shift end like everyone else.

### Adding Operator Permissions

1. Go to the **Operator Settings** tab
2. Click the **Add Setting** button
3. A new row appears in the grid
4. Click on the **Operator** dropdown and select an operator
   - The dropdown shows: Operator ID - Operator Name
5. Check or uncheck the permission checkboxes:
   - **Can Replace Operator**
   - **Can Logout During Shift**
6. Click the **Save** button
7. A success message confirms the settings have been saved

### Editing Operator Permissions

1. Go to the **Operator Settings** tab
2. Click on the operator's row
3. Change the permission checkboxes as needed
4. Click the **Save** button
5. Confirm the success message

### Deleting Operator Permissions

1. Go to the **Operator Settings** tab
2. Click on the operator's row to select it
3. Click the **Delete Setting** button
4. Confirm deletion when prompted - the setting is deleted from the database straight away (no need to click **Save**)

### Important Notes

- **One Entry Per Operator**: Each operator can only have one permission entry
  - If you try to add the same operator twice, you'll get an error
  - To change permissions, edit the existing entry

- **Must Select Operator**: You must select an operator from the dropdown before saving

---

## Tips & Best Practices

### Shift Configuration

- **Use descriptive names**: "Morning Shift", "Afternoon Shift", "Night Shift" are clearer than "Shift 1", "Shift 2"

- **Plan for shift changeovers**:
  - If shifts are back-to-back (e.g., 06:00-14:00 and 14:00-22:00), the first shift's operators are logged out at 14:00 and the next shift's operators log in from 14:00
  - Incoming operators should scan in once their shift has started. A scan in the last seconds of the previous shift (or of a gap before the shift) is logged out when the new shift starts

### Break Times

- **Enter the time people leave their stations**, not the time they come back - returning operators log themselves in

- **Tell operators what to expect**: at a break the LEDs turn red and they scan in again when they return. A scan "to log out" just after the break time actually logs them back in

- **Check breaks after changing shift times**: a break must stay inside the shift

### Machine Assignment

- **Assign by production area**: Select all machines in one area and assign them together

- **Use day-specific assignments**: Weekend shifts often differ from weekday shifts

- **Check for conflicts**: Before adding assignments, verify the machine doesn't already have an overlapping shift on that day

### Operator Permissions

- **Limit special permissions**: Only grant "Can Replace Operator" and "Can Logout During Shift" to supervisors and leads who truly need them

- **Document who has permissions**: Keep a list of operators with special permissions for reference

### General Workflow

- **Save frequently**: Click Save after each set of changes

- **Test changes**: After configuring shifts, test with one machine before rolling out to all machines

- **Refresh to see latest data**: Click the Refresh button to reload data from the database if multiple people are using the system

---

## Troubleshooting

### Common Issues and Solutions

#### "Overlapping shift assignment detected"

**Problem**: You're trying to assign a shift to a machine/day that already has a conflicting shift.

**Solution**:
1. Check the machine's existing assignments for that day
2. Remove the conflicting assignment or choose a different time
3. Ensure shift times don't overlap (e.g., 06:00-14:00 and 12:00-20:00 overlap)

#### "Operator already has a setting entry"

**Problem**: You're trying to add permissions for an operator who already has an entry.

**Solution**:
1. Go to the Operator Settings tab
2. Find the existing entry for that operator
3. Edit the existing entry instead of creating a new one
4. Or delete the existing entry first, then create a new one

#### "You must select an operator"

**Problem**: You tried to save an operator permission without selecting an operator from the dropdown.

**Solution**:
1. Click on the Operator dropdown in the row
2. Select an operator from the list
3. Then click Save

#### A break time has a red border and won't accept what I typed

**Problem**: The time isn't in a form the editor understands.

**Solution**:
1. Type the time as HH:mm, for example 10:00 or 14:30
2. Press Enter to accept it, or Esc to go back to the previous time

#### "Cannot save the break times"

**Problem**: A break is outside the shift, or the same time is entered twice.

**Solution**:
1. Read which time the message names
2. Correct or remove it - a break must be after the shift's start and before its end
3. Click **Save Breaks** again

#### "Save or refresh the break times before..."

**Problem**: You tried to save, refresh or delete shifts while break changes were unsaved.

**Solution**:
1. Go to the **Breaks** tab in the lower panel
2. Click **Save Breaks** to keep the changes, or **Refresh** to drop them
3. Repeat what you were doing

#### Operators were logged out unexpectedly

**Problem**: Operators on a shift machine were logged out and nobody scanned.

**Solution**:
1. Check the shift's **Breaks** tab - a break start time logs out everyone on the shift
2. Check the shift's end time - everyone is logged out when the shift ends
3. Check whether the machine was added to a shift, or a shift's times were changed, while people were logged in
4. Check the fixed-time auto-logout in the service configuration (see the note under overnight shifts below)

#### An operator can't log out or move during a shift

**Problem**: The reader flashes orange and the operator stays logged in where they were.

**Solution**:
1. This is the normal rule during a shift - operators stay logged in until a break or the end of the shift
2. If this operator needs to move around or leave during shifts, give them **Can Logout During Shift** on the Operator Settings tab

#### Changes aren't saving

**Problem**: You click Save but nothing happens, or you get an error.

**Solution**:
1. Check that all required fields are filled in correctly
   - Shift times must be in HH:mm format (e.g., 08:00)
   - Operators must be selected from the dropdown
2. Look for validation error messages on screen
3. Try clicking Refresh to reload data, then make changes again
4. Close and reopen the application

#### Lost unsaved changes

**Problem**: You closed the window or clicked Refresh and lost your changes.

**Solution**:
1. Watch for the asterisk (*) in the window title - it indicates unsaved changes
2. Always click Save before:
   - Clicking Refresh
   - Closing the window
   - Switching to another application for a long time
3. The status bar shows "Modified" and displays a warning when you have unsaved changes
4. Unsaved machine assignments for a shift are dropped when you select a different shift - click **Save Assignments** first

#### Midnight-crossing shifts not working correctly

**Problem**: Your overnight shift (e.g., 22:00-06:00) isn't behaving as expected.

**Solution**:
1. Verify the times are entered correctly (Start Time = 22:00, End Time = 06:00)
2. The system automatically detects midnight crossing when Start Time > End Time
3. Remember that 06:00 is the NEXT day, not the same day
4. Check the shift is assigned to **both** days it touches (e.g. Monday and Tuesday)
5. Check the fixed-time auto-logout in the service configuration (`AutoLogout` / `AutoLogoutTime`,
   default 23:15) - it still runs with shift management and logs everyone out at that time.
   Ask your system administrator to switch it off or move it if you run night shifts

#### "Save Failed"

**Problem**: A "Save Failed" message says the database rejected a change.

**Solution**:
1. Read the likely cause in the message (e.g. an overlapping assignment, a blank shift name)
2. The list has been reloaded from the database - check which changes were kept
3. Correct the problem and make the remaining changes again

#### Can't delete a shift

**Problem**: The Delete button is disabled or deletion fails.

**Solution**:
1. Ensure you've selected a shift row (click on it)
2. If you have unsaved machine assignment or break changes, save or refresh them first
3. Refresh the data and try again

#### Multiple users making changes

**Problem**: Two people are editing shifts at the same time and changes conflict.

**Solution**:
1. Coordinate shift configuration changes
2. Click Refresh before making changes to see the latest data
3. Save immediately after making changes
4. Communicate with other users when making large-scale changes

---

## Additional Resources

### Getting Help

If you encounter issues not covered in this manual:

1. Check the status bar for error messages
2. Note the exact error message text
3. Check whether you have unsaved changes (asterisk in title bar)
4. Try refreshing the data
5. Contact your system administrator with:
   - The exact steps you took
   - The error message received
   - Screenshots if possible

### System Requirements

- This application requires the Central Operator system to be installed
- Database connectivity is required
- Saved changes take effect on the operational system within about a minute

### Related Documentation

- **Complete Shift Logic Specification** - Technical documentation for developers and system administrators
- Contact your system administrator for additional technical details

---

**End of User Manual**
