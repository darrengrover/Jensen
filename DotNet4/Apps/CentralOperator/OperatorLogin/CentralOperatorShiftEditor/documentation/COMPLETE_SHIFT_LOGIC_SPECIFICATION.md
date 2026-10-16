# COMPLETE SHIFT MANAGEMENT LOGIC SPECIFICATION

**Version:** 1.1
**Last Updated:** 2026-10-09
**Status:** Comprehensive Specification
**Applies to:** CentralLoginService 1.3.0, TPL.TagReaders 1.0.4, Shift Editor 1.0.2

### What changed in version 1.1 (October 2026)

Agreed by Ge-Mac and Disney on 1 October 2026:

- **Logout at shift end is always on** unless switched off by SQL. `AutoLogoutAtShiftEnd` still exists,
  but a missing row counts as on and the setting is not shown in the Shift Editor.
- **Break times.** A shift can have any number of break start times. At each one everyone logged in on
  the shift is logged out (Rule TM3). Logging back in is up to the operator.
- **Delta (the grace minutes) defaults to 0** and is not shown in the Shift Editor. The delta rules in
  this document still work for a shift whose values are set by SQL, but with 0 an operator without
  permissions cannot log out or move during a shift - they leave at a break or at the shift end.
- **Shift start rule (Rule TM0).** An operator logged in without a shift is logged out when a shift
  starts on their machine, whatever the `AutoLogoutAtShiftEnd` setting. The old Rule TM2.2 (give such
  an operator the shift and keep them logged in) is gone.
- **Shift data is re-read from the database once a minute**, so Shift Editor changes reach the service
  within about a minute without a restart.

---

## Table of Contents

1. [Core Concepts](#core-concepts)
2. [Global Settings](#global-settings-and-prerequisites)
3. [ON SERVICE START Logic](#on-service-start-logic)
4. [EVERY MINUTE Timer Logic](#every-minute-timer-logic)
5. [AT CARD SCAN Logic](#at-card-scan-logic)
6. [Complete Decision Trees](#complete-decision-trees)
7. [Edge Cases and Special Scenarios](#edge-cases-and-special-scenarios)
8. [Examples and Walkthroughs](#examples-and-walkthroughs)

---

## Core Concepts

### Shifts
A **shift** is a time period when operators work on specific machines. Each shift has:
- **Start Time** (TimeSpan): e.g., 06:00:00
- **End Time** (TimeSpan): e.g., 14:00:00
- **DeltaMinus** (minutes): Grace period BEFORE shift end for manual logout (normally 0)
- **DeltaPlus** (minutes): Grace period AFTER shift end for manual logout and shift transitions (normally 0)
- **Break Times**: Zero or more break start times (times of day inside the shift)
- **Machine Assignments**: Which machines use this shift

**IMPORTANT**: Shifts are defined by TIME ONLY, not dates. A shift cannot span multiple days (e.g., no 48-hour shifts). Times are compared against server time.

### Delta Periods

> **Since version 1.1** both delta values default to 0 and the Shift Editor neither shows nor sets
> them, so normally there is **no** delta window: an operator without permissions is held to the shift
> until a break or the shift end. Everything below still applies to a shift whose `DeltaMinus` /
> `DeltaPlus` have been set by SQL (`UPDATE JEGR_Utils.dbo.tblShifts SET DeltaMinus = ..., DeltaPlus = ...`).

The **delta period** is a time window around shift end when manual logout is permitted. Each shift has **two separate delta values**:

- **DeltaMinus** (Before shift end): From `(ShiftEnd - DeltaMinus)` to `ShiftEnd`
- **DeltaPlus** (After shift end): From `ShiftEnd` to `(ShiftEnd + DeltaPlus)`
- **Combined Delta Window**: From `(ShiftEnd - DeltaMinus)` to `(ShiftEnd + DeltaPlus)`

**Example**: Shift ends at 14:00 with DeltaMinus = 15 minutes, DeltaPlus = 10 minutes
- Delta- period: 13:45 - 14:00 (can manually logout)
- Delta+ period: 14:00 - 14:10 (can manually logout, grace before transition)
- Combined: 13:45 - 14:10 (full manual logout window)

**Note**: DeltaMinus and DeltaPlus can have different values to allow flexible logout windows.

### Critical Timing Distinctions

Using the example: Shift ends at 14:00, DeltaMinus = 15 min, DeltaPlus = 10 min

1. **Delta- Start (13:45)** = ShiftEnd - DeltaMinus
   - Manual logout becomes allowed

2. **Shift End Time (14:00)**
   - Auto-logout occurs here (if enabled)
   - Shift officially ends

3. **Delta+ Expiration (14:10)** = ShiftEnd + DeltaPlus
   - Shift transitions occur here
   - Shift info is cleared or updated to new shift

### Break Times

A shift can have any number of **break start times** (`JEGR_Utils.dbo.tblShiftBreaks`: `ShiftID`,
`BreakTime`), entered on the Shift Editor's Breaks tab.

- A break is a time of day strictly between the shift's start and end (for a midnight-crossing shift:
  after the start or before the end). The same time can appear only once per shift.
- At a break start time **everyone logged in on that shift is logged out** - on every machine the shift
  is assigned to, whatever their permissions (Rule TM3).
- A break has **no end time**. Logging in again, at the same station or another, is up to the operator.
- A break logout is an ordinary logout record (the same as a card-scan logout).
- Breaks started on a Jensen kiosk or terminal (log records with SubregType 6) are a different thing and
  are not affected by this rule.

### Operator State

Each operator logged into a machine has:
- **Machine ID**: Which machine they're logged into
- **Station ID**: Which station on that machine (multiple stations per machine possible)
- **Login Time**: When they originally logged in
- **Active Shift ID**: The shift they were assigned at login (null if no shift was active)

**IMPORTANT**: Only the shift ID is stored, and only in memory. Shift start/end times are **retrieved
from the datalayer** each time they're needed. This ensures operators always use the current shift
configuration. The shift ID belongs to the login: **every logout clears it** (card scan, replacement,
break, shift start, shift end, or a logout made by another application).

### Station Capacity and MaxLogins

Each station has a **MaxLogins** setting that defines how many operators can be logged in simultaneously:

- **MaxLogins = 1**: Only one operator at a time (most common)
- **MaxLogins > 1**: Multiple operators can be logged in (e.g., MaxLogins = 3 allows 3 operators)

**Checking Current Capacity:**
- Use: `OperatorStates.TotalLoggedInByMachineSubID(machineId, subId)`
- Where `subId` identifies the station

**Available Capacity:**
- A station has available capacity when: `current_count < MaxLogins`
- Station is FULL when: `current_count >= MaxLogins`

**Important Notes:**
- Shifts are assigned to **machines**, not individual stations
- All stations on the same machine share the machine's shift
- All operators at all stations on the same machine will either have shift info or not (no mix)
- Station capacity is independent of shift management

### Retrieving Shift Information from Datalayer

When the system needs to check delta periods or perform shift-related calculations:

1. **Get the operator's `ActiveShiftId`** from operator state
2. **Retrieve shift details from the datalayer** using the shift ID:
   - Shift start time
   - Shift end time
   - DeltaMinus value (in minutes)
   - DeltaPlus value (in minutes)
3. **Use those times** for all calculations (delta checks, auto-logout, transitions)
4. **If shift not found** (datalayer returns null):
   - Treat operator as having no shift info
   - Clear the operator's `ActiveShiftId` to null
   - Apply no-shift rules (operator can logout freely)

**Benefits of this approach**:
- Operators immediately use updated shift times if admin modifies shift configuration
- Datalayer is responsible for keeping shift data current
- Deleted shifts are automatically handled (operator loses shift restrictions)
- No data synchronization issues - datalayer manages data freshness

### Permissions

#### CANLOGOUTOFSHIFT = "Can Log Out Of Shift"

**Purpose**: Bypasses shift restrictions when leaving current station

**What it allows:**
- **Scanning own station to logout**: Unconditionally allows logout (bypasses ALL shift checks)
- **Moving to different station**: Bypasses shift restrictions for leaving current station (only if target login is viable)

**What it does NOT allow:**
- Does NOT bypass station capacity limits (MaxLogins)
- Does NOT allow replacing operators at full stations (need CANREPLACEOPERATOR)
- Does NOT automatically grant ability to leave - target login must be viable first

**How it works:**
- **Step 1**: System evaluates if login at target station is possible (capacity, replacement rules)
- **Step 2**: IF Step 1 succeeds, CANLOGOUTOFSHIFT bypasses shift restrictions for leaving current station
- **Exception**: When scanning own station, immediately allows logout (no Step 1/Step 2)

#### CANREPLACEOPERATOR = "Can Replace Operator"

**Purpose**: Allows replacing other operators at a station and initial login to any station

**What it allows:**
- **Initial Login (not logged in anywhere)**: Can log into ANY station, including empty stations
- **Replacing ALL operators** at target station (when any operators present)
- Works at both full stations and stations with available capacity
- Bypasses delta checks for operators being replaced
- When moving machines/stations while already logged in: Allows leaving current station outside delta (ONLY when target has operators to replace)

**What it does NOT allow:**
- Does NOT allow remote logout to empty stations (when already logged in elsewhere, requires someone to replace)
- Does NOT bypass auto-logout

**How it works:**
- When operator with CANREPLACEOPERATOR scans at station with operators:
  - ALL operators at that station are logged out
  - All their shift info is cleared
  - LED turns RED
  - New operator logs in
  - New operator assigned current active shift (if exists)
  - LED turns GREEN

#### Permissions Working Together - Supervisor Workflow Example

**Supervisor has BOTH permissions:**

1. **Take over station** (CANREPLACEOPERATOR):
   - Station has 3 operators logged in with shifts (outside delta)
   - Supervisor scans → Replaces all 3 operators
   - Supervisor now logged in at station

2. **Complete task**: Supervisor performs work

3. **Log out** (CANLOGOUTOFSHIFT):
   - Supervisor scans own station (10:00 AM, outside any delta)
   - Bypasses shift restrictions → Logout successful
   - Station now empty

**Without CANLOGOUTOFSHIFT**: Supervisor would be stuck until delta period!

---

## Global Settings and Prerequisites

### CRITICAL PREREQUISITE FOR ALL SHIFT MANAGEMENT RULES

**ALL shift management rules documented in this specification ONLY apply when BOTH conditions are met:**

1. **`UseShiftManagement = true`** (service config setting)
2. **Machine has an active shift assigned**

**If EITHER condition is false:**
- All shift management logic is bypassed
- Legacy operator login/logout logic applies - **exactly the pre-shift behaviour**, which must still
  be implemented and tested: scanning logs the operator in (or out at their own station), a
  MaxLogins = 1 station logs out its occupant first, a full multi-login station refuses, and
  scanning elsewhere logs the operator out there and in here
- The legacy rules are unchanged, so they are not re-specified here ("OUT OF SCOPE" below means
  "shift rules don't apply - use legacy logic", not "nothing to do")

**This prerequisite applies to:**
- Auto-logout checks
- Shift transition checks
- Delta period checks
- All card scan scenarios (A, B, C, D)
- All replacement and movement rules

### UseShiftManagement
- **Type**: Boolean (service config setting in `CentralLoginService.exe.config`, written by the
  installer: `true` by the Shift MSI, `false` by the standard MSI - not a database setting)
- **Purpose**: Master switch for shift management
- **When FALSE**: Legacy logic applies (see prerequisite above)
- **When TRUE**: Shift rules apply (IF machine has active shift)

### AutoLogoutAtShiftEnd
- **Type**: Boolean (database setting, `JEGR_Utils.dbo.tblShiftSettings`)
- **Purpose**: Controls automatic logout when shift ends
- **Default: ON.** `schema.sql` seeds `'True'`, and the service treats a missing row, or any value that
  is not `False`, `0`, `No` or `Off`, as on
- **Not shown in the Shift Editor.** It can only be changed by SQL (Ge-Mac support):
  `UPDATE JEGR_Utils.dbo.tblShiftSettings SET SettingValue = 'False', ModifiedDate = GETDATE() WHERE SettingName = 'AutoLogoutAtShiftEnd'`
- **When ON**: Operators are automatically logged out when their shift ends (at shift end time, NOT after delta+)
- **When OFF**: Operators are not logged out at shift end. Once delta+ has passed they move to the
  shift that follows on the machine, or are released from shift rules if there is none (Rule TM2)
- The setting does **not** affect the shift start rule (TM0) or breaks (TM3)
- The service logs one line when the value it is using changes, including at start:
  `[AutoLogout] Logout at shift end is ON` / `... is OFF (...)`

### Shift Configuration Changes
- The service re-reads shifts, machine assignments, break times, shift settings and operator settings
  from the database **once a minute** (and at start). A change saved in the Shift Editor, or made by
  SQL, takes effect within about a minute - no restart
- If a table cannot be read, the previous contents stay in use and one `[Reload] Loading shift data
  failed` line is logged (at most once a minute)
- **Assigning a machine to a shift that is already running** makes every operator on that machine "logged
  in without a shift while a shift is running": Rule TM0 logs them out at the next check
- **Moving a running shift's end time into the past** logs out everyone on it at the next check (Rule TM1)
- `UseShiftManagement` itself is a service config value: changing it needs a service restart, and on
  restart the SERVICE START rules apply

---

## ON SERVICE START Logic

When the service starts, it reads the shift data and then recovers operator states from the database
(the latest login/logout record of each operator).

### Rule SS1: Operator Was Logged In Before Service Stopped

**FOR EACH** operator whose latest record is a login:

1. **Restore the login** at its machine and station with its **original login time**

2. **Determine if there was a shift covering the operator's original login time**
   - Use the operator's original login time (NOT current time)
   - Get from datalayer: "Was there a shift assigned to this machine that was active at {original login time}?"

3. **IF THERE IS A SHIFT covering the login time**:
   - `ActiveShiftId` = shift ID
   - Keep operator logged in
   - Log: "[ServiceStart] Operator {id} ... Assigned to shift {name} covering original login time"
   - If that shift has since ended, the first minute check deals with it (Rule TM1 logs the operator
     out; with `AutoLogoutAtShiftEnd` off, Rule TM2 moves or releases them)

4. **IF NO SHIFT covered the login time**:
   - Restore the operator **without** shift info (`ActiveShiftId` = null). Nothing else happens here

**ONCE EVERY RECORD HAS BEEN PROCESSED**, apply the shift start rule (Rule TM0) to everyone:

- An operator restored without a shift, on a machine whose shift is running **now**, is logged out
  (Log: "[ShiftStart] Operator {id} ... logged out - logged in without a shift and shift {name} is running")
- An operator restored without a shift on a machine with no shift running stays logged in, free of
  shift rules

The logouts are applied after the loop, not inside it, because a logout changes the list of records
being processed.

The first minute check then runs straight away (see below), so a break that passed while the service
was stopped - up to five minutes ago - is also applied at start.

**The same processing runs whenever the service picks up login records written by another
application**, and after an operator-table reload. On a reload each logged-in operator keeps the shift
they already had (it is not re-derived), provided they are still at the same station.

---

## EVERY MINUTE Timer Logic

The service runs these checks **once per minute** (on the first timer tick after the minute changes,
and on the first tick after a start). They run **before** that tick's card scans, so a scan in the first
moments of a shift, a break or the end of a shift already sees the new state.

### Order of Operations

**CRITICAL**: Execute in this exact order:

1. **Refresh shift data** from the database (shifts, assignments, breaks, settings, operator settings)
2. **Shift Start Check** (Rule TM0)
3. **Auto-Logout Check** (Rule TM1 - if enabled)
4. **Shift Transition Check** (Rule TM2)
5. **Break Check** (Rule TM3)

None of the rules acts on an operator who is not at a station (machine ID <= 0 - the state a break
started on a Jensen kiosk leaves), except that Rule TM1 releases such an operator from an ended shift
without writing a logout.

### Rule TM0: Shift Start Check

Applies whatever `AutoLogoutAtShiftEnd` is set to - it is a rule of shift logging.

**FOR EACH** logged-in operator **without** shift info (`ActiveShiftId` is null):

1. **Get the current active shift for the operator's machine** (active RIGHT NOW)
2. **IF there IS a current active shift:**
   - Log operator out (ordinary logout record)
   - Set LED to RED at their station once nobody is left logged in there
   - Log: "[ShiftStart] Operator {id} at Machine {m}, Sub {s} logged out - logged in without a shift and shift {name} is running on the machine"
3. **IF there IS NO current active shift:** no action - shift rules don't apply to this machine now

This is what happens to a login made in a gap when the next shift starts, to a login made before
shift management had anything to say about the machine, and to a machine added to a running shift.
An operator who scans in a few seconds before a shift starts is logged out at the start and scans again.

### Rule TM1: Auto-Logout Check

**ONLY IF** `AutoLogoutAtShiftEnd` is on (the default):

**FOR EACH** logged-in operator with shift info:

1. **Get shift details from datalayer** using `ActiveShiftId`
   - IF shift not found → leave the operator to Rule TM2 (which runs next)

2. **Determine if the shift is running now**, from the shift's own start and end times:
   - **Normal shift** (e.g., 06:00-14:00): running when start <= time of day < end
   - **Midnight-crossing shift** (e.g., 22:00-06:00): running when time of day >= start OR time of day < end

3. **IF the shift is not running** (it has ended):
   - Log operator out immediately (ordinary logout record; `ActiveShiftId` is cleared with the logout)
   - Set LED to RED at their station once nobody is left logged in there
   - Log: "[AutoLogout] Operator {id} at Machine {m}, Sub {s} logged out - shift {name} ended at {end}"

**IMPORTANT**: Auto-logout happens at shift end time (14:00), NOT after delta+ expires. Permissions
make no difference. An operator restored at a service start on a shift that has already ended is
logged out by the first check.

### Rule TM2: Shift Transition Check

Has work to do only when `AutoLogoutAtShiftEnd` is **off**, or when a shift was deleted while
operators were on it.

**FOR EACH** logged-in operator with shift info:

1. **Get current shift details from datalayer** using the operator's `ActiveShiftId`

2. **IF the shift still exists:**
   - IF it is running now → SKIP
   - IF it has ended but current time <= (most recent shift end + DeltaPlus) → SKIP (grace period)

3. **Otherwise** (the shift has ended and delta+ has passed, or the shift no longer exists) - get the
   current active shift for the operator's machine:

   **IF there IS a current active shift:**
   - `ActiveShiftId` = that shift's ID; operator remains logged in, now held to that shift
   - Log: "[ShiftTransition] Operator {id} moved to shift {newId} ({name}) - {reason}"

   **IF there IS NO current active shift:**
   - `ActiveShiftId` = null; operator remains logged in with no shift restrictions
   - Log: "[ShiftTransition] Operator {id} shift info cleared - {reason}, no shift running on the machine"
   - When a shift later starts on the machine, Rule TM0 logs this operator out

**Removed in version 1.1 - Rule TM2.2** ("assign the running shift to a logged-in operator who has
none"). Such an operator is now logged out by Rule TM0.

### Rule TM3: Break Check

Applies whatever `AutoLogoutAtShiftEnd` is set to.

**FOR EACH** logged-in operator with shift info:

1. **Find the most recent start of any of the shift's breaks**: for each break time of the operator's
   shift, today at that time - or yesterday at that time if today's is still in the future - and take
   the latest
2. **IF** that moment is **no more than 5 minutes ago AND** the operator **logged in before it**:
   - Log operator out (ordinary logout record)
   - Set LED to RED at their station once nobody is left logged in there
   - Log: "[Break] Operator {id} at Machine {m}, Sub {s} logged out - break at {HH:mm}"
3. Otherwise no action

Nothing is remembered between checks, which gives these properties:

- **Everyone on the shift** is logged out, whatever their permissions, on every machine of the shift
- An operator who logs in again after the break (even seconds after) is **not** logged out again by
  the same break - their login time is later than the break
- A check that runs late, or a service start up to 5 minutes after a break, still applies the break
- A break time added or changed in the Shift Editor applies from the next check. One set to a time
  less than 5 minutes ago still fires; anything further in the past waits for the next day
- A midnight-crossing shift needs no special handling
- An operator carried into a following shift (auto-logout off) is subject to that shift's breaks

---

## AT CARD SCAN Logic

When an operator scans their card at a reader, the system must handle multiple scenarios.

### Pre-Scan Checks

**BEFORE** any login/logout logic:

1. **Validate card data** (out of scope - assume card is valid)
2. **Deduplicate scans** (prevent multiple scans in quick succession)
3. **Identify operator, machine, and station**

### Main Card Scan Decision Tree

Determine operator's current state and proceed to appropriate rule:

```
IS operator currently logged in?
├─ NO  → Go to SCENARIO A: Operator Not Logged In
├─ YES → Is operator logged into THIS machine?
   ├─ YES → Is operator logged into THIS station?
   │  ├─ YES → Go to SCENARIO B: Operator Scans Own Station
   │  └─ NO  → Go to SCENARIO C: Operator Scans Different Station (Same Machine)
   └─ NO  → Go to SCENARIO D: Operator Scans Different Machine
```

---

### SCENARIO A: Operator Not Logged In Anywhere

**Operator A scans card at Machine X, Station Y**

**PREREQUISITE CHECK:**
- IF `UseShiftManagement = false` OR Machine X has no active shift → OUT OF SCOPE (legacy logic)

#### A.1: Check Station Capacity and Operator Permissions

1. **Get station capacity information:**
   - Get `MaxLogins` for Station Y
   - Get `current_count` = `TotalLoggedInByMachineSubID(X, Y)`

2. **Check if station is empty:**

   **IF current_count = 0** (station is completely empty):
   - Go to **Rule A.1.1**: Login to Empty Station
   - NOTE: This applies to ALL operators, including those with CANREPLACEOPERATOR (no restriction on initial login to empty stations)
   - DONE

3. **Station has operators (current_count > 0):**

   **Check Operator A's CANREPLACEOPERATOR permission:**

   **IF Operator A HAS CANREPLACEOPERATOR:**
   - Go to **Rule A.2**: Mass Replace All Operators at Station
   - DONE

   **IF Operator A does NOT have CANREPLACEOPERATOR:**

   a. **Check if station has available capacity:**
      - IF `current_count < MaxLogins`:
        - Go to **Rule A.1.2**: Login to Available Slot
      - IF `current_count >= MaxLogins`:
        - Go to **Rule A.3**: Station Full - Check Replacement Eligibility

#### Rule A.1.1: Login to Empty Station

**Station is completely empty (current_count = 0)**

1. **Get current active shift for this machine**
   - Get from datalayer: active shift at Machine X RIGHT NOW
   - IF no active shift found → OUT OF SCOPE (legacy logic)

2. **Log operator in:**
   - `LoginTime` = now
   - `IsActiveStation` = true

3. **Assign shift info (if active shift exists):**
   - `ActiveShiftId` = shift ID
   - Turn LED GREEN
   - Log: "Login | Operator {id} logged into shift {shiftId} at empty station"

#### Rule A.1.2: Login to Available Slot

**Station has operators but is not full (0 < current_count < MaxLogins)**

**Operator does NOT have CANREPLACEOPERATOR**

1. **Get current active shift for this machine**
   - Get from datalayer: active shift at Machine X RIGHT NOW
   - IF no active shift found → OUT OF SCOPE (legacy logic)

2. **Log operator in:**
   - `LoginTime` = now
   - `IsActiveStation` = true

3. **Assign shift info (if active shift exists):**
   - `ActiveShiftId` = shift ID
   - Turn LED GREEN
   - Log: "Login | Operator {id} logged into shift {shiftId} at available slot ({current_count + 1}/{MaxLogins})"

#### Rule A.2: Mass Replace All Operators at Station

**Operator A HAS CANREPLACEOPERATOR permission**

**Station has operators logged in (current_count > 0)**

1. **Get current active shift for this machine**
   - Get from datalayer: active shift at Machine X RIGHT NOW
   - IF no active shift found → OUT OF SCOPE (legacy logic)

2. **Get list of all operators currently logged into this station**
   - Use `TotalLoggedInByMachineSubID(X, Y)` to identify all operators

3. **Log out ALL operators at this station:**
   - FOR EACH operator at station:
     - Set `IsActiveStation` = false
     - Clear `ActiveShiftId` = null
   - Turn LED RED
   - Log: "Mass Replacement | Logging out {count} operators from station"

4. **Log in Operator A:**
   - `LoginTime` = now
   - `IsActiveStation` = true
   - `ActiveShiftId` = shift ID (current active shift)
   - Turn LED GREEN
   - Log: "Mass Replacement | Operator {A} replaced {count} operators at station (CANREPLACEOPERATOR)"

**Key Points:**
- ALL operators are replaced, regardless of their delta status
- CANREPLACEOPERATOR bypasses all delta checks for operators being replaced
- Works whether station is full or has available capacity

#### Rule A.3: Station Full - Check Replacement Eligibility

**Station is FULL (current_count >= MaxLogins)**

**Operator A does NOT have CANREPLACEOPERATOR**

1. **Get current active shift for this machine**
   - Get from datalayer: active shift at Machine X RIGHT NOW
   - IF no active shift found → OUT OF SCOPE (legacy logic)

2. **Check if ANY operator at station is within delta period:**

   FOR EACH operator at station:
   - Get operator's shift details from datalayer
   - Check if within delta: (shift end - DeltaMinus) to (shift end + DeltaPlus)
   - IF any operator is within delta → At least one can be replaced

3. **Decision:**

   **IF at least one operator is within delta:**
   - Select operator to replace (implementation detail: first found, oldest, etc.)
   - Log out selected operator
   - Clear their shift info
   - Turn LED RED (briefly)
   - Log in Operator A
   - Assign current shift to A
   - Turn LED GREEN
   - Log: "Replacement | Operator {A} replaced operator {B} (within delta)"

   **IF NO operators are within delta:**
   - **DENY** login
   - Turn LED ORANGE for 5 seconds
   - Log: "Station Full | Cannot replace operators - all outside delta, no CANREPLACEOPERATOR permission"
   - Operator A is not logged in

---

### SCENARIO B: Operator Scans Own Station

**Operator is logged into Station X, scans card at Station X again**

This is interpreted as a **LOGOUT ATTEMPT**.

#### Rule B.1: Logout Attempt

1. **Check if shift management is enabled**
   - IF `UseShiftManagement` is FALSE:
     - Check legacy permissions (out of scope)
     - Log operator out
     - Turn LED RED
     - DONE

2. **Shift management IS enabled:**

   **Check if operator has CANLOGOUTOFSHIFT permission:**

   **IF YES:**
   - Log operator out immediately
   - Clear shift info
   - Turn LED RED
   - Log: "Logout | Operator {id} logged out (CANLOGOUTOFSHIFT permission)"
   - DONE

   **IF NO:**

   a. **Check if operator has shift info** (`ActiveShiftId` != null)

      **IF NO shift info:**
      - Log operator out
      - Turn LED RED
      - DONE

      **IF HAS shift info:**

      b. **Get shift details from datalayer:**
         - Get shift object using operator's `ActiveShiftId`
         - IF shift not found (datalayer returns null) → Clear `ActiveShiftId` and allow logout (treat as no shift)
         - Get shift start time, shift end time, DeltaMinus, and DeltaPlus from shift object

      c. **Check if within delta period:**
         - Is current time within (shift end - DeltaMinus) to (shift end + DeltaPlus)?
         - Account for midnight crossing

      d. **Decision:**

         **IF within delta:**
         - Log operator out
         - Clear shift info (`ActiveShiftId` = null)
         - Turn LED RED
         - Log: "Logout | Operator {id} logged out (within delta period)"

         **IF outside delta:**
         - DENY logout
         - Turn LED ORANGE for 5 seconds
         - Log: "Logout Denied | Operator {id} outside delta period, no CANLOGOUTOFSHIFT permission"

---

### SCENARIO C: Operator Scans Different Station (Same Machine)

**Operator A is logged into Machine X, Station 1**
**Operator A scans card at Machine X, Station 2**

This is **STATION MOVEMENT** (same machine, different station).

**PREREQUISITE CHECK:**
- IF `UseShiftManagement = false` OR Machine X has no active shift → OUT OF SCOPE (legacy logic)

**TWO-STEP EVALUATION PROCESS:**

#### STEP 1: Can Login at Target Station (Station 2)?

1. **Get station capacity:**
   - Get `current_count` = `TotalLoggedInByMachineSubID(X, 2)`

2. **Check if Station 2 is empty:**

   **IF current_count = 0** (completely empty):
   - **Target login is NOT viable**
   - CANREPLACEOPERATOR requires someone to replace
   - **STOP → DENY**
   - Turn LED ORANGE at Station 2
   - Log: "Station Movement Denied | Cannot move to empty station - no one to replace"
   - DONE

3. **Station 2 has operators (current_count > 0):**

   **Check if Operator A has CANREPLACEOPERATOR:**

   **IF YES:**
   - **Target login IS viable** (will replace all operators)
   - Continue to STEP 2 ✓

   **IF NO:**

   a. **Check if Station 2 has available capacity:**
      - IF `current_count < MaxLogins`:
        - **Target login IS viable** (available slot)
        - Continue to STEP 2 ✓

   b. **Station 2 is FULL (current_count >= MaxLogins):**
      - Check if ANY operator at Station 2 is within delta
      - IF at least one within delta:
        - **Target login IS viable** (can replace)
        - Continue to STEP 2 ✓
      - IF NONE within delta:
        - **Target login NOT viable**
        - **STOP → DENY**
        - Turn LED ORANGE at Station 2
        - Log: "Station Movement Denied | Station full, no one within delta"
        - DONE

#### STEP 2: Can Leave Current Station (Station 1)?

**Only executed if STEP 1 succeeded**

1. **Check if Operator A has shift info at Station 1:**

   **IF NO shift info:**
   - Can leave ✓
   - Go to STEP 3

   **IF HAS shift info:**

   a. **Check if has CANLOGOUTOFSHIFT permission:**
      - IF YES → Can leave ✓ (bypasses shift checks)
      - Go to STEP 3

   b. **Check if has CANREPLACEOPERATOR AND target has operators:**
      - IF YES → Can leave ✓ (replacing someone allows leaving)
      - Go to STEP 3

   c. **Get shift details from datalayer:**
      - Get shift object using Operator A's `ActiveShiftId`
      - IF shift not found → Treat as no shift, can leave ✓
      - Get shift end time, DeltaMinus, DeltaPlus

   d. **Check if within delta period:**
      - Is current time within (shift end - DeltaMinus) to (shift end + DeltaPlus)?
      - IF YES → Can leave ✓
      - IF NO → **CANNOT leave** → DENY

   **IF CANNOT leave:**
   - **STOP → DENY**
   - Turn LED ORANGE at Station 2
   - Operator A remains at Station 1
   - Log: "Station Movement Denied | Cannot leave current station - outside delta"
   - DONE

#### STEP 3: Execute Station Movement

**Both STEP 1 and STEP 2 succeeded**

1. **If Operator A has CANREPLACEOPERATOR AND Station 2 has operators:**
   - Log out ALL operators at Station 2
   - Clear their shift info
   - Turn LED RED at Station 2

2. **If no CANREPLACEOPERATOR but Station 2 has available slot:**
   - No operators logged out

3. **If no CANREPLACEOPERATOR but replacing one operator within delta:**
   - Log out the operator within delta
   - Clear their shift info
   - Turn LED RED at Station 2 (briefly)

4. **Log out Operator A from Station 1:**
   - Set `IsActiveStation` = false at Station 1

5. **Log in Operator A at Station 2:**
   - `IsActiveStation` = true at Station 2
   - Retain `ActiveShiftId` (same machine, same shift)
   - Turn LED GREEN at Station 2
   - Log: "Station Movement | Operator {A} moved from Station 1 to Station 2"

---

### SCENARIO D: Operator Scans Different Machine

**Operator A is logged into Machine X, Station 1**
**Operator A scans card at Machine Y, Station Z**

This is **REMOTE LOGOUT** (logout from old machine, login to new machine).

**PREREQUISITE CHECK:**
- IF `UseShiftManagement = false` OR Machine Y has no active shift → OUT OF SCOPE (legacy logic)

**TWO-STEP EVALUATION PROCESS:**

**Note**: SCENARIO D follows the same two-step pattern as SCENARIO C, but involves different machines.

#### STEP 1: Can Login at Target Station (Machine Y, Station Z)?

1. **Get station capacity:**
   - Get `current_count` = `TotalLoggedInByMachineSubID(Y, Z)`

2. **Check if Station Z is empty:**

   **IF current_count = 0** (completely empty):
   - **Target login is NOT viable** (CANREPLACEOPERATOR requires someone to replace)
   - **STOP → DENY**
   - Turn LED ORANGE at Machine Y
   - Log: "Remote Logout Denied | Cannot move to empty station - no one to replace"
   - DONE

3. **Station Z has operators (current_count > 0):**

   **Check if Operator A has CANREPLACEOPERATOR:**

   **IF YES:**
   - **Target login IS viable** (will replace all operators)
   - Continue to STEP 2 ✓

   **IF NO:**

   a. **Check if Station Z has available capacity:**
      - IF `current_count < MaxLogins`:
        - **Target login IS viable** (available slot)
        - Continue to STEP 2 ✓

   b. **Station Z is FULL (current_count >= MaxLogins):**
      - Check if ANY operator at Station Z is within delta
      - IF at least one within delta:
        - **Target login IS viable** (can replace)
        - Continue to STEP 2 ✓
      - IF NONE within delta:
        - **Target login NOT viable**
        - **STOP → DENY**
        - Turn LED ORANGE at Machine Y
        - Log: "Remote Logout Denied | Station full, no one within delta"
        - DONE

#### STEP 2: Can Leave Current Machine/Station (Machine X, Station 1)?

**Only executed if STEP 1 succeeded**

1. **Check if Operator A has shift info at Machine X:**

   **IF NO shift info:**
   - Can leave ✓
   - Go to STEP 3

   **IF HAS shift info:**

   a. **Check if has CANLOGOUTOFSHIFT permission:**
      - IF YES → Can leave ✓ (bypasses shift checks)
      - Go to STEP 3

   b. **Check if has CANREPLACEOPERATOR AND target has operators:**
      - IF YES → Can leave ✓ (replacing someone allows leaving)
      - Go to STEP 3

   c. **Get shift details from datalayer:**
      - Get shift object using Operator A's `ActiveShiftId`
      - IF shift not found → Treat as no shift, can leave ✓
      - Get shift end time, DeltaMinus, DeltaPlus

   d. **Check if within delta period:**
      - Is current time within (shift end - DeltaMinus) to (shift end + DeltaPlus)?
      - IF YES → Can leave ✓
      - IF NO → **CANNOT leave** → DENY

   **IF CANNOT leave:**
   - **STOP → DENY**
   - Turn LED ORANGE at Machine Y
   - Operator A remains at Machine X
   - Log: "Remote Logout Denied | Cannot leave current machine - outside delta"
   - DONE

#### STEP 3: Execute Remote Logout

**Both STEP 1 and STEP 2 succeeded**

1. **If Operator A has CANREPLACEOPERATOR AND Station Z has operators:**
   - Log out ALL operators at Station Z (Machine Y)
   - Clear their shift info
   - Turn LED RED at Machine Y

2. **If no CANREPLACEOPERATOR but Station Z has available slot:**
   - No operators logged out at Machine Y

3. **If no CANREPLACEOPERATOR but replacing one operator within delta:**
   - Log out the operator within delta at Station Z
   - Clear their shift info
   - Turn LED RED at Machine Y (briefly)

4. **Log out Operator A from Machine X, Station 1:**
   - Set `IsActiveStation` = false at Machine X
   - Clear `ActiveShiftId` = null

5. **Log in Operator A at Machine Y, Station Z:**
   - `IsActiveStation` = true at Machine Y
   - `LoginTime` = now
   - Get current active shift at Machine Y
   - Assign `ActiveShiftId` = Machine Y's shift ID (if exists)
   - Turn LED GREEN at Machine Y
   - Log: "Remote Logout | Operator {A} moved from Machine {X} to Machine {Y}"

---

## Complete Decision Trees

### Decision Tree 1: Card Scan Entry Point

```
CARD SCANNED at Machine M, Station S by Operator O
│
├─ Is O logged in anywhere?
│  │
│  ├─ NO → SCENARIO A: Not Logged In
│  │     │
│  │     ├─ Is station S empty (or allows multiple)?
│  │     │  ├─ YES → Rule A.1.1: Login to Empty Station
│  │     │  └─ NO  → Rule A.2: Replace Other Operator
│  │
│  └─ YES → Is O logged into machine M?
│        │
│        ├─ YES → Is O logged into station S?
│        │     │
│        │     ├─ YES → SCENARIO B: Own Station (Logout Attempt)
│        │     │        └─ Rule B.1: Logout Attempt
│        │     │
│        │     └─ NO  → SCENARIO C: Different Station (Same Machine)
│        │              └─ Rule C.1: Station Movement
│        │
│        └─ NO  → SCENARIO D: Different Machine (Remote Logout)
│              │
│              ├─ Is station S empty (or allows multiple)?
│              │  ├─ YES → Rule D.1: Remote Logout to Empty Station
│              │  └─ NO  → Rule D.2: Remote Logout + Replace Other Operator
```

### Decision Tree 2: Manual Logout Permission Check

```
OPERATOR ATTEMPTING MANUAL LOGOUT
│
├─ UseShiftManagement enabled?
│  │
│  ├─ NO → Allow logout (legacy permission checks only)
│  │
│  └─ YES → Does operator have CANLOGOUTOFSHIFT permission?
│        │
│        ├─ YES → ALLOW logout (bypass all shift checks)
│        │
│        └─ NO → Does operator have shift info?
│              │
│              ├─ NO → ALLOW logout
│              │
│              └─ YES → Is current time within delta period?
│                    │  Delta = (ShiftEnd - Delta) to (ShiftEnd + Delta)
│                    │
│                    ├─ YES → ALLOW logout
│                    │
│                    └─ NO → DENY logout (Orange LED)
```

### Decision Tree 3: Replacement Permission Check

```
OPERATOR A ATTEMPTING TO REPLACE OPERATOR B
│
├─ UseShiftManagement enabled?
│  │
│  ├─ NO → Allow replacement
│  │
│  └─ YES → Does Operator B have shift info?
│        │
│        ├─ NO → Allow replacement
│        │
│        └─ YES → Is Operator B within delta period?
│              │
│              ├─ YES → ALLOW replacement
│              │
│              └─ NO → Does Operator A have CANREPLACEOPERATOR permission?
│                    │
│                    ├─ YES → ALLOW replacement
│                    │
│                    └─ NO → DENY replacement (Orange LED)
```

### Decision Tree 4: Every Minute - Shift Start and Auto-Logout Checks

```
EVERY MINUTE (shift data refreshed first)
│
├─ SHIFT START CHECK - FOR EACH logged-in operator WITHOUT shift info:
│  │
│  └─ Is a shift running on the operator's machine now?
│     ├─ NO  → No action (legacy rules apply on this machine)
│     └─ YES → LOG OUT operator, LED RED
│
├─ AutoLogoutAtShiftEnd on? (default: on)
│  │
│  ├─ NO → Skip to Shift Transition Check
│  │
│  └─ YES → FOR EACH logged-in operator WITH shift info:
│        │
│        └─ Is the operator's shift running now?
│           │  (Account for midnight crossing)
│           │
│           ├─ YES → Skip this operator
│           │
│           └─ NO → AUTO-LOGOUT operator immediately
│                   Shift info cleared with the logout
│                   Turn LED RED
│
├─ SHIFT TRANSITION CHECK (Decision Tree 5)
│
└─ BREAK CHECK (Decision Tree 7)
```

### Decision Tree 5: Shift Transition Check (Every Minute)

```
SHIFT TRANSITION CHECK (runs after auto-logout check)
│
FOR EACH logged-in operator WITH shift info:
│  (operators without shift info are handled by the Shift Start Check)
│
├─ Does the operator's shift still exist?
│  │
│  ├─ YES → Is it running now?
│  │        ├─ YES → Skip
│  │        └─ NO  → Is current time <= (most recent shift end + DeltaPlus)?
│  │                 ├─ YES → Skip (delta+ grace period)
│  │                 └─ NO  → continue below
│  │
│  └─ NO → continue below
│
└─ Get current active shift at operator's machine
   │
   ├─ Active shift exists → MOVE operator to it (ActiveShiftId = new shift ID)
   │
   └─ No active shift     → CLEAR shift info (ActiveShiftId = null)
                            Operator stays logged in without shift restrictions
```

### Decision Tree 6: Service Start Recovery

```
ON SERVICE START (shift data loaded first)
│
FOR EACH operator whose latest record is a login:
│
├─ Restore the login (machine, station, original login time)
│
└─ Was there a shift on the machine covering the operator's ORIGINAL login time?
   │
   ├─ YES → Assign that shift to operator (ActiveShiftId)
   │
   └─ NO  → Restore without shift info
│
THEN, once for everyone: SHIFT START CHECK (Decision Tree 4)
│   → an operator without shift info on a machine whose shift is running now is LOGGED OUT
│
THEN: the first EVERY MINUTE checks run on the first timer tick
```

### Decision Tree 7: Break Check (Every Minute)

```
BREAK CHECK (runs last)
│
FOR EACH logged-in operator WITH shift info:
│
├─ Does the operator's shift have any break times?
│  │
│  ├─ NO → Skip
│  │
│  └─ YES → B = the most recent start of any of them (today, or yesterday if today's is still to come)
│        │
│        ├─ Was B more than 5 minutes ago?      → Skip
│        ├─ Did the operator log in at or after B? → Skip (already back from this break)
│        │
│        └─ Otherwise → LOG OUT operator, LED RED
```

---

## Edge Cases and Special Scenarios

### Edge Case 1: Shift Not Found by ID

**Scenario**: Operator has `ActiveShiftId` = 123, but shift 123 no longer exists in database (deleted).

**Handling**:
- **At the next minute check** (Rule TM2): the operator is moved to the shift running on the machine
  now, or - if there is none - `ActiveShiftId` is cleared and the operator stays logged in without
  shift restrictions. They are not logged out for this
- **At a card scan before that check**: the shift lookup returns null, so `ActiveShiftId` is cleared
  and the scan is treated under no-shift rules (the operator can log out or move)
- The shift's break times are deleted with it (database cascade)

### Edge Case 2: Shift Times Modified While Operator Logged In

**Scenario**: Operator logged into Shift A (06:00-14:00) at 06:30, admin changes Shift A to (07:00-15:00) at 10:00.

**Handling** (with shift ID-only storage):
- Operator has `ActiveShiftId` = Shift A ID stored
- When system needs to check delta at 13:00:
  1. Get Shift A object from datalayer using the shift ID
  2. Get current shift times from shift object: 07:00-15:00 (NEW times)
  3. Calculate delta based on NEW times (15:00 end time, not 14:00)
  4. Operator can now logout between 14:45-15:15 (not 13:45-14:15)
- Operators use the updated shift times **within about a minute** of the change (the service re-reads
  the shift data once a minute)

**Benefits**:
- No need to notify operators, restart the service or reload state
- Consistent behavior across all operators in the shift

**Watch out for**: moving the end time of a running shift into the past logs out everyone on it at the
next check, and the Shift Editor refuses new times that would leave one of the shift's breaks outside it.

### Edge Case 3: Midnight Crossing Shifts

**Scenario**: Shift runs from 22:00 to 06:00 (next day).

**Detection**:
- `LoginShiftStart` > `LoginShiftEnd` (22:00 > 06:00) indicates midnight crossing

**Delta Calculation**:
- **Example**: Shift 22:00-06:00, Delta = 15 minutes
- Delta- period: 05:45-06:00 (next day)
- Delta+ period: 06:00-06:15 (next day)

**Current Time Comparison**:
```
Current Time: 05:50 (before shift end)
  -> Shift has NOT ended yet (05:50 < 06:00)
  -> Within delta- period (05:50 is between 05:45 and 06:00)

Current Time: 06:10 (after shift end)
  -> Shift HAS ended (06:10 >= 06:00)
  -> Within delta+ period (06:10 is between 06:00 and 06:15)

Current Time: 23:00 (during shift)
  -> Shift has NOT ended (we're in the middle of the shift)
  -> Outside delta period

Current Time: 20:00 (before shift starts)
  -> Shift has ended AND we've passed delta+
  -> This is the next day, after 06:15
```

**Auto-Logout Logic**:
```
For shift 22:00-06:00:
  IF (current time >= 06:00 AND current time < 22:00):
    -> Shift has ended
    -> Auto-logout if past 06:00
```

### Edge Case 4: DST (Daylight Saving Time) Transitions

**Scenario**: Clocks "spring forward" or "fall back" during a shift.

**Handling**:
- **ALWAYS** use current server time
- If server time jumps forward (spring forward):
  - Operators may suddenly be past shift end
  - Auto-logout will trigger on next minute check
- If server time jumps backward (fall back):
  - Operators may have extra time in their shift
  - Delta calculations use the "new" time

**Recommendation**:
- No special handling needed
- System adapts to server time automatically
- Document that shift times are based on server time

### Edge Case 5: Gap Between Shifts

**Scenario**:
- Shift A: 06:00-14:00
- **GAP**: 14:00-16:00 (no shift)
- Shift B: 16:00-22:00
- Delta 0 (the default)

**Timeline for Operator logged into Shift A at 06:30**:

| Time | Auto-Logout On (default) | Auto-Logout Off (set by SQL) |
|------|--------------------------|------------------------------|
| 13:59 | Held to Shift A - cannot logout | Held to Shift A - cannot logout |
| 14:00 | **AUTO-LOGOUT occurs** | Shift A ended → shift info cleared, stays logged in |
| 14:20 | (logged out; may log in again under legacy rules) | No shift info, no restrictions |
| 16:00 | A legacy login made in the gap is **logged out** (shift start rule) | **Logged out** (shift start rule) - logs in again to join Shift B |

**Key Point**: Whatever the setting, nobody is carried across a gap into the next shift: anyone logged
in without a shift when a shift starts is logged out and logs in again.

### Edge Case 6: Back-to-Back Shifts (No Gap)

**Scenario**:
- Shift A: 06:00-14:00
- Shift B: 14:00-22:00 (starts exactly when A ends)
- Delta 0 (the default)

**Timeline for Operator logged into Shift A at 06:30**:

| Time | Auto-Logout On (default) | Auto-Logout Off (set by SQL) |
|------|--------------------------|------------------------------|
| 13:59 | Held to Shift A | Held to Shift A |
| 14:00 | **AUTO-LOGOUT occurs**; Shift B operators log in from now | Shift A ended, Shift B running → **moved to Shift B**, stays logged in |
| 14:20 | (logged out) | In Shift B, held to it and subject to its breaks |

**Key Point**: With auto-logout off, an operator is carried from one shift straight into the next only
when the shifts are back to back. With a non-zero DeltaPlus the move happens when delta+ expires.

### Edge Case 7: Multiple Shifts Overlap (Should Be Impossible)

**Scenario**: Machine has two shifts active at the same time (design flaw).

**Handling**:
- **PREVENTION**: Database/UI should prevent overlapping shifts
- **IF it happens**: System gets first active shift from query
- Undefined behavior - FIX THE DATA

**Recommendation**:
- Add database constraint or validation to prevent overlaps
- When querying for "current active shift", ensure query returns only one result

### Edge Case 8: Very Large Delta Values

**Scenario**: Shift 06:00-14:00 with Delta = 120 minutes (2 hours).

**Implications**:
- Delta- period: 12:00-14:00 (can logout 2 hours before shift ends)
- Delta+ period: 14:00-16:00 (can logout up to 2 hours after shift ends)
- Auto-logout (if enabled) happens at 14:00
- Transition to next shift happens at 16:00

**Possible Issues**:
- If next shift starts at 14:00, operators stay in old shift until 16:00
- This may not be desired behavior
- Very long delta+ periods delay shift transitions

**Recommendation**:
- Keep delta values reasonable (5-30 minutes)
- Document that large delta values delay transitions

### Edge Case 9: Delta = 0 (No Grace Period)

**This is the normal configuration since version 1.1.**

**Scenario**: Shift 06:00-14:00 with DeltaMinus = 0 and DeltaPlus = 0.

**Implications**:
- There is no delta window: an operator without permissions cannot log out, move station or be
  replaced by another operator without permissions at any time during the shift
- Operators leave the shift at a break start time or at the shift end (both automatic), or by scanning
  at a machine that has no shift running
- CANLOGOUTOFSHIFT operators can still log out or move at any time; CANREPLACEOPERATOR operators can
  still take over an occupied station
- With auto-logout off, the shift transition (Rule TM2) happens at the first check after the shift end

### Edge Case 10: Service Restart During Delta Period

**Scenario**:
- Operator logged into Shift A (06:00-14:00) at 06:30
- Service stops at 13:50 (within delta-)
- Service restarts at 13:55 (still within delta-)

**Expected Behavior**:
1. On service start, operator's login time is 06:30
2. Find shift covering 06:30 → Shift A (06:00-14:00)
3. Assign Shift A info to operator
4. Operator is kept logged in
5. Operator is now within delta- period (13:45-14:00)
6. Operator can logout manually

**Key Point**: Service start uses ORIGINAL login time to find covering shift, not restart time.

### Edge Case 11: Multiple Operators on Same Machine (Multiple Stations)

**Scenario**:
- Machine 100 has 3 stations (each with card reader)
- Shift A: 06:00-14:00 (Delta = 15 min)
- Operator 1 logged into Station 1 at 06:10
- Operator 2 logged into Station 2 at 06:20
- Operator 3 logged into Station 3 at 06:30

**Behavior**:
- Each operator is independently subject to shift rules
- Each has their own shift assignment (all have Shift A in this case)
- Each can logout independently based on their own shift
- At 14:00 (if auto-logout enabled):
  - All three operators are auto-logged out
  - All LEDs turn RED
- At a break start time of Shift A:
  - All three operators are logged out
- After 14:00 plus delta+ (if auto-logout disabled):
  - All three operators moved to Shift B (if it is running)
  - Each retains their original login time

**Key Point**: Multiple operators per machine are handled independently.

### Edge Case 12: Operator Without Shift Tries to Move Machines

**Scenario**:
- Operator logged into Machine A with NO shift info (logged in during gap)
- Operator scans card at Machine B (empty station)
- Machine B has an active shift

**Expected Behavior**:
1. Operator has no shift info → Can leave Machine A freely
2. Log out from Machine A
3. Log in at Machine B
4. Get current active shift at Machine B
5. Assign shift to operator
6. Operator now has shift info from Machine B

**Key Point**: Operators without shift info can move freely, then get assigned to new shift.

### Edge Case 13: CANREPLACEOPERATOR Permission and Empty Stations

**Scenario 1: Initial Login (Not Logged In Anywhere)**
- Operator A is NOT logged in anywhere
- Operator A has CANREPLACEOPERATOR permission
- Operator A scans at Machine Y (empty station)

**Expected Behavior**:
- ALLOW login to empty station
- This is initial login (Scenario A), not remote logout
- Log: "Login | Operator {A} logged into shift at empty station"

**Scenario 2: Remote Logout (Already Logged In Elsewhere)**
- Operator A logged into Machine X with Shift info, outside delta
- Operator A has CANREPLACEOPERATOR permission
- Operator A scans at Machine Y (empty station)

**Expected Behavior**:
- CANREPLACEOPERATOR requires someone to replace for remote logout
- Since Machine Y is empty, there's no one to replace
- DENY the move
- Turn LED ORANGE at Machine Y
- Log: "Remote Logout Denied | No one to replace at Machine Y"

**Exception**:
- If Operator A is within delta at Machine X, allow the move (delta permits logout)

**Key Point**: CANREPLACEOPERATOR allows initial login to any station (including empty), but remote logout/movement requires someone to replace at the target.

---

## Examples and Walkthroughs

> **Examples 1-8 use Delta = 15 minutes** to show how the delta window works. Since version 1.1 delta
> is 0 unless set by SQL: in these examples every "within delta" logout, move or replacement is then
> **refused**, and operators leave at a break or at the shift end. "Auto-logout: ENABLED" is the default;
> "DISABLED" means the setting was switched off by SQL. Example 9 shows a normal day under the 1.1 rules.

### Example 1: Basic Shift Day with Auto-Logout Enabled

**Setup**:
- Machine 100, Station 1
- Shift A: 06:00-14:00, Delta = 15 minutes
- Shift B: 14:00-22:00, Delta = 15 minutes
- Auto-logout: ENABLED
- Operator 50 (no special permissions)

**Timeline**:

| Time | Event | System Response | Operator State |
|------|-------|-----------------|----------------|
| 06:15 | Operator 50 scans card at Station 1 | Login successful, Shift A assigned, LED GREEN | Logged in, ActiveShiftId = Shift A |
| 10:00 | Operator 50 scans card at Station 1 (logout attempt) | Get Shift A from datalayer, outside delta, logout DENIED, LED ORANGE | Still logged in, ActiveShiftId = Shift A |
| 13:45 | Operator 50 scans card at Station 1 | Get Shift A from datalayer, within delta-, logout successful, LED RED | Logged out |
| *Alternative: Operator doesn't logout* | | | |
| 14:00 | Minute timer check runs | Get Shift A from datalayer, auto-logout triggered (shift ended), LED RED | Logged out, ActiveShiftId cleared |

**Alternative Timeline** (operator doesn't manually logout):

| Time | Event | System Response | Operator State |
|------|-------|-----------------|----------------|
| 06:15 | Operator 50 scans card | Login + Shift A assigned | Logged in, ActiveShiftId = Shift A |
| 13:50 | Minute timer check | Get Shift A from datalayer, no action (shift not ended) | Logged in, ActiveShiftId = Shift A |
| 14:00 | Minute timer check | Get Shift A from datalayer, **AUTO-LOGOUT** (shift ended) | Logged out, ActiveShiftId cleared |

### Example 2: Shift Transition with Auto-Logout Disabled

**Setup**:
- Machine 100, Station 1
- Shift A: 06:00-14:00, Delta = 15 minutes
- Shift B: 14:00-22:00, Delta = 15 minutes
- Auto-logout: DISABLED
- Operator 50 (no special permissions)

**Timeline**:

| Time | Event | System Response | Operator State |
|------|-------|-----------------|----------------|
| 06:15 | Operator 50 scans card | Login + Shift A assigned, LED GREEN | Logged in, ActiveShiftId = Shift A |
| 10:00 | Operator 50 scans card (logout attempt) | Query Shift A times, outside delta, logout DENIED, LED ORANGE | Still logged in, ActiveShiftId = Shift A |
| 13:50 | Operator 50 scans card | Query Shift A times, within delta-, logout successful, LED RED | Logged out |

**Alternative Timeline** (operator stays logged in):

| Time | Event | System Response | Operator State |
|------|-------|-----------------|----------------|
| 06:15 | Operator 50 scans card | Login + Shift A assigned | Logged in, Shift A (06:00-14:00) |
| 14:00 | Minute timer check | No auto-logout (disabled), no transition (within delta+) | Logged in, Shift A |
| 14:10 | Operator 50 scans card (logout attempt) | Within delta+, logout successful, LED RED | Logged out |

**Alternative Timeline** (operator remains through transition):

| Time | Event | System Response | Operator State |
|------|-------|-----------------|----------------|
| 06:15 | Operator 50 scans card | Login + Shift A assigned | Logged in, ActiveShiftId = Shift A |
| 14:00 | Minute timer check | No auto-logout (disabled), query Shift A times, still within delta+ | Logged in, ActiveShiftId = Shift A |
| 14:15 | Minute timer check | Query Shift A times, delta+ expired, Shift B active → **TRANSITION** to Shift B | Logged in, ActiveShiftId = Shift B |
| 14:20 | Operator 50 scans card (logout attempt) | Query Shift B times, outside delta, logout DENIED, LED ORANGE | Still logged in, ActiveShiftId = Shift B |
| 21:50 | Operator 50 scans card | Query Shift B times, within delta-, logout successful | Logged out |

### Example 3: Gap Between Shifts

**Setup**:
- Machine 100, Station 1
- Shift A: 06:00-14:00, Delta = 15 minutes
- **GAP: 14:15-16:00 (no shift)**
- Shift B: 16:00-22:00, Delta = 15 minutes
- Auto-logout: DISABLED
- Operator 50

**Timeline**:

| Time | Event | System Response | Operator State |
|------|-------|-----------------|----------------|
| 06:15 | Operator 50 scans card | Login + Shift A assigned | Logged in, ActiveShiftId = Shift A |
| 14:00 | Minute timer check | No auto-logout (disabled), query Shift A times, within delta+ | Logged in, ActiveShiftId = Shift A |
| 14:15 | Minute timer check | Query Shift A times, delta+ expired, no active shift → **CLEAR shift info** | Logged in, ActiveShiftId = null |
| 14:20 | Operator 50 scans card (logout attempt) | No shift info → logout allowed, LED RED | Logged out |

**Alternative** (operator stays logged in):

| Time | Event | System Response | Operator State |
|------|-------|-----------------|----------------|
| 14:15 | Minute timer check | Shift info cleared | Logged in, NO shift info |
| 15:00 | Operator 50 scans card (logout attempt) | No shift restrictions → logout allowed | Logged out |

**Alternative** (operator stays until Shift B):

| Time | Event | System Response | Operator State |
|------|-------|-----------------|----------------|
| 14:15 | Minute timer check | Shift info cleared | Logged in, ActiveShiftId = null |
| 16:00 | Minute timer check | Shift B started, operator has no shift → **LOGGED OUT** (shift start rule), LED RED | Logged out |
| 16:05 | Operator 50 scans card | Login + Shift B assigned, LED GREEN | Logged in, ActiveShiftId = Shift B |

### Example 4: Operator Replacement Scenario

**Setup**:
- Machine 100, Station 1 (MaxLogins = 1)
- Shift A: 06:00-14:00, Delta = 15 minutes
- Operator 50 (no special permissions) logged in at 06:15
- Operator 51 (has CANREPLACEOPERATOR) scans card at Station 1

**Timeline**:

| Time | Event | System Response | Result |
|------|-------|-----------------|--------|
| 06:15 | Operator 50 scans card | Login + Shift A assigned | Op 50 logged in, ActiveShiftId = Shift A |
| 10:00 | Operator 51 scans card | Query Op 50's Shift A times, outside delta, Op 51 has CANREPLACEOPERATOR → **REPLACE** | Op 50 logged out, Op 51 logged in, ActiveShiftId = Shift A |
| 10:05 | Operator 50 scans card again | Query Op 51's Shift A times, outside delta, Op 50 no permission → DENIED, LED ORANGE | Op 51 still logged in |
| 13:50 | Operator 50 scans card | Query Op 51's Shift A times, within delta- → **REPLACE** allowed | Op 51 logged out, Op 50 logged in, ActiveShiftId = Shift A |

### Example 5: Station Movement

**Setup**:
- Machine 100 with Station 1 and Station 2
- Shift A: 06:00-14:00, Delta = 15 minutes
- Operator 50 (no special permissions)

**Timeline**:

| Time | Event | System Response | Operator State |
|------|-------|-----------------|----------------|
| 06:15 | Operator 50 scans at Station 1 | Login + Shift A assigned | Logged in Station 1, ActiveShiftId = Shift A |
| 10:00 | Operator 50 scans at Station 2 | Query Shift A times, outside delta, no CANREPLACEOPERATOR → DENIED, LED ORANGE | Still at Station 1, ActiveShiftId = Shift A |
| 13:50 | Operator 50 scans at Station 2 | Query Shift A times, within delta- → **MOVEMENT ALLOWED** | Logged in Station 2, ActiveShiftId = Shift A |
| 14:00 | Minute timer (auto-logout enabled) | Query Shift A times, auto-logout at Station 2 | Logged out |

**Alternative** (with CANREPLACEOPERATOR):

| Time | Event | System Response | Operator State |
|------|-------|-----------------|----------------|
| 06:15 | Operator 50 scans at Station 1 | Login + Shift A assigned | Logged in Station 1, ActiveShiftId = Shift A |
| 10:00 | Operator 50 scans at Station 2 | Has CANREPLACEOPERATOR → **MOVEMENT ALLOWED** | Logged in Station 2, ActiveShiftId = Shift A |

### Example 6: Remote Logout with Replacement

**Setup**:
- Machine 100, Station 1: Operator 50 logged in with Shift A (06:00-14:00, Delta=15)
- Machine 200, Station 1: Operator 51 logged in with Shift C (06:00-14:00, Delta=15)
- Time: 10:00 (both outside delta)
- Operator 50 has CANREPLACEOPERATOR permission

**Timeline**:

| Time | Event | System Response | Result |
|------|-------|-----------------|--------|
| 10:00 | Operator 50 scans at Machine 200, Station 1 | Op 50 outside delta at Machine 100 BUT has CANREPLACEOPERATOR and replacing Op 51 → **ALLOWED** | Op 50 logged out from Machine 100, Op 51 logged out from Machine 200, Op 50 logged in at Machine 200 |

**Alternative** (Operator 50 has NO permission):

| Time | Event | System Response | Result |
|------|-------|-----------------|--------|
| 10:00 | Operator 50 scans at Machine 200, Station 1 | Op 50 outside delta, no CANREPLACEOPERATOR → DENIED, LED ORANGE | Both remain at their original machines |

### Example 7: Service Restart Recovery

**Setup**:
- Shift A: 06:00-14:00
- Shift B: 14:00-22:00
- Operator 50 logged in at 06:15 (into Shift A)
- Service stops at 10:00
- Service restarts at 16:30 (during Shift B)

**Service Start Logic**:

1. Load operator 50 from opstate
   - Machine: 100
   - Login Time: 06:15
   - No stored shift info (was only in memory)

2. Check if machine 100 exists → YES

3. Find shift covering login time 06:15:
   - Query: Which shift was active at Machine 100 at 06:15?
   - Result: Shift A (06:00-14:00)

4. Assign Shift A to operator:
   - `ActiveShiftId` = Shift A ID
   - `LoginShiftStart` = 06:00
   - `LoginShiftEnd` = 14:00

5. Keep operator logged in

**First Minute Timer Check** (straight after the start):

*Auto-logout on (the default):*

1. Operator 50 has Shift A info
2. Shift A is not running at 16:30 - it ended at 14:00 while the service was stopped
3. **AUTO-LOGOUT**: operator 50 is logged out, LED RED

**Result**: Operator 50 is logged out and scans in again to join Shift B.

*Auto-logout off (set by SQL):*

1. Operator 50 has Shift A info
2. Current time (16:30) > Shift A end (14:00) + DeltaPlus → delta+ has expired
3. Get current active shift at Machine 100 → Shift B (14:00-22:00)
4. **MOVE** operator to Shift B (`ActiveShiftId` = Shift B ID)

**Result**: Operator 50 is now in Shift B and can continue working.

### Example 8: Midnight Crossing Shift

**Setup**:
- Shift A: 22:00-06:00 (crosses midnight), Delta = 15 minutes
- Operator 50 logs in at 22:30

**Timeline**:

| Time | Event | System Response | Operator State |
|------|-------|-----------------|----------------|
| 22:30 | Operator 50 scans card | Login + Shift A assigned | Logged in, ActiveShiftId = Shift A |
| 23:00 | Operator 50 scans card (logout attempt) | Query Shift A times, outside delta (delta is 05:45-06:15) → DENIED | Still logged in, ActiveShiftId = Shift A |
| 05:50 (next day) | Operator 50 scans card | Query Shift A times, within delta- (05:45-06:00) → logout successful | Logged out |

**Alternative** (stay until auto-logout):

| Time | Event | System Response | Operator State |
|------|-------|-----------------|----------------|
| 22:30 | Operator 50 scans card | Login + Shift A assigned | Logged in, ActiveShiftId = Shift A |
| 06:00 (next day) | Minute timer check (auto-logout enabled) | Query Shift A times, current time >= shift end → **AUTO-LOGOUT** | Logged out |

**Alternative** (stay through delta+):

| Time | Event | System Response | Operator State |
|------|-------|-----------------|----------------|
| 22:30 | Operator 50 scans card | Login + Shift A assigned | Logged in, ActiveShiftId = Shift A |
| 06:15 (next day) | Minute timer check (auto-logout disabled) | Query Shift A times, delta+ expired, Shift B active → **TRANSITION** to Shift B | Logged in, ActiveShiftId = Shift B |

### Example 9: A Normal Day Under the Version 1.1 Rules

**Setup**:
- Machine 100, Station 1 (MaxLogins = 1) and Station 2
- Shift A: 06:00-14:00, break start times 09:30 and 12:00, Delta 0
- Auto-logout: on (default)
- Operator 50 (no special permissions), Operator 60 (CANLOGOUTOFSHIFT)

**Timeline**:

| Time | Event | System Response | Operator State |
|------|-------|-----------------|----------------|
| 05:58 | Operator 50 scans at Station 1 | No shift running → legacy login, LED GREEN | Logged in, no shift |
| 06:00 | Minute timer check | Shift A running, operator has no shift → **LOGGED OUT** (shift start rule), LED RED | Logged out |
| 06:02 | Operator 50 scans at Station 1 | Login + Shift A assigned, LED GREEN | Logged in, ActiveShiftId = Shift A |
| 08:00 | Operator 50 scans at Station 1 (logout attempt) | Held to Shift A → DENIED, LED ORANGE | Still logged in |
| 08:05 | Operator 50 scans at Station 2 (move attempt) | Held to Shift A → DENIED, LED ORANGE at Station 2 | Still at Station 1 |
| 08:10 | Operator 60 scans at Station 2, and again at 08:40 | Login + Shift A; second scan: CANLOGOUTOFSHIFT → logout allowed | Op 60 logged out |
| 09:30 | Minute timer check | Break → everyone on Shift A **LOGGED OUT**, LEDs RED | Logged out |
| 09:44 | Operator 50 scans at Station 2 | Login + Shift A assigned (any free station), LED GREEN | Logged in Station 2, ActiveShiftId = Shift A |
| 09:45 | Minute timer check | Break at 09:30 was more than 5 minutes ago, and login is later anyway → no action | Logged in |
| 12:00 | Minute timer check | Break → **LOGGED OUT** | Logged out |
| 12:01 | Operator 50 scans at Station 2 | Login + Shift A assigned (logged in after the break → not logged out again) | Logged in |
| 14:00 | Minute timer check | Shift A ended → **AUTO-LOGOUT**, LED RED | Logged out |

---

## Summary of Key Rules

### Always (whatever the AutoLogoutAtShiftEnd setting):
1. A login during a shift joins that shift; every logout clears the shift
2. An operator on a shift is held to it: no logout, move or replacement by an operator without
   permissions, except inside the delta window - and delta is 0 unless set by SQL
3. At each break start time everyone on the shift is logged out; logging back in is up to them
4. An operator logged in without a shift is logged out when a shift starts on their machine

### When Auto-Logout is ON (the default):
1. Operators are logged out AT shift end time (14:00)
2. Operators never reach the delta+ period (already logged out)

### When Auto-Logout is OFF (set by SQL):
1. Operators stay logged in past shift end
2. At shift end plus DeltaPlus:
   - IF another shift is running on the machine → Moved to that shift (held to it, subject to its breaks)
   - IF no shift is running → Shift info cleared, stay logged in; logged out when the next shift starts

### Permission Summary:
- **CANLOGOUTOFSHIFT**: Bypass shift logout restrictions on a card scan (can logout or move anytime)
- **CANREPLACEOPERATOR**: Can replace other operators OR move between machines during shift (only when replacing someone)
- Neither permission affects breaks, the shift start rule or the shift-end logout

### Critical Timing:
- **Shift Start**: Logins made without a shift are logged out here
- **Break Start Times**: Everyone on the shift is logged out here
- **Shift End (14:00)**: Auto-logout occurs here (if on)
- **Delta+ Expiration (shift end + DeltaPlus)**: Shift transitions occur here (auto-logout off)
- **Delta Window**: Manual logout allowed here - empty when delta is 0
- All of these are applied by the once-a-minute checks, on the first timer tick of the minute

### Multiple Operators per Machine:
- Each operator is independent
- Each has their own shift assignment
- Each subject to their own delta periods
- Station movement between stations on same machine follows same rules as replacement

---

## Implementation Checklist

### Core Storage
- [ ] Store only `ActiveShiftId` in operator state (never store shift start/end times)
- [ ] Get shift times from datalayer each time needed for calculations
- [ ] Handle shift-not-found scenario gracefully (datalayer returns null → treat as no shift info)

### Service Start Logic
- [ ] Recover operator state using original login time (from opstate)
- [ ] Get from datalayer: shift that covered original login time (not current time)
- [ ] Assign only `ActiveShiftId` to recovered operators

### Timer-Based Checks (Every Minute)
- [ ] Shift data re-read from the database every minute; a failed read keeps the previous data
- [ ] Order: refresh, shift start check, auto-logout check, shift transition check, break check
- [ ] The checks run before the same tick's card scans
- [ ] Shift start check logs out operators without a shift on a machine whose shift is running (any setting)
- [ ] Auto-logout check runs unless AutoLogoutAtShiftEnd is switched off; a missing setting row counts as on
- [ ] Auto-logout happens AT shift end time (not after delta+)
- [ ] Shift transitions happen AFTER delta+ expires; operators without a shift are never given one by the timer
- [ ] Break check logs out everyone on the shift who logged in before a break that started in the last 5 minutes
- [ ] Timed rules skip operators who are not at a station (machine ID <= 0)

### Card Scan Logic
- [ ] Get shift details from datalayer before each delta period check
- [ ] Assign only `ActiveShiftId` on login (never store times)
- [ ] Clear only `ActiveShiftId` on logout (no times to clear)

### Delta Calculations
- [ ] Always get shift object from datalayer to get current times and delta values
- [ ] Account for midnight crossing in all delta checks
- [ ] Delta- period: (shift end - DeltaMinus) to (shift end)
- [ ] Delta+ period: (shift end) to (shift end + DeltaPlus)
- [ ] DeltaMinus and DeltaPlus can have different values - retrieve both from shift object

### Permission System
- [ ] CANLOGOUTOFSHIFT bypasses all shift logout checks
- [ ] CANREPLACEOPERATOR only allows movement when replacing others (not general movement)
- [ ] CANREPLACEOPERATOR allows leaving current machine when replacing someone at target

### Logging & Feedback
- [ ] All shift decisions logged with clear feedback messages
- [ ] LED states: GREEN=logged in, RED=logged out, ORANGE=denied
- [ ] Log when shift not found (shift deleted scenario)
- [ ] Log when shift times are used (helpful for debugging)

### Datalayer Integration
- [ ] Datalayer provides method to get shift by ID (returns null if not found)
- [ ] Datalayer provides method to get active shift at machine NOW
- [ ] Datalayer provides method to get shift covering a specific login time (for service start)
- [ ] Datalayer is responsible for keeping shift data current
- [ ] Handle null results gracefully when datalayer returns null (shift deleted/not found)

### Station Capacity Management
- [ ] Implement `TotalLoggedInByMachineSubID(machineId, subId)` to get current login count
- [ ] Check station capacity before allowing login (current_count vs MaxLogins)
- [ ] Handle empty stations (current_count = 0)
- [ ] Handle stations with available slots (current_count < MaxLogins)
- [ ] Handle full stations (current_count >= MaxLogins)

### Two-Step Evaluation Process
- [ ] **STEP 1**: Evaluate if login at target station is viable (capacity + replacement checks)
- [ ] **STEP 2**: Evaluate if can leave current station (only if STEP 1 succeeds)
- [ ] CANLOGOUTOFSHIFT is checked in STEP 2, not STEP 1
- [ ] CANREPLACEOPERATOR allows leaving in STEP 2 when replacing someone in STEP 1

### Mass Replacement Logic
- [ ] When CANREPLACEOPERATOR used, replace ALL operators at target station
- [ ] Log out all operators at station (clear their shift info)
- [ ] Turn LED RED during mass logout
- [ ] Log in new operator with current active shift
- [ ] Turn LED GREEN after login
- [ ] Log mass replacement with operator count

### Prerequisite Checks
- [ ] Check UseShiftManagement = true before applying shift rules
- [ ] Check machine has active shift before applying shift rules
- [ ] If either false, use legacy logic (out of scope)
- [ ] Add prerequisite checks to ALL shift management scenarios

---

## Glossary

- **Active Shift**: A shift that is currently active (current time is between shift start and end)
- **Active Shift ID**: The ID of the shift assigned to an operator (stored in operator state)
- **Datalayer**: The layer responsible for managing and providing shift data to the application
- **DeltaMinus**: Grace period in minutes BEFORE shift end for manual logout
- **DeltaPlus**: Grace period in minutes AFTER shift end for manual logout and before shift transition
- **Delta- Period**: Time window before shift end: (shift end - DeltaMinus) to (shift end)
- **Delta+ Period**: Time window after shift end: (shift end) to (shift end + DeltaPlus)
- **Operator's Shift**: The shift currently assigned to an operator (identified by `ActiveShiftId`)
- **Get Shift from Datalayer**: Process of retrieving current shift details from datalayer using shift ID
- **Station**: A location on a machine with a card reader (multiple stations per machine possible)
- **Shift Transition**: Moving operator from one shift to another (happens after delta+ expires)
- **Auto-Logout**: Automatic logout when shift ends at shift end time (on unless `AutoLogoutAtShiftEnd` is switched off by SQL)
- **Break (Break Start Time)**: A time of day inside a shift at which everyone logged in on the shift is logged out
- **Shift Start Rule**: An operator logged in without a shift is logged out when a shift starts (or is found running) on their machine
- **Held to the Shift**: An operator on a shift cannot log out or move by card scan outside the delta window (which is empty when delta is 0)
- **Remote Logout**: Logging out from one machine to log into another
- **Replacement**: One operator replacing another at the same station
- **Shift Not Found**: When datalayer returns null for a shift ID (shift was deleted or doesn't exist)
- **Midnight Crossing**: A shift where start time > end time (e.g., 22:00-06:00)

---

**END OF SPECIFICATION**
