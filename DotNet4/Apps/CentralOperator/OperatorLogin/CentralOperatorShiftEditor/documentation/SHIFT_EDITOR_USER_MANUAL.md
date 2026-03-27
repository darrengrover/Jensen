# Shift Editor User Manual

**Version:** 1.0
**Last Updated:** October 2025
**Application:** Central Operator Shift Editor

---

## Table of Contents

1. [Introduction](#introduction)
2. [Getting Started](#getting-started)
3. [Managing Shifts](#managing-shifts)
4. [Assigning Machines to Shifts](#assigning-machines-to-shifts)
5. [Configuring Operator Permissions](#configuring-operator-permissions)
6. [Global Shift Settings](#global-shift-settings)
7. [Tips & Best Practices](#tips--best-practices)
8. [Troubleshooting](#troubleshooting)

---

## Introduction

### What is the Shift Editor?

The Shift Editor is a configuration tool that allows you to define work shifts, assign machines to those shifts, and configure operator permissions. This determines when operators can log in and out of machines throughout the facility.

### Who Should Use This Application?

- Facility Managers
- Shift Supervisors
- Production Coordinators
- System Administrators

### Key Capabilities

- Create and manage work shifts with specific start and end times
- Assign multiple machines to shifts for different days of the week
- Configure grace periods (Delta) for shift transitions
- Set operator-level permissions for special access privileges
- Configure global shift management settings

---

## Getting Started

### Opening the Application

Launch the **Central Operator Shift Editor** from your desktop or start menu.

### Understanding the Interface

The application has three main tabs:

1. **Shifts** - Create and manage shifts, assign machines
2. **Shift Settings** - View and configure global shift management settings
3. **Operator Settings** - Manage operator-specific permissions

### Status Bar

At the bottom of the window:
- **Status** - Shows "Ready" or "Modified"
- **Unsaved Changes Warning** - Alerts you when changes haven't been saved
- **Window Title** - An asterisk (*) appears when you have unsaved changes

---

## Managing Shifts

### Understanding Shifts

A **shift** is a defined time period when operators work on specific machines. Each shift has:

- **Shift Name** - A descriptive name (e.g., "Day Shift", "Night Shift")
- **Start Time** - When the shift begins (e.g., 08:00)
- **End Time** - When the shift ends (e.g., 16:00)
- **Delta - (DeltaMinus)** - Grace period in minutes BEFORE shift end for early logout
- **Delta + (DeltaPlus)** - Grace period in minutes AFTER shift end for late logout
- **Active Status** - Whether the shift is currently enabled

### Understanding Delta Periods

**Delta periods** provide flexibility around shift end times:

- **Delta -** allows operators to manually logout up to X minutes BEFORE their shift officially ends
- **Delta +** provides a grace period AFTER the shift ends before the system transitions to the next shift

**Example:**
- Shift ends at 14:00
- Delta - = 15 minutes
- Delta + = 10 minutes
- Operators can manually logout between 13:45 and 14:10

### Creating a New Shift

1. Go to the **Shifts** tab
2. Click the **Add Shift** button in the toolbar
3. A new row appears in the grid with default values
4. Fill in the shift details:
   - Click on **Shift Name** and type a descriptive name
   - Click on **Start Time** and enter time in HH:mm format (e.g., 06:00)
   - Click on **End Time** and enter time in HH:mm format (e.g., 14:00)
   - Adjust **Delta -** using the +/- buttons or type a value (in minutes)
   - Adjust **Delta +** using the +/- buttons or type a value (in minutes)
   - Check or uncheck the **Active** checkbox
5. Click the **Save** button
6. A success message confirms the shift has been created

### Editing an Existing Shift

1. Go to the **Shifts** tab
2. Click on any field in the shift row you want to edit
3. Make your changes
4. Click the **Save** button
5. Confirm the success message

### Deleting a Shift

1. Go to the **Shifts** tab
2. Click on the shift row you want to delete (it will be highlighted)
3. Click the **Delete Shift** button
4. A confirmation dialog appears
5. Click **Yes** to confirm deletion
6. Click the **Save** button
7. The shift and all its machine assignments are removed

### Special Case: Midnight-Crossing Shifts

Some shifts span midnight (e.g., 22:00 to 06:00 the next day). The system automatically handles these shifts. Simply enter the times as you would normally:

- Start Time: 22:00
- End Time: 06:00

The system understands that 06:00 is the next day.

---

## Assigning Machines to Shifts

### Understanding Machine Assignments

Once you create a shift, you must assign machines to it. This tells the system which machines operate on which shifts, and on which days of the week.

### Adding Machines to a Shift

1. Go to the **Shifts** tab
2. Click on a shift in the upper grid to select it
3. The lower panel shows current machine assignments for that shift
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
2. In the lower panel, click on one or more machine assignment rows
3. Click the **Remove Selected** button
4. Click **Save Assignments**
5. Confirm the success message

### Important Notes About Machine Assignments

- **No Overlaps**: You cannot assign overlapping shifts to the same machine on the same day
  - Example: If Machine 100 already has Shift A (06:00-14:00) on Monday, you cannot assign Shift B (12:00-20:00) on Monday
  - The system will display an error if you try

- **Multiple Machines**: You can assign the same shift to many machines at once

- **Day-Specific**: Each machine assignment is tied to specific days of the week

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
   - Allows this operator to logout at any time, even outside delta periods
   - Bypasses normal shift logout restrictions
   - Useful for supervisors or maintenance personnel who need flexibility

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
4. Confirm deletion when prompted
5. Click the **Save** button

### Important Notes

- **One Entry Per Operator**: Each operator can only have one permission entry
  - If you try to add the same operator twice, you'll get an error
  - To change permissions, edit the existing entry

- **Must Select Operator**: You must select an operator from the dropdown before saving

---

## Global Shift Settings

### Understanding Shift Settings

The Shift Settings tab displays system-wide configuration options that control how shift management works across the entire facility.

### Viewing Shift Settings

1. Go to the **Shift Settings** tab
2. You'll see a list of settings with:
   - **Setting Name** - What the setting controls
   - **Value** - Current value (usually "true" or "false")
   - **Description** - What the setting does

### Key Settings

1. **UseShiftManagement**
   - **Purpose**: Master on/off switch for the entire shift management system
   - **true** = Shift management is active
   - **false** = Shift management is disabled (legacy mode)

2. **AutoLogoutAtShiftEnd**
   - **Purpose**: Controls whether operators are automatically logged out when their shift ends
   - **true** = Operators are automatically logged out at shift end time
   - **false** = Operators must manually logout (can stay past shift end until delta+ expires)

### Modifying Settings

1. Go to the **Shift Settings** tab
2. Click on the **Value** field for the setting you want to change
3. Type the new value (typically "true" or "false")
4. Click the **Save** button
5. **IMPORTANT**: Some settings require a service restart to take effect

### When to Change Settings

- **Disable shift management temporarily**: Set UseShiftManagement to "false"
- **Allow operators to stay past shift end**: Set AutoLogoutAtShiftEnd to "false"
- **Enforce strict shift times**: Set AutoLogoutAtShiftEnd to "true"

---

## Tips & Best Practices

### Shift Configuration

- **Use descriptive names**: "Morning Shift", "Afternoon Shift", "Night Shift" are clearer than "Shift 1", "Shift 2"

- **Standard delta values**: Most facilities use 5-15 minutes for both Delta - and Delta +
  - Too small (0-2 minutes): Operators may not have enough time to logout
  - Too large (30+ minutes): Delays shift transitions and can cause confusion

- **Plan for shift transitions**:
  - If shifts are back-to-back (e.g., 06:00-14:00 and 14:00-22:00), understand that operators from the first shift may stay logged in until 14:00 + Delta +
  - Leave gaps between shifts if you want clean transitions

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

#### Changes aren't saving

**Problem**: You click Save but nothing happens, or you get an error.

**Solution**:
1. Check that all required fields are filled in correctly
   - Shift times must be in HH:mm format (e.g., 08:00, not 8:00)
   - Delta values must be numbers
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

#### Midnight-crossing shifts not working correctly

**Problem**: Your overnight shift (e.g., 22:00-06:00) isn't behaving as expected.

**Solution**:
1. Verify the times are entered correctly (Start Time = 22:00, End Time = 06:00)
2. The system automatically detects midnight crossing when Start Time > End Time
3. Remember that 06:00 is the NEXT day, not the same day
4. Ensure Delta values are reasonable (not too large)

#### Can't delete a shift

**Problem**: The Delete button is disabled or deletion fails.

**Solution**:
1. Ensure you've selected a shift row (click on it)
2. Some shifts may have dependencies (check if operators are currently logged in on this shift)
3. Try removing all machine assignments first, then delete the shift
4. Refresh the data and try again

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
- Changes take effect in real-time on the operational system

### Related Documentation

- **Complete Shift Logic Specification** - Technical documentation for developers and system administrators
- Contact your system administrator for additional technical details

---

**End of User Manual**
