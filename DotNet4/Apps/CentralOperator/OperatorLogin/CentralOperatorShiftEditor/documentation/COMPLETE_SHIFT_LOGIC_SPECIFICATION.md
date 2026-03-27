# COMPLETE SHIFT MANAGEMENT LOGIC SPECIFICATION

**Version:** 1.0
**Last Updated:** 2025-09-20
**Status:** Comprehensive Specification

---

## Table of Contents

1. [Core Concepts](#core-concepts)
2. [Global Settings](#global-settings)
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
- **DeltaMinus** (minutes): Grace period BEFORE shift end for manual logout
- **DeltaPlus** (minutes): Grace period AFTER shift end for manual logout and shift transitions
- **Machine Assignments**: Which machines use this shift

**IMPORTANT**: Shifts are defined by TIME ONLY, not dates. A shift cannot span multiple days (e.g., no 48-hour shifts). Times are compared against server time.

### Delta Periods

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

### Operator State

Each operator logged into a machine has:
- **Machine ID**: Which machine they're logged into
- **Station ID**: Which station on that machine (multiple stations per machine possible)
- **Login Time**: When they originally logged in
- **Active Shift ID**: The shift they were assigned at login (null if no shift was active)

**IMPORTANT**: Only the shift ID is stored. Shift start/end times are **retrieved from the datalayer** each time they're needed for delta calculations. This ensures operators always use the current shift configuration.

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

1. **`UseShiftManagement = true`** (database setting)
2. **Machine has an active shift assigned**

**If EITHER condition is false:**
- All shift management logic is bypassed
- Legacy operator login/logout logic applies
- This is **OUT OF SCOPE** for this specification

**This prerequisite applies to:**
- Auto-logout checks
- Shift transition checks
- Delta period checks
- All card scan scenarios (A, B, C, D)
- All replacement and movement rules

### UseShiftManagement
- **Type**: Boolean (database setting)
- **Purpose**: Master switch for shift management
- **When FALSE**: Legacy logic applies (OUT OF SCOPE)
- **When TRUE**: Shift rules apply (IF machine has active shift)

### AutoLogoutAtShiftEnd
- **Type**: Boolean (database setting)
- **Purpose**: Controls automatic logout when shift ends
- **When FALSE**: Operators must manually logout, can stay logged in past shift end
- **When TRUE**: Operators are automatically logged out when their shift ends (at shift end time, NOT after delta+)

### Shift Configuration Changes
- **IMPORTANT**: Changing from "using shifts" to "not using shifts" (or vice versa) requires a service restart
- On restart, the SERVICE START rules apply

---

## ON SERVICE START Logic

When the service starts, it must recover operator states from the database (opstate).

### Rule SS1: Operator Was Logged In Before Service Stopped

**FOR EACH** operator in opstate with a login record:

1. **Find the machine** the operator was logged into (from opstate)

2. **Check if machine still exists**
   - **IF machine no longer exists**:
     - Log operator out
     - Log: "Service Start | Operator {id} logged out - machine {machineId} no longer exists"
     - DONE

3. **Determine if there was a shift covering the operator's original login time**
   - Use the operator's **original login time** (when they first logged in, NOT current time)
   - Get from datalayer: "Was there a shift assigned to this machine that was active at {original login time}?"

4. **IF THERE IS A SHIFT covering the login time**:
   - Assign that shift to the operator:
     - `ActiveShiftId` = shift ID
   - Keep operator logged in
   - Log: "Service Start | Operator {id} assigned to shift {shiftId}"
   - NOTE: Shift times will be retrieved from datalayer when needed for delta calculations

5. **IF NO SHIFT covered the login time**:
   - Determine if the machine has a **current active shift** (active NOW at service start time)

   - **IF THIS MACHINE IS NOT USING SHIFTS**:
     - Keep operator logged in with no shift info
     - `ActiveShiftId` = null
     - Log: "Service Start | Operator {id} kept logged in - machine not using shifts"

   - **IF THERE IS AN ACTIVE MACHINE SHIFT** (now):
     - Log operator out
     - Log: "Service Start | Operator {id} logged out - did not log into current active shift"
     - Rationale: They were logged in during a different period and never joined this current shift

---

## EVERY MINUTE Timer Logic

The service runs these checks **once per minute** (when the minute changes).

### Order of Operations

**CRITICAL**: Execute in this exact order:

1. **Auto-Logout Check** (if enabled)
2. **Shift Transition Check**

### Rule TM1: Auto-Logout Check

**ONLY IF** `AutoLogoutAtShiftEnd` is enabled:

**FOR EACH** logged-in operator:

1. **Check if operator has shift info**
   - IF `ActiveShiftId` is null → SKIP (no shift to auto-logout from)

2. **Get shift details from datalayer**
   - Get shift object using `ActiveShiftId`
   - IF shift not found (datalayer returns null) → Clear operator's `ActiveShiftId` and SKIP
   - Get shift start time, shift end time from shift object

3. **Determine if shift has ended**
   - Get current time (server time)
   - Compare to shift end time
   - Account for midnight crossing (if shift start > shift end)

4. **Check if we are AT OR PAST shift end time**
   - **Normal shift** (e.g., 06:00-14:00):
     - IF current time >= 14:00:00 → Shift has ended

   - **Midnight-crossing shift** (e.g., 22:00-06:00):
     - IF shift start > shift end (22:00 > 06:00) → crosses midnight
     - IF current time >= shift end AND current time < shift start → Shift has ended
     - Example: At 06:01, shift has ended

5. **IF shift has ended**:
   - Log operator out immediately
   - Set LED to RED at their station
   - `IsActiveStation` = false
   - Clear shift info:
     - `ActiveShiftId` = null
   - Log: "Auto-Logout | Operator {id} logged out - shift ended at {shiftEnd}"

**IMPORTANT**: Auto-logout happens at shift end time (14:00), NOT after delta+ expires (14:15).

### Rule TM2: Shift Transition Check

**ONLY IF** `AutoLogoutAtShiftEnd` is **disabled** OR operator survived auto-logout:

**FOR EACH** logged-in operator:

1. **Check if operator has shift info**
   - IF `ActiveShiftId` is null → Check if should assign new shift (Rule TM2.2)
   - IF `ActiveShiftId` is not null → Check if should transition (Rule TM2.1)

#### Rule TM2.1: Transition from Current Shift

**IF operator has shift info:**

1. **Get current shift details from datalayer**
   - Get shift object using operator's `ActiveShiftId`
   - IF shift not found (datalayer returns null) → Clear `ActiveShiftId` and treat as no shift (go to Rule TM2.2)
   - Get shift end time, DeltaMinus, and DeltaPlus from shift object

2. **Check if still within delta+ period**
   - Current time <= (shift end time + DeltaPlus)
   - Account for midnight crossing
   - IF YES → SKIP (still in current shift's grace period)

3. **Delta+ has expired** - determine next action:

   **Get the current active shift for this machine:**
   - Get from datalayer: active shift at this machine RIGHT NOW

   **IF there IS a current active shift:**
   - Transition operator to new shift:
     - `ActiveShiftId` = new shift ID
   - Operator remains logged in
   - Log: "Shift Transition | Operator {id} transitioned from shift {oldId} to shift {newId}"

   **IF there IS NO current active shift:**
   - Clear operator's shift info:
     - `ActiveShiftId` = null
   - Operator remains logged in (no shift restrictions)
   - Log: "Shift Transition | Operator {id} shift info cleared - no active shift"

#### Rule TM2.2: Assign Shift to Operator Without Shift Info

**IF operator has NO shift info** (`ActiveShiftId` is null):

1. **Get the current active shift for this machine**
   - Get from datalayer: active shift RIGHT NOW

2. **IF there IS a current active shift:**
   - Assign shift to operator:
     - `ActiveShiftId` = shift ID
   - Log: "Shift Assignment | Operator {id} assigned to shift {shiftId} - shift started while logged in"

3. **IF there IS NO current active shift:**
   - No action needed
   - Operator continues without shift info

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

### Decision Tree 4: Auto-Logout Check (Every Minute)

```
EVERY MINUTE CHECK
│
├─ AutoLogoutAtShiftEnd enabled?
│  │
│  ├─ NO → Skip to Shift Transition Check
│  │
│  └─ YES → FOR EACH logged-in operator:
│        │
│        ├─ Does operator have shift info?
│        │  │
│        │  ├─ NO → Skip this operator
│        │  │
│        │  └─ YES → Is current time >= LoginShiftEnd?
│        │        │  (Account for midnight crossing)
│        │        │
│        │        ├─ NO → Skip this operator
│        │        │
│        │        └─ YES → AUTO-LOGOUT operator immediately
│        │                 Clear shift info
│        │                 Turn LED RED
│
└─ SHIFT TRANSITION CHECK (for operators who weren't auto-logged out)
```

### Decision Tree 5: Shift Transition Check (Every Minute)

```
SHIFT TRANSITION CHECK (runs after auto-logout check)
│
FOR EACH logged-in operator:
│
├─ Does operator have shift info?
│  │
│  ├─ NO → Get current active shift at operator's machine
│  │     │
│  │     ├─ Active shift exists?
│  │     │  ├─ YES → Assign shift to operator
│  │     │  └─ NO  → No action
│  │
│  └─ YES → Is current time > (LoginShiftEnd + Delta)?
│        │  (Delta+ period expired?)
│        │
│        ├─ NO → Skip (still in delta+ grace period)
│        │
│        └─ YES → Get current active shift at operator's machine
│              │
│              ├─ Active shift exists?
│              │  ├─ YES → TRANSITION operator to new shift
│              │  │         Update ActiveShiftId, LoginShiftStart, LoginShiftEnd
│              │  │
│              │  └─ NO  → CLEAR shift info
│              │           Set ActiveShiftId = null
│              │           Operator stays logged in without shift restrictions
```

### Decision Tree 6: Service Start Recovery

```
ON SERVICE START
│
FOR EACH operator in opstate with login record:
│
├─ Does operator's machine still exist?
│  │
│  ├─ NO → Log operator out
│  │
│  └─ YES → Was there a shift covering operator's ORIGINAL login time?
│        │  (Use LoginTime from opstate)
│        │
│        ├─ YES → Assign that shift to operator
│        │        (ActiveShiftId, LoginShiftStart, LoginShiftEnd)
│        │        Keep operator logged in
│        │
│        └─ NO → Is there a current active shift NOW?
│              │
│              ├─ NO → Is machine using shifts?
│              │     │
│              │     ├─ NO  → Keep operator logged in (no shift info)
│              │     └─ YES → Log operator out (no shift to join)
│              │
│              └─ YES → Log operator out
│                       (They never joined this current shift)
```

---

## Edge Cases and Special Scenarios

### Edge Case 1: Shift Not Found by ID

**Scenario**: Operator has `ActiveShiftId` = 123, but shift 123 no longer exists in database (deleted).

**Handling**:
- When attempting to get shift by ID, query returns null
- Treat operator as having NO shift info
- Clear shift info:
  - `ActiveShiftId` = null
  - `LoginShiftStart` = null
  - `LoginShiftEnd` = null
- Apply no-shift rules (operator can logout freely)
- Log: "Shift Error | Shift {123} not found - clearing operator {id} shift info"

**When checked**:
- During delta period calculations
- During auto-logout checks
- During shift transition checks

### Edge Case 2: Shift Times Modified While Operator Logged In

**Scenario**: Operator logged into Shift A (06:00-14:00) at 06:30, admin changes Shift A to (07:00-15:00) at 10:00.

**Handling** (with shift ID-only storage):
- Operator has `ActiveShiftId` = Shift A ID stored
- When system needs to check delta at 13:00:
  1. Get Shift A object from datalayer using the shift ID
  2. Get current shift times from shift object: 07:00-15:00 (NEW times)
  3. Calculate delta based on NEW times (15:00 end time, not 14:00)
  4. Operator can now logout between 14:45-15:15 (not 13:45-14:15)
- Operators **immediately use updated shift times** after admin makes changes
- Datalayer is responsible for keeping shift data current

**Benefits**:
- No need to notify operators or reload state
- Changes take effect immediately on next delta check (datalayer provides updated data)
- Consistent behavior across all operators in the shift

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
- Shift A: 06:00-14:00 (Delta = 15 min)
- **GAP**: 14:15-16:00 (no shift)
- Shift B: 16:00-22:00

**Timeline for Operator logged into Shift A at 06:30**:

| Time | Auto-Logout Enabled | Auto-Logout Disabled |
|------|---------------------|----------------------|
| 13:45 | Within delta-, can logout | Within delta-, can logout |
| 14:00 | **AUTO-LOGOUT occurs** | Within delta+, can logout |
| 14:10 | (already logged out) | Within delta+, can logout |
| 14:15 | (already logged out) | Delta+ expires → Shift info cleared, stays logged in |
| 14:20 | (logged out) | No shift info, no restrictions |
| 16:00 | (logged out) | Shift B starts → Shift B assigned to operator |

**Key Point**: After delta+ expires with no new shift, operator stays logged in but shift info is cleared.

### Edge Case 6: Back-to-Back Shifts (No Gap)

**Scenario**:
- Shift A: 06:00-14:00 (Delta = 15 min)
- Shift B: 14:00-22:00 (starts exactly when A ends)

**Timeline for Operator logged into Shift A at 06:30**:

| Time | Auto-Logout Enabled | Auto-Logout Disabled |
|------|---------------------|----------------------|
| 13:45 | Within delta-, can logout | Within delta-, can logout |
| 14:00 | **AUTO-LOGOUT occurs** | Within delta+, can logout, Shift B is now active |
| 14:10 | (already logged out) | Within delta+, can logout, still in Shift A info |
| 14:15 | (already logged out) | Delta+ expires → **Transition to Shift B** |
| 14:20 | (logged out) | In Shift B, subject to Shift B delta rules |

**Key Point**: With auto-logout disabled, operator transitions to Shift B after delta+ expires.

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

**Scenario**: Shift 06:00-14:00 with Delta = 0 minutes.

**Implications**:
- Delta- period: NONE (14:00-14:00 = 0 duration)
- Delta+ period: NONE (14:00-14:00 = 0 duration)
- Operators can ONLY logout exactly at 14:00:00
- Auto-logout happens at 14:00:00
- Shift transition happens immediately at 14:00:00

**Handling**:
- System should still work correctly
- Very restrictive - operators effectively cannot manually logout
- Not recommended unless operators have CANLOGOUTOFSHIFT permission

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
- At 14:16 (if auto-logout disabled):
  - All three operators transitioned to Shift B (if exists)
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
| 16:00 | Minute timer check | Shift B started → **ASSIGN Shift B** to operator | Logged in, ActiveShiftId = Shift B |
| 16:05 | Operator 50 scans card (logout attempt) | Query Shift B times, outside delta, DENIED | Still logged in, ActiveShiftId = Shift B |

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

**Next Minute Timer Check** (16:31):

1. Operator 50 has Shift A info
2. Current time (16:31) > Shift A end (14:00) + Delta (15 min) = 14:15
3. Delta+ has expired
4. Get current active shift at Machine 100 → Shift B (14:00-22:00)
5. **TRANSITION** operator to Shift B:
   - `ActiveShiftId` = Shift B ID
   - `LoginShiftStart` = 14:00
   - `LoginShiftEnd` = 22:00

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

---

## Summary of Key Rules

### When Auto-Logout is ENABLED:
1. Operators are logged out AT shift end time (14:00)
2. Delta- period allows manual logout before shift ends
3. Operators never reach delta+ period (already logged out)

### When Auto-Logout is DISABLED:
1. Operators can stay logged in past shift end
2. Delta- and delta+ both allow manual logout
3. After delta+ expires (14:15):
   - IF new shift active → Transition to new shift
   - IF no shift active → Clear shift info, stay logged in

### Permission Summary:
- **CANLOGOUTOFSHIFT**: Bypass all shift logout restrictions (can logout anytime)
- **CANREPLACEOPERATOR**: Can replace other operators OR move between machines during shift (only when replacing someone)

### Critical Timing:
- **Shift End (14:00)**: Auto-logout occurs here (if enabled)
- **Delta+ Expiration (14:15)**: Shift transitions occur here
- **Delta Window (13:45-14:15)**: Manual logout allowed here

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
- [ ] Auto-logout check runs every minute (if enabled)
- [ ] Get shift details from datalayer before each auto-logout check
- [ ] Shift transition check runs every minute (after auto-logout check)
- [ ] Get shift details from datalayer before each transition check
- [ ] Auto-logout happens AT shift end time (not after delta+)
- [ ] Shift transitions happen AFTER delta+ expires

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
- **Auto-Logout**: Automatic logout when shift ends at shift end time (if enabled)
- **Remote Logout**: Logging out from one machine to log into another
- **Replacement**: One operator replacing another at the same station
- **Shift Not Found**: When datalayer returns null for a shift ID (shift was deleted or doesn't exist)
- **Midnight Crossing**: A shift where start time > end time (e.g., 22:00-06:00)

---

**END OF SPECIFICATION**
