# REVISED SHIFT MANAGEMENT TEST PLAN (1-HOUR + OVERNIGHT)

**Version:** 3.0
**Last Updated:** 2026-10-09
**Purpose:** Complete test coverage for all shift management rules - optimized for 1-hour daytime testing + overnight midnight-crossing tests

**v3.0 changes (rules agreed 1 October 2026 - service 1.3.0, Shift Editor 1.0.2):**
- Logout at shift end is on unless switched off by SQL; the setting and the Delta values are no longer
  in the Shift Editor. Tests that need Delta or the setting off now set them **by SQL** (see Prerequisites).
- New shift start rule: an operator logged in without a shift is logged out when a shift starts on
  the machine, whatever the setting. TM1.6, TM2.3, SS1.3 and LP.7 changed accordingly; the old
  "assign the shift and stay logged in" behaviour is gone.
- New **Part D**: break times, the default setting, and what the Shift Editor shows.
- Shift Editor changes reach the service within about a minute - no restart between tests.
- Log messages updated to the current wording. Routine once-a-minute lines are no longer logged: a
  check that does nothing writes nothing.

**Part C (legacy parity) must pass before any site install:** wherever shift rules don't apply, the
service must behave exactly like the non-shift service.

---

## OVERVIEW

This test plan is divided into four parts:
1. **PART A: Daytime 1-Hour Test** - All core functionality tests (Sections 1-8)
2. **PART B: Overnight Midnight-Crossing Tests** - 3 tests spanning midnight (5 min setup + 15 min verification)
3. **PART C: Legacy Parity Tests** - behaviour where shift rules don't apply (~15 minutes)
4. **PART D: Breaks, Defaults and the Shift Editor** - break times, logout at shift end by default, hidden settings (~30 minutes)

---

## TEST ENVIRONMENT SETUP

### Prerequisites
1. **UseShiftManagement** = true (in the service's `CentralLoginService.exe.config` - set by the Shift installer; it is not a database setting)
2. **Test Shifts Configured**:
   - **DAYTIME TESTS:**
     - Shift A: 10:00-10:20, DeltaMinus=2 min, DeltaPlus=2 min
     - Shift B: 10:20-10:40, DeltaMinus=2 min, DeltaPlus=2 min
   - **OVERNIGHT TESTS:**
     - Shift C: 16:00-10:00 (next day), DeltaMinus=15 min, DeltaPlus=10 min
   - **Delta is set by SQL** - the Shift Editor creates shifts with 0 and doesn't show the values. After
     creating the shifts in the editor:
     `UPDATE JEGR_Utils.dbo.tblShifts SET DeltaMinus = 2, DeltaPlus = 2 WHERE ShiftName IN ('Shift A', 'Shift B')`
     (and 15 / 10 for Shift C). Parts A and B test the delta rules, which the service still applies to
     stored values; Part D tests the normal case, delta 0.
   - **No break times** on Shifts A, B and C (Part D uses its own shifts)
   - **`AutoLogoutAtShiftEnd` is set by SQL** where a test says true or false - it is not in the Shift Editor:
     `UPDATE JEGR_Utils.dbo.tblShiftSettings SET SettingValue = 'False' WHERE SettingName = 'AutoLogoutAtShiftEnd'`
     (`'True'` to switch it back on). The service picks the change up within a minute and logs
     "[AutoLogout] Logout at shift end is ON" or "... is OFF"
3. **Test Machines**:
   - Machine 100: Assigned to Shift A/B (daytime), Shift C (overnight)
   - Machine 100, Station 1: MaxLogins = 1
   - Machine 100, Station 2: MaxLogins = 3 (multi-operator station)
   - Machine 200: Assigned to Shift A/B (daytime)
   - Machine 200, Station 1: MaxLogins = 1
   - Machine 300: NOT assigned to any shift (used for Part C and A.4.2)
   - Machine 300, Station 1: MaxLogins = 1; Station 2: MaxLogins = 2
4. **Test Operators**:
   - Operator 1: No special permissions
   - Operator 2: Has CANLOGOUTOFSHIFT permission
   - Operator 3: Has CANREPLACEOPERATOR permission
   - Operator 4: Has both CANLOGOUTOFSHIFT and CANREPLACEOPERATOR
   - Operator 5: No special permissions

### Time Periods for Daytime Tests (Shift A: 10:00-10:20)
- **During Shift**: 10:05 (mid-shift, outside delta)
- **Within Delta-**: 10:18 (within 10:18-10:20)
- **At Shift End**: 10:20
- **Within Delta+**: 10:21 (within 10:20-10:22)
- **After Delta+ Expires**: 10:23 (after grace period)

---

# PART A: DAYTIME 1-HOUR TEST

**Start Time:** 10:00 AM
**End Time:** 11:00 AM
**Total Duration:** 60 minutes

---

## SECTION 1: SCENARIO A - Operator Not Logged In Anywhere (10:00-10:15)

### Test Group A.1: Empty Station and Available Slot Login (Rules A.1.1, A.1.2)

#### TEST A.1.1: Basic Login to Empty Station
**Time:** 10:02 (during Shift A)
**Setup:**
- Machine 100, Station 1 is empty
- Operator 1 scans card

**Expected Result:**
- ✅ Login successful
- ✅ LED turns GREEN
- ✅ Operator assigned to Shift A
- ✅ Log: "[SCENARIO A - Rule A.1.1] Login to empty station"

**Verification:**
- Check `ActiveShiftId` = Shift A ID
- Check `IsActiveStation` = true

---

#### TEST A.1.2: Login to Multi-Operator Station (2 operators)
**Time:** 10:03
**Setup:**
- Machine 100, Station 2 (MaxLogins=3), Operator 1 already logged in
- Operator 5 scans card

**Expected Result:**
- ✅ Login successful
- ✅ LED stays GREEN
- ✅ Operator 5 assigned to Shift A
- ✅ Log: "[SCENARIO A - Rule A.1.2] Login to available slot (2/3)"
- ✅ Operator 1 remains logged in

---

### Test Group A.2: Mass Replacement with CANREPLACEOPERATOR (Rule A.2)

#### TEST A.2.1: Replace Single Operator (CANREPLACEOPERATOR)
**Time:** 10:05 (outside delta)
**Setup:**
- Machine 100, Station 1: Operator 1 logged in
- Operator 3 (has CANREPLACEOPERATOR) scans card

**Expected Result:**
- ✅ Operator 1 logged out
- ✅ LED briefly RED, then GREEN
- ✅ Operator 3 logged in and assigned to Shift A
- ✅ Log: "[SCENARIO A - Rule A.2] Mass Replacement: Operator 3 replacing 1 operators (CANREPLACEOPERATOR)"

---

#### TEST A.2.2: Mass Replace Multiple Operators (CANREPLACEOPERATOR)
**Time:** 10:06
**Setup:**
- Machine 100, Station 2: Operators 1, 5, and 2 logged in (from earlier + add Operator 2)
- Operator 3 (has CANREPLACEOPERATOR) scans Station 2

**Expected Result:**
- ✅ ALL three operators logged out
- ✅ LED briefly RED, then GREEN
- ✅ Operator 3 logged in
- ✅ Log: "[SCENARIO A - Rule A.2] Mass Replacement: Operator 3 replacing 3 operators (CANREPLACEOPERATOR)"

---

#### TEST A.2.3: CANREPLACEOPERATOR at Empty Station - Initial Login ALLOWED
**Time:** 10:07
**Setup:**
- Machine 100, Station 1 is empty (Operator 3 still at Station 2)
- Logout Operator 3 from Station 2
- Operator 3 scans Station 1 (now empty)

**Expected Result** (rule changed 2025-10-29: CANREPLACEOPERATOR only blocks *moving* to an empty
station - see C.2.1 / D.2.1 - not an initial login):
- ✅ Login successful
- ✅ LED turns GREEN
- ✅ Operator 3 assigned to Shift A
- ✅ Log: "[ShiftManagement] Login to empty station"

---

### Test Group A.3: Station Full - Replacement Eligibility (Rule A.3)

#### TEST A.3.1: Station Full, No One Within Delta, No Permission - DENIED
**Time:** 10:08 (outside delta)
**Setup:**
- Login Operator 1 at Machine 100, Station 1
- Operator 5 (no special permissions) scans Station 1

**Expected Result:**
- ❌ Login DENIED
- ✅ LED turns ORANGE
- ✅ Operator 1 remains logged in
- ✅ Log: "[SCENARIO A] Login DENIED: Station full (1/1), no operators within delta, no CANREPLACEOPERATOR"

---

#### TEST A.3.2: Replace Operator Within Delta
**Time:** 10:18 (within Delta-: 10:18-10:20)
**Setup:**
- Machine 100, Station 1: Operator 1 still logged in
- Operator 5 scans card

**Expected Result:**
- ✅ Operator 1 logged out (within delta allows replacement)
- ✅ LED briefly RED, then GREEN
- ✅ Operator 5 logged in and assigned to Shift A
- ✅ Log: "[SCENARIO A - Rule A.3] Replacing Operator 1 (within delta)"

---

### Test Group A.4: Legacy Mode

#### TEST A.4.1: Login When Machine Has No Active Shift
**Time:** 10:11 (create temporary gap by removing shift assignment)
**Setup:**
- Temporarily unassign shifts from Machine 200
- Machine 200, Station 1 is empty
- Operator 1 scans card

**Expected Result:**
- ✅ Login successful (legacy logic)
- ✅ LED turns GREEN
- ✅ Operator NOT assigned to shift (`ActiveShiftId` = null)
- ✅ Log: "[ShiftManagement] Shift management disabled or no active shift - using legacy logic"

**Cleanup:** Log Operator 1 out; re-assign shifts to Machine 200

---

#### TEST A.4.2: Leave a Shift Machine by Scanning an Unassigned Machine
**Time:** 10:13
**Setup:**
- Operator 5 logged in at Machine 100, Station 1 (Shift A, outside delta, no permissions)
- Operator 5 scans Machine 300, Station 1 (no shift assigned)

**Expected Result** (current design: shift rules are decided by the machine being *scanned* -
confirm with the customer this is acceptable during a pilot on some machines only):
- ✅ Operator 5 logged out of Machine 100 (LED RED) and logged in at Machine 300 (LED GREEN)
- ✅ `ActiveShiftId` = null
- ✅ Log: "[ShiftManagement] Shift management disabled or no active shift - using legacy logic"

**Cleanup:** Log Operator 5 out

---

## SECTION 2: SCENARIO B - Operator Scans Own Station (Logout) (10:15-10:25)

### Test Group B.1: Logout With Shift Info

#### TEST B.1.1: Logout Within Delta- Period
**Time:** 10:18 (within Delta-: 10:18-10:20)
**Setup:**
- Machine 100, Station 1: Operator 5 logged in (from TEST A.3.2)
- Operator 5 scans own station

**Expected Result:**
- ✅ Logout successful
- ✅ LED turns RED
- ✅ `ActiveShiftId` cleared (null)
- ✅ Log: "[Logout] Operator 5 logged out (within delta period)"

---

#### TEST B.1.2: Logout Outside Delta - DENIED
**Time:** 10:09 (during shift, outside delta)
**Setup:**
- Login Operator 1 at Machine 100, Station 1
- Operator 1 scans own station

**Expected Result:**
- ❌ Logout DENIED
- ✅ LED turns ORANGE for 5 seconds
- ✅ Operator remains logged in
- ✅ Log: "[Logout Denied] Operator 1 outside delta period, no CANLOGOUTOFSHIFT permission"

---

#### TEST B.1.3: Logout Outside Delta with CANLOGOUTOFSHIFT
**Time:** 10:10 (outside delta)
**Setup:**
- Logout Operator 1 from Station 1 (use Operator 2 to replace them with CANREPLACEOPERATOR, or wait until delta)
- Login Operator 2 (has CANLOGOUTOFSHIFT) at Machine 100, Station 1
- Operator 2 scans own station

**Expected Result:**
- ✅ Logout successful
- ✅ LED turns RED
- ✅ Log: "[Logout] Operator 2 logged out (CANLOGOUTOFSHIFT permission)"

---

#### TEST B.1.4: Logout Within Delta+ Period
**Time:** 10:21 (within Delta+: 10:20-10:22, Shift A ended, Shift B started)
**Setup:**
- Login Operator 1 at Machine 100, Station 1 (Shift B now active)
- Wait until 10:21
- Operator 1 scans own station

**Expected Result:**
- ✅ Logout successful (still within Delta+ of Shift B start)
- ✅ LED turns RED
- ✅ Log: "[Logout] Operator 1 logged out (within delta period)"

---

### Test Group B.2: Shift Not Found (Orphaned Reference)

#### TEST B.2.1: Logout When Shift Deleted
**Time:** 10:12
**Setup:**
- Manually set Operator 1's `ActiveShiftId` = 999 (non-existent shift) in database
- Operator 1 scans own station

**Expected Result:**
- ✅ Logout successful
- ✅ LED turns RED
- ✅ Log: "[Logout] Shift ID 999 not found - clearing orphaned reference"

---

## SECTION 3: SCENARIO C - Same Machine, Different Station (10:25-10:35)

### Test Group C.1: Two-Step Process - Both Steps Pass

#### TEST C.1.1: Move to Empty Station Within Delta
**Time:** 10:18 (within Delta-)
**Setup:**
- Login Operator 1 at Machine 100, Station 1
- Machine 100, Station 2 is empty
- Operator 1 scans Station 2

**Expected Result:**
- ✅ Movement successful
- ✅ Station 1: LED RED (logged out)
- ✅ Station 2: LED GREEN (logged in)
- ✅ `ActiveShiftId` retained (Shift A)
- ✅ Log: "[SCENARIO C] Station movement complete: Machine 100, Sub 1 -> Sub 2"

---

#### TEST C.1.2: Move to Station with Available Slot Within Delta
**Time:** 10:19
**Setup:**
- Login Operator 5 at Machine 100, Station 2 (join Operator 1)
- Operator 1 still at Station 2 from previous test
- Operator 5 scans Station 2

**Expected Result:**
- ✅ Both operators at Station 2 (2/3)
- ✅ LED stays GREEN

---

#### TEST C.1.3: Move with CANLOGOUTOFSHIFT Outside Delta
**Time:** 10:26 (Shift B active, outside delta)
**Setup:**
- Login Operator 2 (CANLOGOUTOFSHIFT) at Machine 100, Station 1
- Machine 100, Station 2 has Operators 1 and 5
- Operator 2 scans Station 2

**Expected Result:**
- ✅ Movement successful (CANLOGOUTOFSHIFT bypasses delta check)
- ✅ Operator 2 logged in at Station 2 (3/3, station full)
- ✅ Log: "[STEP2] Has CANLOGOUTOFSHIFT permission - can leave"

---

#### TEST C.1.4: Replace Multiple Operators with CANREPLACEOPERATOR
**Time:** 10:27
**Setup:**
- Login Operator 3 (CANREPLACEOPERATOR) at Machine 100, Station 1
- Machine 100, Station 2: Operators 1, 5, 2 all logged in (3/3)
- Operator 3 scans Station 2

**Expected Result:**
- ✅ All three operators at Station 2 logged out
- ✅ Operator 3 logged out from Station 1
- ✅ Operator 3 logged in at Station 2
- ✅ Log: "[SCENARIO C] Mass replacing 3 operators at target station (CANREPLACEOPERATOR)"

---

### Test Group C.2: STEP 1 or STEP 2 Fails

#### TEST C.2.1: Move to Empty Station with CANREPLACEOPERATOR - DENIED
**Time:** 10:28
**Setup:**
- Operator 3 at Station 2
- Station 1 is empty
- Operator 3 scans Station 1

**Expected Result:**
- ❌ Movement DENIED (STEP 1 fails)
- ✅ LED turns ORANGE at Station 1
- ✅ Operator 3 remains at Station 2
- ✅ Log: "[SCENARIO C - STEP 1 FAILED] Cannot login at target: CANREPLACEOPERATOR requires someone to replace - station is empty"

---

#### TEST C.2.2: Cannot Leave Current Station Outside Delta
**Time:** 10:29 (outside delta)
**Setup:**
- Login Operator 1 at Machine 100, Station 1
- Logout Operator 3 from Station 2 (use CANLOGOUTOFSHIFT)
- Station 2 now empty
- Operator 1 scans Station 2

**Expected Result:**
- ❌ Movement DENIED (STEP 2 fails)
- ✅ LED turns ORANGE at Station 2
- ✅ Operator 1 remains at Station 1
- ✅ Log: "[SCENARIO C - STEP 2 FAILED] Cannot leave current station: Outside delta period of shift Shift B, no permissions to leave"

---

## SECTION 4: SCENARIO D - Different Machine (10:35-10:42)

### Test Group D.1: Two-Step Process - Both Steps Pass

#### TEST D.1.1: Move to Empty Station on Different Machine Within Delta
**Time:** 10:38 (within Delta- of Shift B: 10:38-10:40)
**Setup:**
- Machine 100, Station 1: Operator 1 logged in (Shift B)
- Machine 200, Station 1: Empty (also has Shift B active)
- Operator 1 scans Machine 200, Station 1

**Expected Result:**
- ✅ Remote logout successful
- ✅ Machine 100 Station 1: LED RED
- ✅ Machine 200 Station 1: LED GREEN
- ✅ Shift info retained (same shift on both machines)
- ✅ Log: "[SCENARIO D] Remote logout complete: Machine 100 -> Machine 200"

---

#### TEST D.1.2: Move with CANLOGOUTOFSHIFT Outside Delta
**Time:** 10:30 (outside delta)
**Setup:**
- Login Operator 2 (CANLOGOUTOFSHIFT) at Machine 100, Station 1
- Machine 200, Station 1 is empty
- Operator 2 scans Machine 200, Station 1

**Expected Result:**
- ✅ Remote logout successful
- ✅ Operator 2 moves to Machine 200
- ✅ Log: "[STEP2] Has CANLOGOUTOFSHIFT permission - can leave"

---

#### TEST D.1.3: Replace Operators at Target Machine with CANREPLACEOPERATOR
**Time:** 10:31
**Setup:**
- Machine 100: Login Operator 3 (CANREPLACEOPERATOR) at Station 1
- Machine 200, Station 2: Login Operators 1, 5, 2 (MaxLogins=3)
- Operator 3 scans Machine 200, Station 2

**Expected Result:**
- ✅ All three operators at Machine 200 logged out
- ✅ Operator 3 logged out from Machine 100
- ✅ Operator 3 logged in at Machine 200, Station 2
- ✅ Log: "[SCENARIO D] Mass replacing 3 operators at target machine (CANREPLACEOPERATOR)"

---

### Test Group D.2: STEP 1 or STEP 2 Fails

#### TEST D.2.1: Move to Empty Station with CANREPLACEOPERATOR - DENIED
**Time:** 10:32
**Setup:**
- Operator 3 at Machine 200, Station 2
- Machine 100, Station 1 is empty
- Operator 3 scans Machine 100, Station 1

**Expected Result:**
- ❌ Movement DENIED
- ✅ LED turns ORANGE at Machine 100, Station 1
- ✅ Operator 3 remains at Machine 200
- ✅ Log: "[SCENARIO D - STEP 1 FAILED] Cannot login at target: CANREPLACEOPERATOR requires someone to replace - station is empty"

---

#### TEST D.2.2: Cannot Leave Outside Delta Without Permissions
**Time:** 10:33 (outside delta)
**Setup:**
- Login Operator 1 at Machine 100, Station 1
- Machine 200, Station 1 is empty
- Operator 1 scans Machine 200, Station 1

**Expected Result:**
- ❌ Movement DENIED (STEP 2 fails)
- ✅ LED turns ORANGE at Machine 200
- ✅ Operator 1 remains at Machine 100
- ✅ Log: "[SCENARIO D - STEP 2 FAILED] Cannot leave current machine: Outside delta period of shift Shift B, no permissions to leave"

---

## SECTION 5: TIMER LOGIC - Auto-Logout (10:42-10:48)

### Test Group TM1: Auto-Logout Check (Rule TM1)

#### TEST TM1.1: Auto-Logout at Shift End
**Time:** 10:20:30 (Shift A ends at 10:20)
**Setup:**
- **AutoLogoutAtShiftEnd** = true (the default)
- Before 10:20: Login Operator 1 at Machine 100, Station 1 at 10:00 (Shift A)
- Wait for minute timer to trigger after 10:20

**Expected Result:**
- ✅ Operator 1 auto-logged out
- ✅ LED turns RED
- ✅ `ActiveShiftId` cleared
- ✅ Log: "[AutoLogout] Operator 1 at Machine 100, Sub 1 logged out - shift Shift A ended at 10:20"

**Verification:**
- Operator 1: `IsLoggedIn` = false, `ActiveShiftId` = null

---

#### TEST TM1.2: Auto-Logout Multiple Operators
**Time:** 10:40:30 (Shift B ends at 10:40)
**Setup:**
- **AutoLogoutAtShiftEnd** = true
- Before 10:40: Login Operators 1, 5, 2 at Machine 100, Station 2 at 10:21
- Wait for minute timer after 10:40

**Expected Result:**
- ✅ All three operators auto-logged out (Operator 2's CANLOGOUTOFSHIFT makes no difference)
- ✅ LED turns RED
- ✅ Log: one "[AutoLogout] Operator ... logged out - shift Shift B ended at 10:40" line per operator

---

#### TEST TM1.3: No Auto-Logout Before Shift End
**Time:** 10:39 (before Shift B ends at 10:40)
**Setup:**
- **AutoLogoutAtShiftEnd** = true
- Operators from TM1.2 still logged in
- Observe minute timer

**Expected Result:**
- ✅ No auto-logout
- ✅ Operators remain logged in
- ✅ LED stays GREEN
- ✅ No "[AutoLogout] Operator ..." lines (a check that does nothing logs nothing)

---

#### TEST TM1.4: No Auto-Logout When Setting Switched Off by SQL
**Time:** 10:43
**Setup:**
- Set **AutoLogoutAtShiftEnd** = 'False' by SQL (see Prerequisites) and wait a minute
- Login Operator 1 at Machine 100, Station 1 during a shift; wait for the shift to end

**Expected Result:**
- ✅ Log (once, within a minute of the SQL change): "[AutoLogout] Logout at shift end is OFF (AutoLogoutAtShiftEnd is switched off in tblShiftSettings)"
- ✅ Operator 1 is not logged out at shift end
- ✅ No "[AutoLogout] Operator ..." lines

---

#### TEST TM1.5: Operator's Shift Deleted
**Time:** 10:44
**Setup:**
- Operator 1 logged in on a shift at Machine 100. Create a second shift with the same times, delete
  Operator 1's shift in the Shift Editor (with its assignments), and assign the second shift to Machine 100
- Wait for the minute timer

**Expected Result:**
- ✅ Operator 1 is **not** logged out
- ✅ Operator 1 is moved to the shift now running on the machine -
  Log: "[ShiftTransition] Operator 1 moved to shift [ID] ([name]) - shift ID [old ID] no longer exists"
- ✅ If no shift is running on the machine instead: "[ShiftTransition] Operator 1 shift info cleared - shift ID [old ID] no longer exists, no shift running on the machine", and Operator 1 can log out freely

---

#### TEST TM1.6: Shift Start Rule - Login Without a Shift on a Machine Whose Shift Is Running
**Time:** 10:45
**Setup:**
- Run once with **AutoLogoutAtShiftEnd** = true and once with false (the result must be the same)
- Operator 1 logs in at Machine 100 a minute before a shift starts there (legacy login, `ActiveShiftId` = null)
- Wait for the shift to start

**Expected Result:**
- ✅ Operator 1 logged out within a minute of the shift start
- ✅ LED turns RED
- ✅ Log: "[ShiftStart] Operator 1 at Machine 100, Sub 1 logged out - logged in without a shift and shift [name] is running on the machine"
- ✅ Operator 1 scans again → logged in and assigned the shift

---

## SECTION 6: TIMER LOGIC - Shift Transitions (10:48-10:52)

### Test Group TM2: Shift Transition Check (Rule TM2)

All TM2 tests need **AutoLogoutAtShiftEnd** = false, set by SQL.

#### TEST TM2.1: Transition After Delta+ Expires to New Shift
**Time:** 10:23 (Delta+ expires at 10:22, Shift B active)
**Setup:**
- **AutoLogoutAtShiftEnd** = false
- Operator 1 logged in at 10:00 with Shift A
- Still logged in after Shift A ended
- Wait for the minute timer at 10:23

**Expected Result:**
- ✅ Operator 1 moved to Shift B
- ✅ `ActiveShiftId` updated to Shift B ID
- ✅ Operator remains logged in, and is now held to Shift B (a logout scan outside Shift B's delta is refused)
- ✅ Log: "[ShiftTransition] Operator 1 moved to shift [Shift B ID] (Shift B) - shift Shift A has ended"

---

#### TEST TM2.2: No Transition While Within Delta+
**Time:** 10:21 (within Delta+: 10:20-10:22)
**Setup:**
- **AutoLogoutAtShiftEnd** = false
- Operator 1 logged in with Shift A, Shift B now active
- Still within Delta+ grace period

**Expected Result:**
- ✅ No transition (still within grace period)
- ✅ Operator still has Shift A info (a logout scan is allowed - within Delta+)
- ✅ No "[ShiftTransition]" lines

---

#### TEST TM2.3: Operator Released After a Shift Is Logged Out When the Next Shift Starts
**Time:** 10:25 onwards (needs a gap between two shifts on one machine - use Part D's Shifts P and Q, or temporarily move Shift B to start at 10:30)
**Setup:**
- **AutoLogoutAtShiftEnd** = false
- Operator 1 logged in on the first shift; the shift ends and no shift follows straight away
- Wait for the second shift to start

**Expected Result:**
- ✅ At the first shift's end (plus Delta+): "[ShiftTransition] Operator 1 shift info cleared - shift [name] has ended, no shift running on the machine"; Operator 1 stays logged in and can log out freely
- ✅ When the second shift starts: Operator 1 is **logged out** - Log: "[ShiftStart] Operator 1 ... logged out - logged in without a shift and shift [name] is running on the machine"
- ❌ Fails if Operator 1 is given the second shift and stays logged in ("assigned to shift ... - shift started while logged in" was the behaviour before v3.0)

---

#### TEST TM2.4: Order of Operations - Auto-Logout Before Transition
**Time:** 10:20:30
**Setup:**
- **AutoLogoutAtShiftEnd** = true
- Operator 1 logged in with Shift A
- Shift B now active at 10:20

**Expected Result:**
- ✅ Auto-logout runs FIRST (Rule TM1)
- ✅ Operator logged out
- ✅ Transition check runs SECOND, finds nothing to do
- ✅ Log: "[AutoLogout] Operator 1 ..." and no "[ShiftTransition]" line for Operator 1

---

## SECTION 7: SERVICE START RECOVERY (10:52-10:58)

### Test Group SS1: Operator Recovery

#### TEST SS1.1: Recover Operator - Shift Covered Login Time
**Time:** 10:52
**Setup:**
- Stop service
- Manually set Operator 1 in opstate: logged in at 10:05, Machine 100, Station 1
- Restart service at 10:52
- Shift B (10:20-10:40) should NOT cover 10:05, but Shift A (10:00-10:20) does

**Expected Result:**
- ✅ Operator 1 kept logged in
- ✅ `ActiveShiftId` assigned to Shift A (covered login time)
- ✅ Log: "[ServiceStart] Operator 1 at Machine 100, Sub 1 - Assigned to shift Shift A (ID=...) covering original login time 10:05:00"

---

#### TEST SS1.2: Recover Operator - No Shift Covered Login, No Current Shift
**Time:** 10:53
**Setup:**
- Stop service
- Temporarily remove shift assignments from Machine 200
- Set Operator 2 in opstate: logged in at 10:15 (during gap), Machine 200, Station 1
- Restart service

**Expected Result:**
- ✅ Operator 2 kept logged in
- ✅ `ActiveShiftId` = null (a logout scan is allowed)
- ✅ No "[ServiceStart]" or "[ShiftStart]" line for Operator 2

---

#### TEST SS1.3: Recover Operator - No Shift Covered Login, But Current Shift Active
**Time:** 10:54
**Setup:**
- Stop service
- Re-assign shifts to Machine 200
- Set Operator 3 in opstate: logged in at 10:15 (gap between shifts), Machine 200, Station 1
- Restart service at 10:54 (during Shift B)

**Expected Result:**
- ✅ Operator 3 LOGGED OUT
- ✅ `IsLoggedIn` = false
- ✅ Log: "[ShiftStart] Operator 3 at Machine 200, Sub 1 logged out - logged in without a shift and shift Shift B is running on the machine"
- ✅ Every other operator who was logged in is restored correctly

---

#### TEST SS1.4: Recover Operator - Machine No Longer Exists
**Time:** 10:55
**Setup:**
- Stop service
- Set Operator 4 in opstate: logged in at Machine 999
- Restart service

**Expected Result:**
- ✅ Operator 4 LOGGED OUT
- ✅ Log: "Service Start | Operator 4 logged out - machine 999 no longer exists"

---

#### TEST SS1.5: Recover Multiple Operators at Same Station
**Time:** 10:56
**Setup:**
- Stop service
- Set Operators 1, 5, 2 in opstate: all at Machine 100, Station 2 at 10:05
- Restart service

**Expected Result:**
- ✅ All three kept logged in
- ✅ All assigned to Shift A
- ✅ All at same station

---

## SECTION 8: EDGE CASES (10:58-11:00)

### Test Group E1: Permission Combinations

#### TEST E1.1: Operator with Both Permissions - Supervisor Workflow
**Time:** 10:58
**Setup:**
- Machine 100, Station 2: Operators 1, 5, 2 logged in (outside delta)
- Operator 4 (both CANLOGOUTOFSHIFT and CANREPLACEOPERATOR) scans Station 2

**Expected Steps:**
1. ✅ Operator 4 replaces all 3 operators (CANREPLACEOPERATOR)
2. ✅ Operator 4 now logged in
3. ✅ Operator 4 scans own station → Logs out (CANLOGOUTOFSHIFT)
4. ✅ Station now empty

---

#### TEST E1.2: CANLOGOUTOFSHIFT Does Not Bypass Capacity
**Time:** 10:59
**Setup:**
- Login Operator 1 at Machine 100, Station 1 (outside delta)
- Operator 2 (has CANLOGOUTOFSHIFT only) scans Station 1

**Expected Result:**
- ❌ Login DENIED
- ✅ LED turns ORANGE
- ✅ Log: "[SCENARIO A] Login DENIED: Station full (1/1), no operators within delta, no CANREPLACEOPERATOR"
- ✅ CANLOGOUTOFSHIFT helps with leaving, not entering

---

**END OF DAYTIME 1-HOUR TEST**

---
---

# PART B: OVERNIGHT MIDNIGHT-CROSSING TESTS

**Shift C Configuration:** 16:00 (4 PM) → 10:00 (10 AM next day)
**DeltaMinus:** 15 minutes (09:45-10:00)
**DeltaPlus:** 10 minutes (10:00-10:10)

---

## DAY 1 END - SETUP (5 minutes at ~4:30 PM)

### TEST MC.1 Setup: Auto-Logout After Midnight Crossing
**Time:** Day 1, 4:30 PM
**Setup:**
- **AutoLogoutAtShiftEnd** = true
- Machine 100, Station 1: Empty
- Operator 1 scans card

**Expected Setup Result:**
- ✅ Login successful
- ✅ LED turns GREEN
- ✅ Operator 1 assigned to Shift C
- ✅ Log: "[SCENARIO A - Rule A.1.1] Login to empty station"

**Action:** Leave Operator 1 logged in overnight

---

### TEST MC.2 Setup: Manual Logout After Midnight Crossing (Within Delta)
**Time:** Day 1, 4:35 PM
**Setup:**
- **AutoLogoutAtShiftEnd** = false (disable auto-logout for MC.2 and MC.3)
- Machine 100, Station 2: Empty
- Operator 2 scans card

**Expected Setup Result:**
- ✅ Login successful
- ✅ LED turns GREEN
- ✅ Operator 2 assigned to Shift C

**Action:** Leave Operator 2 logged in overnight

---

## DAY 2 MORNING - EXECUTION (15 minutes starting at ~8:00 AM)

### TEST MC.3 Setup: Login Before Midnight, Logout At Shift End After Midnight
**Time:** Day 2, 8:00 AM (during Shift C, which ends at 10:00)
**Setup:**
- **AutoLogoutAtShiftEnd** = false (still disabled from yesterday)
- Machine 100, Station 1: Operator 1 still logged in from yesterday
- Machine 100, Station 3: Empty
- Operator 3 scans Station 3

**Expected Result:**
- ✅ Login successful
- ✅ LED turns GREEN
- ✅ Operator 3 assigned to Shift C
- ✅ Log: "[SCENARIO A - Rule A.1.1] Login to empty station"
- ✅ Operator logged in at 8:00 AM (same day as shift end)
- ✅ Shift started at 4:00 PM yesterday (crosses midnight)

**Action:** Wait for 9:50 AM test

---

### TEST MC.2 Verification: Manual Logout After Midnight (Within Delta-)
**Time:** Day 2, 9:50 AM (within Delta-: 09:45-10:00)
**Setup:**
- Machine 100, Station 2: Operator 2 still logged in from yesterday
- Operator 2 scans own station

**Expected Result:**
- ✅ Logout successful (within delta)
- ✅ LED turns RED
- ✅ `ActiveShiftId` cleared
- ✅ Log: "[Logout] Operator 2 logged out (within delta period)"

**Verification:**
- ✅ Operator logged in at 4:35 PM yesterday
- ✅ Shift crossed midnight (16:00 yesterday → 10:00 today)
- ✅ Delta calculation: ShiftEnd (10:00) - DeltaMinus (15) = 09:45
- ✅ Current time (9:50) is ≥ 09:45 and ≤ 10:10 ✅
- ✅ Midnight crossing did NOT break delta calculation

---

### TEST MC.3 Verification: Logout At Shift End After Midnight
**Time:** Day 2, 10:00 AM (exactly at shift end)
**Setup:**
- Machine 100, Station 3: Operator 3 logged in at 8:00 AM
- Operator 3 scans own station

**Expected Result:**
- ✅ Logout successful (at shift end, within delta)
- ✅ LED turns RED
- ✅ `ActiveShiftId` cleared
- ✅ Log: "[Logout] Operator 3 logged out (within delta period)"

**Critical Verification:**
- ✅ Operator logged in at 8:00 AM (AFTER midnight, same day as shift end)
- ✅ Shift started at 4:00 PM YESTERDAY (crosses midnight)
- ✅ Delta calculation uses shift END time (10:00 today), not start time
- ✅ The math: `now (10:00) >= shiftEnd.Add(-deltaMinus) (09:45)` works correctly
- ✅ Midnight crossing does NOT break delta calculation for same-day logins

---

### TEST MC.1 Verification: Auto-Logout After Midnight Crossing
**Time:** Day 2, 10:01 AM (wait for minute timer after 10:00)
**Setup:**
- **Change AutoLogoutAtShiftEnd** = true (re-enable for MC.1 verification)
- Machine 100, Station 1: Operator 1 logged in at 4:30 PM yesterday
- Wait for minute timer to trigger (should be ~10:00:30)

**Expected Result:**
- ✅ Operator 1 auto-logged out (shift ended at 10:00)
- ✅ LED turns RED
- ✅ `ActiveShiftId` cleared
- ✅ Log: "[AutoLogout] Operator 1 at Machine 100, Sub 1 logged out - shift Shift C ended at 10:00"

**Critical Verification:**
- ✅ Operator logged in at 4:30 PM YESTERDAY
- ✅ Shift crossed midnight (16:00 yesterday → 10:00 today)
- ✅ Auto-logout calculation: `now (10:00+) >= shiftEnd (10:00)` works correctly
- ✅ Midnight crossing does NOT break auto-logout calculation
- ✅ Operator was logged in for ~17.5 hours across midnight

---

## MIDNIGHT-CROSSING TEST SUMMARY

**Total Time Investment:**
- Day 1 Setup: 5 minutes (4:30-4:35 PM)
- Day 2 Verification: 15 minutes (8:00-10:01 AM)
- **Total: 20 minutes over 2 days**

**Coverage:**
- ✅ MC.1: Auto-logout works after midnight crossing
- ✅ MC.2: Manual logout (within delta) works after midnight crossing
- ✅ MC.3: Login after midnight, logout at shift end works correctly
- ✅ Delta calculations handle midnight crossing properly
- ✅ Auto-logout calculations handle midnight crossing properly
- ✅ Operators can stay logged in for 17+ hours across midnight

---

# PART C: LEGACY PARITY TESTS

**When:** any time of day, ~15 minutes. **Must pass before any site install.**

**Purpose:** wherever shift rules don't apply, the service must behave exactly like the pre-shift
service (installer v1.1.51): scanning logs you in or out, a MaxLogins = 1 station replaces its
occupant, a full multi-login station refuses, and scanning elsewhere moves you.

**Run Section C.1 twice:**
1. With `UseShiftManagement` = false in the service config (restart the service after changing it)
2. With `UseShiftManagement` = true, using Machine 300 (no shift assigned)

`UseOperatorPermissions` = false for these tests. Every operator should end each test logged out
unless stated.

---

## SECTION C.1: No Shift Rules Apply

#### TEST LP.1: Login to Empty Station
**Setup:** Machine 300, Station 1 empty. Operator 1 scans Machine 300, Station 1.

**Expected Result:**
- ✅ Operator 1 logged in, LED GREEN
- ✅ `ActiveShiftId` = null
- ✅ Log: "[ShiftManagement] Shift management disabled or no active shift - using legacy logic"

---

#### TEST LP.2: Logout at Own Station
**Setup:** Continuing LP.1, Operator 1 scans Machine 300, Station 1 again.

**Expected Result:**
- ✅ Operator 1 logged out, LED RED

---

#### TEST LP.3: Single-Login Station Replaces Occupant
**Setup:** Operator 1 logged in at Machine 300, Station 1 (MaxLogins = 1). Operator 5 scans Machine 300, Station 1.

**Expected Result:**
- ✅ Operator 1 logged out
- ✅ Operator 5 logged in, LED GREEN

---

#### TEST LP.4: Full Multi-Login Station Refuses
**Setup:** Operators 1 and 2 logged in at Machine 300, Station 2 (MaxLogins = 2). Operator 5 (not logged in) scans Machine 300, Station 2.

**Expected Result:**
- ✅ Operator 5 NOT logged in
- ✅ Operators 1 and 2 still logged in, LED stays GREEN
- ✅ Log: "[ShiftManagement] Legacy login refused - station full: Machine 300, Sub 2, MaxLogins 2"

---

#### TEST LP.5: Move Station, Same Machine
**Setup:** Operator 1 logged in at Machine 300, Station 1. Machine 300, Station 2 has a free slot. Operator 1 scans Machine 300, Station 2.

**Expected Result:**
- ✅ Operator 1 logged out of Station 1 (LED RED) and logged in at Station 2 (LED GREEN)

---

#### TEST LP.6: Move to a Different Machine
**Setup:** Operator 1 logged in at Machine 300, Station 2. Operator 1 scans a station on another machine
with no active shift (in the `UseShiftManagement` = true pass, use Machine 200 outside Shifts A/B, or
another unassigned machine).

**Expected Result:**
- ✅ Operator 1 logged out of Machine 300 (LED RED) and logged in at the new machine (LED GREEN)
- ✅ Operator 1 appears once in `RemoteOperatorLog` as logged in, at the new machine

---

## SECTION C.2: Gaps Between Shifts and Operator Reload (`UseShiftManagement` = true)

#### TEST LP.7: Login During a Gap, Then the Shift Starts
**Setup:**
- Create Shift R starting ~5 minutes from now (20 minutes long) and assign it to Machine 300 for today
- Before Shift R starts, Operator 1 scans Machine 300, Station 1
- Wait for Shift R to start

**Expected Result:**
- ✅ Before the start: Operator 1 logged in by legacy logic, `ActiveShiftId` = null
- ✅ Within a minute of the start: Operator 1 logged out (shift start rule, as TM1.6) - the same with
  `AutoLogoutAtShiftEnd` true or false
- ✅ Operator 1 scans again → logged in and assigned Shift R

---

#### TEST LP.8: Operator Reload Keeps the Shift
**Setup:** Continue from LP.7 with Operator 1 logged in and assigned Shift R.
Force an operator reload by editing any row in `JEGR_DB.dbo.tblOperators` (or wait up to 15 minutes).

**Expected Result:**
- ✅ Operator 1 still logged in, still assigned Shift R (a logout scan is still refused)
- ✅ Log: "[Reload] Operator 1 at Machine 300, Sub 1 - kept shift ID ..."
- ❌ Fails if Operator 1 is logged out, or can now log out by scanning

**Cleanup:** Delete Shift R (with its assignment) once Operator 1 is logged out

---

# PART D: BREAKS, DEFAULTS AND THE SHIFT EDITOR

**When:** any time of day, ~30 minutes. New in v3.0.

**Setup for Part D:**
- `AutoLogoutAtShiftEnd` = True (after `schema.sql` it is)
- In the Shift Editor create two shifts (they get Delta 0), starting about 5 minutes from now:
  - **Shift P**: 15 minutes long, on Machine 100, with one break 5 minutes after its start
  - **Shift Q**: starts 5 minutes after Shift P ends, 10 minutes long, on Machine 100, no breaks
- Machine 300 stays without shifts. No restart is needed after saving - wait a minute

Times below are relative to Shift P's start (P+0).

---

## SECTION D.1: What the Shift Editor Shows

#### TEST ED.1: Hidden Settings
**Setup:** Open the Shift Editor.

**Expected Result:**
- ✅ Tabs: **Shifts** and **Operator Settings** only - no Shift Settings tab
- ✅ Shifts grid: Shift ID, Shift Name, Start Time, End Time, Active - no Delta columns
- ✅ Nothing on any screen shows or mentions Delta, grace minutes or AutoLogoutAtShiftEnd
- ✅ In the database the setting row and the Delta columns still exist:
  `SELECT * FROM JEGR_Utils.dbo.tblShiftSettings` / `SELECT ShiftName, DeltaMinus, DeltaPlus FROM JEGR_Utils.dbo.tblShifts`

(The editor's own screens are covered in detail by `testing/SHIFT_EDITOR_UI_CHECKLIST.md`.)

---

## SECTION D.2: Held to the Shift, Breaks, Shift End

#### TEST BR.1: Held to the Shift With Delta 0
**Time:** P+1
**Setup:** Operators 1 and 5 log in at Machine 100 (Stations 1 and 2); Operator 2 (CANLOGOUTOFSHIFT) logs in at Station 2. Operator 1 scans Station 1 again, then scans Station 2.

**Expected Result:**
- ✅ Both scans refused: LED ORANGE, Operator 1 still logged in at Station 1, no new record in `RemoteOperatorLog`

---

#### TEST BR.2: Break Logs Out Everyone on the Shift
**Time:** P+5 (the break)
**Setup:** Nobody scans. Watch the readers at P+5.

**Expected Result:**
- ✅ Within a few seconds of P+5: Operators 1, 5 and 2 logged out (the permission makes no difference), LEDs RED
- ✅ Log: "[Break] Operator 1 at Machine 100, Sub 1 logged out - break at [P+5]" (one line per operator)
- ✅ `RemoteOperatorLog`: an ordinary logout record for each (State 0, SubregType 0)
- ✅ An operator logged in at Machine 300 (no shift) is not affected

---

#### TEST BR.3: Logging Back In After the Break
**Time:** P+5 to P+7
**Setup:** Operator 1 scans Station 1 (same station). Operator 5 scans Station 1 on Machine 200 if it has a running shift, otherwise a different station on Machine 100.

**Expected Result:**
- ✅ Both logged in and assigned the shift of the machine they scanned
- ✅ At P+6 and P+7 neither is logged out again by the same break
- ✅ Operator 1 is held again (a logout scan is refused)

---

#### TEST BR.4: Break Added While the Shift Is Running
**Time:** P+7
**Setup:** In the Shift Editor add a break at P+9 to Shift P and **Save Breaks**. Operator 1 stays logged in.

**Expected Result:**
- ✅ At P+9 Operator 1 is logged out - Log: "[Break] Operator 1 ... logged out - break at [P+9]"
- ✅ No service restart was needed

---

#### TEST BR.5: Break Missed While the Service Was Stopped
**Time:** P+10 to P+12
**Setup:** Operator 1 logs in again. Add a break at P+12 (Save Breaks). Stop the shift service at P+11:30 and start it at P+12:30.

**Expected Result:**
- ✅ Shortly after the start Operator 1 is logged out by the P+12 break
- ✅ Log: "[ServiceStart] Operator 1 ... Assigned to shift Shift P ..." then "[Break] Operator 1 ... logged out - break at [P+12]"

---

#### TEST BR.6: Shift End With the Default Setting
**Time:** P+15
**Setup:** Operator 1 and Operator 2 log in again before P+15 and stay logged in.

**Expected Result:**
- ✅ At P+15 both are logged out, LEDs RED - Log: "[AutoLogout] Operator 1 ... logged out - shift Shift P ended at [P+15]"
- ✅ Between P+15 and P+20 (gap) logins and logouts at Machine 100 follow the legacy rules

---

#### TEST BR.7: Gap Login Logged Out at the Next Shift's Start
**Time:** P+18 to P+20
**Setup:** Operator 5 logs in at Machine 100 at P+18 (in the gap). Shift Q starts at P+20.

**Expected Result:**
- ✅ At P+20 Operator 5 is logged out - Log: "[ShiftStart] Operator 5 ... logged out - logged in without a shift and shift Shift Q is running on the machine"
- ✅ Operator 5 scans again → logged in and assigned Shift Q

---

#### TEST BR.8: Missing Setting Row Counts as On
**Time:** during Shift Q
**Setup:** `DELETE FROM JEGR_Utils.dbo.tblShiftSettings WHERE SettingName = 'AutoLogoutAtShiftEnd'`. Operator 5 stays logged in on Shift Q until it ends.

**Expected Result:**
- ✅ Operator 5 is logged out at Shift Q's end
- ✅ No "[AutoLogout] Logout at shift end is OFF" line

**Cleanup:** run `schema.sql` again (it puts the setting row back as 'True'); delete Shifts P and Q

---

## TEST EXECUTION CHECKLIST

### Pre-Test Setup (Legacy Parity)
- [ ] Machine 300 has no shift assignments; Station 1 MaxLogins = 1, Station 2 MaxLogins = 2
- [ ] `UseOperatorPermissions` = false
- [ ] Pass 1: `UseShiftManagement` = false, service restarted
- [ ] Pass 2: `UseShiftManagement` = true, service restarted

### Pre-Test Setup (Daytime)
- [ ] Database configured with Shift A (10:00-10:20), Shift B (10:20-10:40)
- [ ] Both shifts: DeltaMinus=2, DeltaPlus=2 (set by SQL - see Prerequisites); no break times
- [ ] Test machines configured with correct MaxLogins
- [ ] Test operators configured with correct permissions
- [ ] UseShiftManagement = true
- [ ] Clear all existing operator states
- [ ] Service running with debug logging enabled

### Pre-Test Setup (Overnight)
- [ ] Database configured with Shift C (16:00-10:00)
- [ ] Shift C: DeltaMinus=15, DeltaPlus=10 (set by SQL)
- [ ] Machine 100 assigned to Shift C for **both** days it touches
- [ ] AutoLogoutAtShiftEnd = true for MC.1
- [ ] AutoLogoutAtShiftEnd = false (by SQL) for MC.2 and MC.3
- [ ] Fixed-time auto-logout (`AutoLogout` in the service config) off, or its time outside the test

### Pre-Test Setup (Part D)
- [ ] `schema.sql` (current version) has been run: `tblShiftBreaks` exists, AutoLogoutAtShiftEnd = True
- [ ] Shifts P and Q created in the Shift Editor (Delta 0), Shift P with a break; Machine 300 without shifts

### During Testing
- [ ] Monitor feedback logs for expected messages
- [ ] Verify LED states (RED, GREEN, ORANGE)
- [ ] Check operator state in database (IsLoggedIn, ActiveShiftId, MachineID, SubID)
- [ ] Note timestamp of each action
- [ ] Capture screenshots or logs for verification

### Post-Test Verification
- [ ] Review all log messages
- [ ] Verify database state matches expected
- [ ] Check for any unexpected errors or warnings
- [ ] Verify LED states returned to correct state
- [ ] Confirm midnight crossing calculations worked correctly

---

## PASS/FAIL CRITERIA

### Test Passes If:
1. ✅ Operator login/logout state matches expected
2. ✅ LED color matches expected (RED/GREEN/ORANGE)
3. ✅ ActiveShiftId matches expected value
4. ✅ Log messages contain expected text
5. ✅ No unexpected errors in logs
6. ✅ Other operators not affected when they shouldn't be
7. ✅ Midnight crossing calculations work correctly (MC tests)

### Test Fails If:
1. ❌ Operator state doesn't match expected
2. ❌ LED shows wrong color
3. ❌ ActiveShiftId is wrong or null when should have value
4. ❌ Expected log messages missing
5. ❌ Unexpected errors or exceptions occur
6. ❌ Other operators affected when they shouldn't be
7. ❌ Midnight crossing breaks delta or auto-logout calculations

---

## SUMMARY STATISTICS

**Daytime Test (Part A):**
- Duration: 60 minutes (10:00-11:00 AM)
- Total Test Cases: 41 tests (v2.0 said 48; the breakdown below is the actual count)
- Coverage: All SCENARIOS A-D, Timer Logic (TM1, TM2), Service Recovery (SS1), Edge Cases

**Breakdown by Section:**
- SCENARIO A (Not Logged In): 8 tests
- SCENARIO B (Logout): 5 tests
- SCENARIO C (Same Machine): 6 tests
- SCENARIO D (Different Machine): 5 tests
- Timer Logic (Auto-Logout): 6 tests
- Timer Logic (Transitions): 4 tests
- Service Start Recovery: 5 tests
- Edge Cases: 2 tests

**Overnight Test (Part B):**
- Duration: 5 min setup (Day 1) + 15 min verification (Day 2) = 20 minutes total
- Total Test Cases: 3 tests
- Coverage: Midnight crossing for auto-logout, manual logout, and same-day login

**Legacy Parity Test (Part C):**
- Duration: ~15 minutes (LP.1-LP.6 run twice: shift management off, and on with an unassigned machine)
- Total Test Cases: 8 tests
- Coverage: pre-shift behaviour where no shift applies, gaps between shifts, operator reload

**Breaks, Defaults and the Shift Editor (Part D):**
- Duration: ~30 minutes
- Total Test Cases: 9 tests
- Coverage: hidden settings, held to the shift with delta 0, breaks (logout, re-login, added while running, missed during a restart), shift end by default, shift start rule, missing setting row

**Total Test Coverage:**
- All Rules: A.1.1, A.1.2, A.2, A.3, B.1, C (two-step), D (two-step), TM0 (shift start), TM1, TM2, TM3 (breaks), SS1 ✅
- All Permissions: CANLOGOUTOFSHIFT, CANREPLACEOPERATOR ✅
- All Edge Cases: Midnight crossing, permission combinations ✅
- All Prerequisites: UseShiftManagement, active shift checks ✅

---

**END OF REVISED TEST PLAN**
