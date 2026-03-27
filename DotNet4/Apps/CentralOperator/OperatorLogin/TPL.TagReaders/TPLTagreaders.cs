using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text;
using System.Timers;
using System.Threading;
using System.Threading.Tasks;
using Dynamic.DataLayer;
using MVVMHelpers;
using TagReaderShared;

namespace TPL.TagReaders
{
    public class TagReaderServiceFramework
    {
        private TPLTagReaders tagReaders;

        public TPLTagReaders TagReaders
        {
            get { return tagReaders; }
            set { tagReaders = value; }
        }
        private SqlDataAccess da;
        //private int timercount = 0;
        private ServiceState currentState;
        private System.Timers.Timer timer;
        private bool isBusy = false;
        //private int datareadminute = 0;

        public delegate void FeedbackDelegate(string feedback);
        public FeedbackDelegate TextFeedback = null;
        public Boolean DebugMode;
        public Boolean DBDebugMode;
        private FeedbackRecs feedbackRecs;
        private DateTime lastForcedLogout = DateTime.MinValue;
        private DateTime logoutTime = DateTime.Today.AddHours(23).AddMinutes(30);
        private List<DateTime> logoutTimes = new List<DateTime>();
        
        public string AutoLogoutTimes
        {
            get
            {
                return string.Join("|", logoutTimes.Select(d => d.ToShortTimeString()));
            }
            set
            {
                logoutTimes = new List<DateTime>();
                if (!string.IsNullOrEmpty(value))
                {
                    string[] times = value.Split('|');

                    foreach (string time in times)
                    {
                        // hh:mm
                        string[] t = time.Split(':');
                        if (t.Count() >= 2)
                        {
                            int h = 23;
                            if (t[0].Length > 2) t[0] = t[0].Substring(t[0].Length - 2, 2);
                            bool test = Int32.TryParse(t[0], out h);
                            if (test)
                            {
                                int m = 30;
                                test = Int32.TryParse(t[1], out m);
                                if (test)
                                {
                                    logoutTimes.Add(DateTime.Today.AddHours(h).AddMinutes(m));
                                }
                            }
                        }
                    }
                    if (logoutTimes.Count > 0)
                        logoutTimes.Sort();
                }
            }
        }
        private bool autoLogout = false;

        public bool AutoLogout
        {
            get { return autoLogout; }
            set { autoLogout = value; }
        }
        private bool useOperatorPermissions = false;

        public bool UseOperatorPermissions
        {
            get { return useOperatorPermissions; }
            set 
            { 
                useOperatorPermissions = value;
                if (tagReaders != null)
                    tagReaders.UseOperatorPermissions = value;
            }
        }
        private bool useShiftManagement = false;

        public bool UseShiftManagement
        {
            get { return useShiftManagement; }
            set
            {
                useShiftManagement = value;
                if (tagReaders != null)
                    tagReaders.UseShiftManagement = value;
            }
        }
        public TagReaderServiceFramework(string connectionString, FeedbackDelegate feedback)
        {
            da = SqlDataAccess.Singleton;
            DBConnection dBConnection = new DBConnection();            
            dBConnection.DBName = "JEGR_DB";
            dBConnection.ConnectionString = connectionString;
            dBConnection.SiteName = "default";
            da.DBConnections.Add(dBConnection);
            da.SiteName = "default";            
            
            TextFeedback = feedback;
            tagReaders = new TPLTagReaders();
            tagReaders.TextFeedback = Feedback;
            feedbackRecs = new FeedbackRecs();
            currentState = ServiceState.Stopped;
            timer = new System.Timers.Timer();
            timer.Interval = 100;
            timer.Elapsed += new ElapsedEventHandler(DoTiming);
        }

        public ServiceState Start()
        {
            try
            {
                Feedback("Cockpit_Central_Operator Service Starting");
                tagReaders.DebugMode = DebugMode;
                tagReaders.Start();
                timer.Start();
                currentState = ServiceState.Running;
                if (autoLogout)
                {
                    Feedback("Autologout enabled, set for: " + AutoLogoutTimes + Environment.NewLine);
                }
                else
                {
                    Feedback("Autologout disabled");
                }
                Feedback("Current logins = " + tagReaders.EvalCurrentLogins() + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Feedback("Exception: Cockpit_Central_Operator Service Startup Exception, at:" + DateTime.Now + " Message: " + ex.Message);

            } 
            return currentState;
        }

        public ServiceState Stop()
        {
            if (timer != null)
            {
                Feedback("Cockpit Central Operator Service Stopping");
                timer.Stop();
                tagReaders.Stop();
                currentState = ServiceState.Stopped;
            }
            return currentState;
        }

        public ServiceState Pause()
        {
            if (timer != null)
            {
                timer.Stop();
                currentState = ServiceState.Paused;
            }
            return currentState;
        }

        public ServiceState Resume()
        {
            if (timer != null)
            {
                timer.Start();
                currentState = ServiceState.Running;
            }
            return currentState;
        }

        public void Ping()
        {
            Feedback("Pinging Tag Readers");
            tagReaders.PingReaders();
            Feedback("Finished Pinging");
        }

        public void Feedback(string info)
        {
            if (TextFeedback != null)
                TextFeedback(info);
            else
                Debug.WriteLine(info);
        }

        private int updateMinute = -1;
        private int autoLogoutMin = -1;
        private int shiftCheckMinute = -1;
        private DateTime _clearedDueToRetentionLimit = DateTime.MinValue;
        private bool evalLogoutTime()
        {
            bool test = false;
            foreach (DateTime d in logoutTimes)
            {
                if ((DateTime.Now.Hour == d.Hour) && (DateTime.Now.Minute >= d.Minute) && (DateTime.Now.Minute <= (d.Minute + 5)))
                {
                    test = (lastForcedLogout < DateTime.Today.AddHours(d.Hour).AddMinutes(d.Minute));
                    if (test)
                    {
                        test = tagReaders.OperatorStates.Count > 0;
                        if (!test)
                        {
                            Feedback("Autologout failed - No operator states data " + DateTime.Now);
                        }
                        else
                        {
                            logoutTime = DateTime.Today.AddHours(d.Hour).AddMinutes(d.Minute);
                            break;
                        }
                    }
                }
            }
            return test;
        }
        private DateTime lastlogintotal = DateTime.MinValue;

        private bool evalLastLogoutTime()
        {
            bool test = false;
            TimeSpan ts = DateTime.Now.Subtract(lastlogintotal);
            test = (ts.TotalMinutes > 14);
            return test;
        }
        private bool evalRetentionClearDateTime()
        {
            bool test = false;
            TimeSpan ts = DateTime.Now.Subtract(_clearedDueToRetentionLimit);
            test = (ts.TotalMinutes > 29);
            return test;
        }
        private void DoTiming(object sender, ElapsedEventArgs e)
        {
            if (!isBusy)
            {
                isBusy = true;
                try
                {
                    tagReaders.ReloadData(true);
                    tagReaders.Read();

                    int min = DateTime.Now.Minute;
                    if (AutoLogout && min != autoLogoutMin && evalLogoutTime())
                    {
                        autoLogoutMin = min; // only check once a minute
                        int logouts;
                        Feedback("Autologout Commencing");
                        logouts = tagReaders.AutoLogoutNow();
                        lastForcedLogout = DateTime.Today.AddHours(logoutTime.Hour).AddMinutes(logoutTime.Minute);
                        Feedback("Autologout Completed");
                    }
                    // Shift-based auto logout and transition (checks its own setting internally)
                    if (useShiftManagement && min != shiftCheckMinute)
                    {
                        shiftCheckMinute = min;

                        // Rule TM1: Auto-Logout Check (if enabled)
                        int shiftLogouts = tagReaders.AutoLogoutExpiredShifts();
                        if (shiftLogouts > 0)
                        {
                            Feedback($"Shift auto-logout: {shiftLogouts} operators logged out");
                        }

                        // Rule TM2: Shift Transition Check (runs after auto-logout)
                        int shiftTransitions = tagReaders.ShiftTransitionCheck();
                        if (shiftTransitions > 0)
                        {
                            Feedback($"Shift transitions: {shiftTransitions} operators transitioned/assigned");
                        }
                    }
                    if (min % 15 == 0) // every 15 minutes??
                    {
                        if (evalLastLogoutTime())
                        {
                            Feedback("Current logins = " + tagReaders.EvalCurrentLogins() + Environment.NewLine);
                            lastlogintotal = DateTime.Now;
                        }
                    }
                    if (min != updateMinute)
                    {
                        updateMinute = min;// only do once a minute
                        if (DBDebugMode) StoreFeedback();
                        tagReaders.SetUpdateAllLEDsNow();
                    }                    
                    if (min % 30 == 0 && evalRetentionClearDateTime()) // every 30 minutes but only once in that minute
                    {
                        tagReaders.clearExcessData();
                        _clearedDueToRetentionLimit = DateTime.Now;
                    }
                }
                catch (Exception ex)
                {
                    Feedback("Exception: Cockpit_Central_Operator Service, at:" + DateTime.Now + " Message: " + ex.Message);
                }
                finally
                {
                    isBusy = false;
                }
            }
        }

        public void DBFeedback(string aString)
        {
            try
            {
                if (DBDebugMode)
                {
                    //EventLog.WriteEntry(aString, System.Diagnostics.EventLogEntryType.Information);
                    if (feedbackRecs != null)
                    {
                        //SqlDataAccess da = SqlDataAccess.Singleton;
                        FeedbackRec rec = new FeedbackRec();
                        rec.Recnum = -1;
                        rec.ForceNew = true;
                        rec.LogTime = DateTime.Now;
                        rec.AppName = "Cockpit Central Operator";
                        rec.Feedback = aString;
                        feedbackRecs.Add(rec);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        private void StoreFeedback()
        {
            feedbackRecs.UpdateToDB();
            feedbackRecs.Clear();
        }


    }

    public class TPLTagReaders
    {
        SqlDataAccess da;
        public RfIdeasTagReaders TcpReaders;
        public delegate void FeedbackDelegate(string feedback);
        public FeedbackDelegate TextFeedback = null;
        public Tags TagsRfid;
        private Tags tags;
        private Operators operatorsRfid;
        public OperatorLoginStates OperatorStates;        
        private RemoteOperatorLogsGroupedByOperator remoteOperatorLogs;
        private JGLogData logData;
        private MachineSubIDs machineSubIDs;
        private TagReaderLocations tagReaderLocs;
        private TagReaderLog tagReaderLog;
        private Permissions availablePermissionsFromDb;
        private OperatorPermissions operatorPermissionsFromDb;
        public bool DebugMode { get; set; }
        public bool UseOperatorPermissions { get; set; }
        public bool UseShiftManagement { get; set; }
        private Shifts shifts;
        private ShiftMachineAssignments shiftMachineAssignments;
        private ShiftSettings shiftSettings;
        private OperatorSettings operatorSettings;

        private SharedReaderStatus sharedStatus;
        public TPLTagReaders()
        {
            
            TcpReaders = new RfIdeasTagReaders();
            DebugMode = false;
            OperatorStates = new OperatorLoginStates();
            OperatorStates.StatesFeedback = StatesFeedback;
            operatorsRfid = new Operators();
            TagsRfid = new Tags();
            tags = new Tags();
            remoteOperatorLogs = new RemoteOperatorLogsGroupedByOperator();
            remoteOperatorLogs.UpdateFieldNames = new List<string>();
            remoteOperatorLogs.UpdateFieldNames.Add("Operator_Remote"); 
            logData = new JGLogData();
            machineSubIDs = new MachineSubIDs();
            tagReaderLog = new TagReaderLog();
            availablePermissionsFromDb = new Permissions();
            operatorPermissionsFromDb = new OperatorPermissions();
            shifts = new Shifts();
            shiftMachineAssignments = new ShiftMachineAssignments();
            shiftSettings = new ShiftSettings();
            operatorSettings = new OperatorSettings();         
            da = SqlDataAccess.Singleton;
            GetData();        
        }
        public void StatesFeedback(string info)
        {
            if ((TextFeedback != null) && DebugMode)
                TextFeedback(info);
            else
                Debug.WriteLine(info);
        }
        public void GetData()
        {
            getReadersData();
            ReloadData(true);
            
        }
        private void getReadersData()
        {
            if (tagReaderLocs == null)
                tagReaderLocs = new TagReaderLocations();
            tagReaderLocs.RaiseException = true;
            tagReaderLocs = (TagReaderLocations)da.DBDataListSelect(tagReaderLocs, false, true);
            if (tagReaderLocs.UpdatedFromDB)
            {
                TcpReaders.Clear();
                foreach (TagReaderLocation tr in tagReaderLocs)
                {
                    addTcpReader(tr.LocationID, tr.LocationIP, tr.TcpPort, tr.MachineID, tr.SubID, tr.MaxLogins);
                }
                TcpReaders.Reset();
            }
            if (tagReaderLocs.Count == 0)
            {
                feedback(this.GetType().Name + " " + MethodInfo.GetCurrentMethod() + " TagReaderLocations Count = 0. UpdatedFromDB = " + tagReaderLocs.UpdatedFromDB);                
            }
        }

        private void addTcpReader(int locationid, string ip, int port, int machine, int sub, int maxlogins)
        {
            RfIdeasTcpReader tcpReader = new RfIdeasTcpReader(locationid, ip, port, machine, sub, maxlogins, DebugMode);
            tcpReader.OnStatusChanged = UpdateSingleReaderStatus;
            TcpReaders.Add(tcpReader);
        }
        private void getShiftData(bool useCache)
        {
            if (UseShiftManagement)
            {
                shifts.RaiseException = true;
                shifts = da.GetShifts(shifts, !useCache);

                shiftMachineAssignments.RaiseException = true;
                shiftMachineAssignments = da.GetShiftMachineAssignments(shiftMachineAssignments, !useCache);

                shiftSettings.RaiseException = true;
                shiftSettings = da.GetShiftSettings(shiftSettings, !useCache);

                operatorSettings.RaiseException = true;
                operatorSettings = da.GetOperatorSettings(operatorSettings, !useCache);
            }
        }
        private bool OperatorCanReplace(int operatorId)
        {
            if (!UseShiftManagement || operatorSettings == null)
                return false;

            // Find the operator in operatorsRfid to get their RecNum
            Operator op = (Operator)operatorsRfid.GetById(operatorId);
            if (op == null)
                return false;

            // Look up their setting by OperatorRecNum
            OperatorSetting setting = operatorSettings
                .OfType<OperatorSetting>()
                .FirstOrDefault(s => s.OperatorRecNum == op.RecNum);

            return setting != null && setting.CanReplaceOperator;
        }
        private bool OperatorCanLogoutShift(int operatorId)
        {
            if (!UseShiftManagement || operatorSettings == null)
                return false;

            // Find the operator in operatorsRfid to get their RecNum
            Operator op = (Operator)operatorsRfid.GetById(operatorId);
            if (op == null)
                return false;

            // Look up their setting by OperatorRecNum
            OperatorSetting setting = operatorSettings
                .OfType<OperatorSetting>()
                .FirstOrDefault(s => s.OperatorRecNum == op.RecNum);

            return setting != null && setting.CanLogoutShift;
        }
        private void getOperatorsData(bool useCache)
        {
            operatorsRfid.RaiseException = true;
            operatorsRfid = da.GetOperators(operatorsRfid, !useCache);
            getOperatorPermissions(false);
            if (operatorsRfid.UpdatedFromDB)
            {               
                OperatorStates.Clear();
                foreach (Operator op in operatorsRfid)
                {
                    addOperatorState(op.IdJensen, -1, -1, DateTime.MinValue, DateTime.MinValue, false);
                }
                operatorsRfid.Reset();
                OperatorStates.Reset();
            }
            if (operatorsRfid.Count == 0)
            {
                feedback(this.GetType().Name + " " + MethodInfo.GetCurrentMethod() + " Operator Count = 0. UpdatedFromDB = " + operatorsRfid.UpdatedFromDB);
                operatorsRfid.IsValid = false;
            }
        }
        private void getOperatorPermissions(bool useCache)
        {
            if (UseOperatorPermissions)
            {
                availablePermissionsFromDb = da.GetPermissions(availablePermissionsFromDb, !useCache);
                operatorPermissionsFromDb = da.GetOperatorPermissions(operatorPermissionsFromDb, !useCache);
                foreach (Operator op in operatorsRfid)
                {
                    op.PermissionsAvailableFromDb = availablePermissionsFromDb;
                    op.OperatorPermissionsFromDb = operatorPermissionsFromDb;
                }
            }
        }
        private void addOperatorState(int operatorid, int machineid, int subid, DateTime login, DateTime logout, bool isloggedin)
        {
            OperatorLoginState opState = new OperatorLoginState(operatorid, machineid, subid, login, logout, isloggedin, remoteOperatorLogs, logData, machineSubIDs);
            OperatorStates.Add(opState);
        }

        private void getTags(bool useCache)
        {
            tags.RaiseException = true;
            tags = da.GetAllTags(tags, !useCache);
            if (tags.UpdatedFromDB)
            {
                TagsRfid.Clear();
                foreach (Tag tag in tags)
                {
                    if (tag.TagReferenceTable == "tblOperators")
                    {
                        addTag(tag.ReferenceID, tag.TagData);
                    }
                }
                TagsRfid.Reset();
            }
            //if (TagsRfid.Count == 0)
            //{
            //    TextFeedback(this.GetType().Name + " " + MethodInfo.GetCurrentMethod() + " TagsRfid Count = 0. UpdatedFromDB = " + operatorsRfid.UpdatedFromDB);
            //}
        }

        private void addTag(int id, string data)
        {
            Tag tag = new Tag();
            tag.ID = id - 1;
            tag.TagData = data;
            tag.TagReferenceTable = "tblOperators";
            tag.ReferenceID = id;
            TagsRfid.Add(tag);
        }
        private void PrintToFile(string path, string msg)
        {
            if (DebugMode)
            {
                try
                {
                    using (StreamWriter sw = File.AppendText(path))
                    {
                        sw.WriteLine(DateTime.Now + "| " + msg + Environment.NewLine);
                    }
                }
                catch (Exception ex)
                {
                    feedback(ex.Message + Environment.NewLine);
                }
            }
        }
        private void processRemoteOperatorLog()
        {
            if (remoteOperatorLogs.UpdatedFromDB || operatorsRfid.UpdatedFromDB)
            {
                // remoteOperatorLogs is ordered by RemoteOperatorLogId
                foreach (RemoteOperatorLog log in remoteOperatorLogs)
                {
                    PrintToFile("processRemoteOperatorLog.log", "LogTime: " + log.LogTime + ", RemoteId: " + log.RemoteOperatorLogID + ", State: " + log.State + ", SubregType: " + log.SubregType + ", OperartorId: " + log.OperatorID + ", MachineId: " + log.MachineID + ", SubId: " + log.SubID);
                    bool isLoggedIn = (log.State == 1) && (log.SubregType == 0);
                    DateTime loginTime = DateTime.MinValue;
                    DateTime logoutTime = DateTime.MaxValue;
                    bool isClockInOut = (log.SubregType == 5);
                    bool isBreak = (log.SubregType == 6);
                    if (isLoggedIn)
                        loginTime = log.LogTime;
                    else
                        logoutTime = log.LogTime;
                    OperatorLoginState opState = (OperatorLoginState)OperatorStates.GetByOperatorID(log.OperatorID);

                    // GUARD: Prevent re-processing logs that were already handled by this service's card scan handler
                    // This gives card scans priority over processRemoteOperatorLog
                    // Only process logs from OTHER applications or true service start recovery
                    if (opState != null && isLoggedIn)
                    {
                        bool alreadyProcessedByCardScan = (
                            opState.IsLoggedIn &&
                            opState.MachineID == log.MachineID &&
                            opState.SubID == log.SubID &&
                            opState.LoginTime >= log.LogTime
                        );

                        if (alreadyProcessedByCardScan)
                        {
                            // Card scan handler already processed this login - skip to avoid duplicate processing
                            // This prevents race condition where timer tick processes card scan before card handler
                            PrintToFile("processRemoteOperatorLog.log", $"Skipping already-processed login: Operator {log.OperatorID}, Machine {log.MachineID}, LoginTime {opState.LoginTime:HH:mm:ss} >= LogTime {log.LogTime:HH:mm:ss}");
                            continue;
                        }
                    }

                    if (opState == null) //shouldn't happen
                    {
                        PrintToFile("processRemoteOperatorLog.log", "opState not found so creating");
                        opState = new OperatorLoginState(log.OperatorID, log.MachineID, log.SubID, loginTime, logoutTime, isLoggedIn, remoteOperatorLogs, logData, machineSubIDs);
                        OperatorStates.Add(opState);
                    }

                    if (log.SubregType == 0)
                    {
                        opState.ClockedIn = true;
                        opState.MachineID = log.MachineID;
                        opState.SubID = log.SubID;
                        if (isLoggedIn)
                            opState.LoginTime = loginTime;
                        else
                            opState.LogoutTime = logoutTime;
                        opState.IsLoggedIn = isLoggedIn;

                        // Rule SS1: Service Start Recovery Logic
                        // Backfill shift info for operators logged in when shift management is enabled
                        // ONLY backfill if shift info is not already set (prevent overwriting on data refreshes)
                        // Per spec: Store ONLY the ActiveShiftId, never store shift times
                        if (UseShiftManagement && isLoggedIn && !opState.ActiveShiftId.HasValue)
                        {
                            // Find shift that was covering the operator's ORIGINAL login time (not current time)
                            Shift loginShift = GetActiveShift(log.MachineID, opState.LoginTime);

                            if (loginShift != null)
                            {
                                // Shift was covering login time - assign that shift
                                opState.ActiveShiftId = loginShift.ShiftID;
                                feedback($"[ServiceStart] Operator {log.OperatorID} at Machine {log.MachineID}, Sub {log.SubID} - " +
                                        $"Assigned to shift {loginShift.ShiftName} (ID={loginShift.ShiftID}) covering original login time {opState.LoginTime:HH:mm:ss}");
                            }
                            else
                            {
                                // No shift covered login time - check if machine has active shift NOW
                                Shift currentShift = GetActiveShift(log.MachineID);

                                if (currentShift != null)
                                {
                                    // Machine has active shift now, but operator didn't log into it
                                    // Log operator out per spec
                                    feedback($"[ServiceStart] Operator {log.OperatorID} at Machine {log.MachineID}, Sub {log.SubID} - " +
                                            $"Logging out: no shift covered original login, but machine has active shift {currentShift.ShiftName} now");
                                    opState.Logout(DateTime.Now);
                                }
                                else
                                {
                                    // No shift covering login, no current shift - keep logged in without shift info
                                    feedback($"[ServiceStart] Operator {log.OperatorID} at Machine {log.MachineID}, Sub {log.SubID} - " +
                                            $"Kept logged in: no shift covered login, machine not using shifts");
                                }
                            }
                        }

                        PrintToFile("processRemoteOperatorLog.log", "SubregType==0: IsLoggedIn==" + isLoggedIn);
                    }
                    if (isClockInOut)
                    {
                        opState.ClockedIn = (log.State == 1);
                        opState.MachineID = -1;
                        opState.SubID = -1;
                        PrintToFile("processRemoteOperatorLog.log", "IsClockedIn==" + opState.ClockedIn);
                    }
                    if (log.SubregType == 6)
                    {
                        opState.ClockedIn = true;
                        opState.MachineID = -1;
                        opState.SubID = -1;
                        if (log.State == 1)
                        {
                            opState.BreakID = log.SubregTypeID;
                        }
                        else
                        {
                            opState.BreakID = -1;
                        }
                        PrintToFile("processRemoteOperatorLog.log", "SubregType==6: BreakId==" + opState.BreakID);
                    }

                }
                SetUpdateAllLEDsNow();
                //DebugOperatorState();
            }
        }
        private void DebugOperatorState()
        {
            foreach (var opstate in OperatorStates)
            {
                PrintToFile("operatorStates.log", "OperatorId: " + opstate.OperatorID + ", MachineId: " + opstate.MachineID + ", SubId: " + opstate.SubID + ", LoginTime: " + opstate.LoginTime + ", LogoutTime: " + opstate.LogoutTime + ", IsClockedIn: " + opstate.ClockedIn + ", IsLoggedIn: " + opstate.IsLoggedIn + ", BreakId: " + opstate.BreakID);
            }
        }
        private void getMachineSubids(bool useCache)
        {
            machineSubIDs.RaiseException = true;
            machineSubIDs = da.GetMachineSubIDs(machineSubIDs, !useCache);
            if (machineSubIDs.Count == 0)
            {
                feedback(this.GetType().Name + " " + MethodInfo.GetCurrentMethod() + " Machine Subids = 0. UpdatedFromDB = " + machineSubIDs.UpdatedFromDB);
                machineSubIDs.IsValid = false;
            }
        }

        public void SetUpdateAllLEDsNow()
        {
            foreach (RfIdeasTcpReader tcpReader in TcpReaders)
            {
                tcpReader.UpdateLEDNow = true;
                PrintToFile("ReaderLEDState.log", "Reader=" + tcpReader.IpAddress + ", LED=" + tcpReader.CurrentLED);
            }
        }

        private void getRemoteOperatorLog(bool useCache)
        {
            remoteOperatorLogs.RaiseException = true;
            remoteOperatorLogs.ClearSelectRestrictions();
            remoteOperatorLogs.AddSelectRestriction(new RestrictionRec("LogTime", DateTime.Today.AddDays(-1), ComparisonType.MoreThanEqual));
            remoteOperatorLogs = (RemoteOperatorLogsGroupedByOperator)da.DBDataListSelect(remoteOperatorLogs, !useCache, useCache);
            remoteOperatorLogs.UpdateFieldNames = new List<string>();
            remoteOperatorLogs.UpdateFieldNames.Add("Operator_Remote");
        }
        private bool GetAutoLogoutAtShiftEnd()
        {
            feedback($"[AutoLogout] GetAutoLogoutAtShiftEnd() called - UseShiftManagement={UseShiftManagement}, shiftSettings={(shiftSettings == null ? "NULL" : $"Count={shiftSettings.Count}")}");

            if (!UseShiftManagement || shiftSettings == null)
                return false;

            ShiftSetting setting = (ShiftSetting)shiftSettings
                .OfType<ShiftSetting>()
                .FirstOrDefault(s => s.SettingName == "AutoLogoutAtShiftEnd");

            if (setting != null)
            {
                bool result = bool.TryParse(setting.SettingValue, out bool parsedValue) && parsedValue;
                feedback($"[AutoLogout] Setting found: SettingValue='{setting.SettingValue}', Parsed={result}");
                return result;
            }

            feedback($"[AutoLogout] Setting 'AutoLogoutAtShiftEnd' NOT FOUND in shiftSettings collection");
            return false; // Default to false if setting not found
        }
        public int AutoLogoutExpiredShifts()
        {
            feedback($"[AutoLogout] === Auto-logout check starting at {DateTime.Now:HH:mm:ss} ===");

            bool autoLogoutEnabled = GetAutoLogoutAtShiftEnd();

            if (!UseShiftManagement || !autoLogoutEnabled)
            {
                feedback($"[AutoLogout] EXITING EARLY: UseShiftManagement={UseShiftManagement}, autoLogoutEnabled={autoLogoutEnabled}");
                return 0;
            }

            feedback($"[AutoLogout] Checking {OperatorStates.Count} operator login states...");
            int logoutCount = 0;

            try
            {
                foreach (OperatorLoginState opState in OperatorStates)
                {
                    if (opState.IsLoggedIn)
                    {
                        feedback($"[AutoLogout] Checking Operator {opState.OperatorID}, Machine {opState.MachineID}, Sub {opState.SubID}, ActiveShiftId={opState.ActiveShiftId}");

                        // Check if operator has shift info
                        if (opState.ActiveShiftId.HasValue)
                        {
                            // Get shift details from datalayer (per spec: never store shift times, always query)
                            Shift loginShift = (Shift)shifts.GetById(opState.ActiveShiftId.Value);

                            if (loginShift != null)
                            {
                                DateTime now = DateTime.Now;

                                // Calculate the shift end time based on operator's login date
                                // Use the operator's login date as the base to handle overnight shifts correctly
                                DateTime loginDate = opState.LoginTime.Date;
                                DateTime shiftEndTime = loginDate.Add(loginShift.EndTime);

                                // Handle midnight-crossing shifts (e.g., 22:00-06:00)
                                if (loginShift.EndTime <= loginShift.StartTime)
                                {
                                    // Shift crosses midnight
                                    shiftEndTime = shiftEndTime.AddDays(1);
                                }

                                // Determine if shift has ended
                                bool shiftEnded = false;
                                if (loginShift.StartTime <= loginShift.EndTime)
                                {
                                    // Normal shift (doesn't cross midnight)
                                    shiftEnded = (now >= shiftEndTime);
                                }
                                else
                                {
                                    // Midnight-crossing shift
                                    // Shift has ended if we're past shift end time AND before shift start time
                                    if (now.TimeOfDay >= loginShift.EndTime && now.TimeOfDay < loginShift.StartTime)
                                    {
                                        shiftEnded = true;
                                    }
                                }

                                feedback($"[AutoLogout] Operator {opState.OperatorID}, Machine {opState.MachineID}, " +
                                         $"LoginShift '{loginShift.ShiftName}' (ID={loginShift.ShiftID}), " +
                                         $"ShiftEnd={loginShift.EndTime}, ShiftEnded={shiftEnded}");

                                // Auto-logout if shift has ended (per spec: at shift end time, NOT after delta+)
                                if (shiftEnded)
                                {
                                    feedback($"[AutoLogout] LOGGING OUT: Operator {opState.OperatorID}, Machine {opState.MachineID}");
                                    opState.Logout(DateTime.Now);
                                    logoutCount++;

                                    // Update the LED on the reader
                                    foreach (RfIdeasTcpReader reader in TcpReaders)
                                    {
                                        if (reader.MachineID == opState.MachineID && reader.SubID == opState.SubID)
                                        {
                                            reader.RedLED();
                                            reader.IsActiveStation = false;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                feedback($"[AutoLogout] Shift ID {opState.ActiveShiftId.Value} NOT FOUND in database for Operator {opState.OperatorID} - clearing orphaned shift info");

                                // Clear orphaned shift references (per spec: handle shift-not-found scenario)
                                opState.ActiveShiftId = null;
                            }
                        }
                        else
                        {
                            // Operator logged in without shift info
                            // Per spec: ONLY enforce shift management if machine is using shifts
                            // Check if their machine is SUPPOSED to be using shifts
                            Shift machineActiveShift = GetActiveShift(opState.MachineID);

                            if (machineActiveShift != null)
                            {
                                // Machine IS using shifts but operator has no shift info - orphaned login
                                // This happens when operator logged in before shift management was enabled
                                feedback($"[AutoLogout] Operator {opState.OperatorID} logged in without shift info on shift-enabled machine (shift: {machineActiveShift.ShiftName}) - LOGGING OUT (orphaned login)");
                                opState.Logout(DateTime.Now);
                                logoutCount++;

                                // Update the LED on the reader
                                foreach (RfIdeasTcpReader reader in TcpReaders)
                                {
                                    if (reader.MachineID == opState.MachineID && reader.SubID == opState.SubID)
                                    {
                                        reader.RedLED();
                                        reader.IsActiveStation = false;
                                    }
                                }
                            }
                            else
                            {
                                // Machine is NOT using shifts - operator correctly has no shift info
                                // Per spec: shift management rules do NOT apply when machine has no active shift
                                feedback($"[AutoLogout] Operator {opState.OperatorID} has no shift info - machine {opState.MachineID} not using shifts, skipping");
                                // Do NOT log out
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                feedback($"[AutoLogout] ERROR: {ex.Message}");
                feedback($"[AutoLogout] Stack trace: {ex.StackTrace}");
            }

            feedback($"[AutoLogout] === Auto-logout check complete: {logoutCount} operators logged out ===");
            return logoutCount;
        }

        /// <summary>
        /// Rule TM2: Shift Transition Check - runs every minute after auto-logout
        /// Handles transitioning operators between shifts and assigning shifts to operators without shift info
        /// </summary>
        public int ShiftTransitionCheck()
        {
            if (!UseShiftManagement)
                return 0;

            feedback($"[ShiftTransition] === Shift transition check starting at {DateTime.Now:HH:mm:ss} ===");
            int transitionCount = 0;

            try
            {
                foreach (OperatorLoginState opState in OperatorStates)
                {
                    if (!opState.IsLoggedIn)
                        continue; // Only process logged-in operators

                    if (opState.ActiveShiftId.HasValue)
                    {
                        // Rule TM2.1: Operator has shift info - check if should transition
                        Shift currentShift = (Shift)shifts.GetById(opState.ActiveShiftId.Value);

                        if (currentShift == null)
                        {
                            // Shift not found - clear and treat as no shift
                            feedback($"[ShiftTransition] Shift ID {opState.ActiveShiftId.Value} not found for Operator {opState.OperatorID} - clearing");
                            opState.ActiveShiftId = null;
                            // Fall through to Rule TM2.2
                        }
                        else
                        {
                            // Calculate shift end time + DeltaPlus
                            DateTime loginDate = opState.LoginTime.Date;
                            DateTime shiftEndTime = loginDate.Add(currentShift.EndTime);

                            // Handle midnight-crossing shifts
                            if (currentShift.EndTime <= currentShift.StartTime)
                            {
                                shiftEndTime = shiftEndTime.AddDays(1);
                            }

                            DateTime deltaPlusExpiry = shiftEndTime.AddMinutes(currentShift.DeltaPlus);
                            DateTime now = DateTime.Now;

                            // Check if still within delta+ period
                            if (now <= deltaPlusExpiry)
                            {
                                // Still in current shift's grace period - no action
                                continue;
                            }

                            // Delta+ has expired - determine next action
                            feedback($"[ShiftTransition] Operator {opState.OperatorID} Delta+ expired at {deltaPlusExpiry:HH:mm:ss}");

                            Shift newShift = GetActiveShift(opState.MachineID);

                            if (newShift != null)
                            {
                                // Transition to new shift
                                opState.ActiveShiftId = newShift.ShiftID;
                                transitionCount++;
                                feedback($"[ShiftTransition] Operator {opState.OperatorID} transitioned from shift {currentShift.ShiftID} to shift {newShift.ShiftID} ({newShift.ShiftName})");
                            }
                            else
                            {
                                // No active shift - clear shift info
                                opState.ActiveShiftId = null;
                                transitionCount++;
                                feedback($"[ShiftTransition] Operator {opState.OperatorID} shift info cleared - no active shift");
                            }
                        }
                    }

                    // Rule TM2.2: Check if operator without shift info should be assigned a shift
                    if (!opState.ActiveShiftId.HasValue && opState.IsLoggedIn)
                    {
                        Shift activeShift = GetActiveShift(opState.MachineID);

                        if (activeShift != null)
                        {
                            // Assign shift to operator
                            opState.ActiveShiftId = activeShift.ShiftID;
                            transitionCount++;
                            feedback($"[ShiftTransition] Operator {opState.OperatorID} assigned to shift {activeShift.ShiftID} ({activeShift.ShiftName}) - shift started while logged in");
                        }
                        // No action if no active shift
                    }
                }
            }
            catch (Exception ex)
            {
                feedback($"[ShiftTransition] ERROR: {ex.Message}");
                feedback($"[ShiftTransition] Stack trace: {ex.StackTrace}");
            }

            feedback($"[ShiftTransition] === Shift transition check complete: {transitionCount} transitions ===");
            return transitionCount;
        }

        /// <summary>
        /// Gets the active shift for a machine at the current time
        /// </summary>
        /// <param name="machine_idJensen">Machine ID</param>
        /// <returns>Active Shift or null if no shift is active</returns>
        private Shift GetActiveShift(int machine_idJensen)
        {
            return GetActiveShift(machine_idJensen, DateTime.Now);
        }

        /// <summary>
        /// Gets the active shift for a machine at a specific time
        /// </summary>
        private Shift GetActiveShift(int machine_idJensen, DateTime checkTime)
        {
            try
            {
                DayOfWeek currentDay = checkTime.DayOfWeek;
                TimeSpan currentTimeOfDay = checkTime.TimeOfDay;
                int dayInt = (int)currentDay;

                feedback($"[GetActiveShift] Looking for machine_idJensen={machine_idJensen}, day={currentDay}, time={currentTimeOfDay:hh\\:mm\\:ss}");
                feedback($"[GetActiveShift] Total shift assignments loaded: {shiftMachineAssignments.Count}");

                // Get all shift assignments for this machine on this day of week
                var assignments = shiftMachineAssignments
                    .OfType<ShiftMachineAssignment>()
                    .Where(a => a.MachineIdJensen == machine_idJensen
                             && a.DayOfWeek == (int)currentDay
                             && a.IsActive)
                    .ToList();

                feedback($"[GetActiveShift] Found {assignments.Count} assignments matching machine_idJensen={machine_idJensen}, day={dayInt}");

                if (assignments.Count == 0)
                {
                    // Debug: Show what we have for this machine
                    var allForMachine = shiftMachineAssignments
                        .OfType<ShiftMachineAssignment>()
                        .Where(a => a.MachineIdJensen == machine_idJensen)
                        .ToList();
                    feedback($"[GetActiveShift] Total assignments for machine_idJensen={machine_idJensen}: {allForMachine.Count}");

                    if (allForMachine.Count > 0)
                    {
                        var sample = allForMachine.First();
                        feedback($"[GetActiveShift] Sample assignment: ShiftID={sample.ShiftID}, MachineRecNum={sample.MachineRecNum}, MachineIdJensen={sample.MachineIdJensen}, DayOfWeek={sample.DayOfWeek}, IsActive={sample.IsActive}");
                    }

                    return null;
                }

                // Check each assignment to see if its shift is currently active
                foreach (var assignment in assignments)
                {
                    Shift shift = (Shift)shifts.GetById(assignment.ShiftID);
                    if (shift != null && shift.IsActive)
                    {
                        // Handle shifts that cross midnight
                        if (shift.StartTime <= shift.EndTime)
                        {
                            // Normal shift (e.g., 6am - 2pm)
                            if (currentTimeOfDay >= shift.StartTime && currentTimeOfDay < shift.EndTime)
                                return shift;
                        }
                        else
                        {
                            // Midnight-crossing shift (e.g., 10pm - 6am)
                            if (currentTimeOfDay >= shift.StartTime || currentTimeOfDay < shift.EndTime)
                                return shift;
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                debugInfo($"GetActiveShift error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Checks if current time is within the combined delta period (DeltaMinus before OR DeltaPlus after shift end)
        /// Per spec: Delta- is (ShiftEnd - DeltaMinus) to ShiftEnd
        ///           Delta+ is ShiftEnd to (ShiftEnd + DeltaPlus)
        /// </summary>
        /// <param name="shift">The shift to check</param>
        /// <param name="currentTime">Time to check (typically DateTime.Now)</param>
        /// <returns>True if within either delta period (logout allowed), false otherwise</returns>
        private bool IsWithinShiftDelta(Shift shift, DateTime currentTime)
        {
            if (shift == null)
                return true; // No shift = no restrictions

            try
            {
                TimeSpan currentTimeOfDay = currentTime.TimeOfDay;
                TimeSpan shiftStartTime = shift.StartTime;
                TimeSpan shiftEndTime = shift.EndTime;

                // Calculate delta boundaries using SEPARATE DeltaMinus and DeltaPlus values
                TimeSpan deltaMinusStart = shiftEndTime.Subtract(TimeSpan.FromMinutes(shift.DeltaMinus));
                TimeSpan deltaPlusEnd = shiftEndTime.Add(TimeSpan.FromMinutes(shift.DeltaPlus));

                if (shiftStartTime <= shiftEndTime)
                {
                    // Normal shift (doesn't cross midnight) e.g., 06:00 - 14:00
                    // Delta- period: (14:00 - 15min) to 14:00 = 13:45 to 14:00
                    // Delta+ period: 14:00 to (14:00 + 10min) = 14:00 to 14:10

                    // Check Delta- (before shift end)
                    if (currentTimeOfDay >= deltaMinusStart && currentTimeOfDay < shiftEndTime)
                    {
                        return true; // Within Delta- period
                    }

                    // Check Delta+ (after shift end)
                    if (currentTimeOfDay >= shiftEndTime && currentTimeOfDay <= deltaPlusEnd)
                    {
                        return true; // Within Delta+ period
                    }
                }
                else
                {
                    // Midnight-crossing shift e.g., 22:00 - 06:00
                    // Delta- period: (06:00 - 15min) to 06:00 = 05:45 to 06:00
                    // Delta+ period: 06:00 to (06:00 + 10min) = 06:00 to 06:15

                    // Handle negative time (before midnight)
                    if (deltaMinusStart < TimeSpan.Zero)
                    {
                        deltaMinusStart = deltaMinusStart.Add(TimeSpan.FromHours(24));
                    }

                    // Check if we're in the Delta- period (before shift end)
                    if (currentTimeOfDay >= deltaMinusStart || currentTimeOfDay < shiftEndTime)
                    {
                        // We're potentially in range, but need to verify we're actually in Delta-
                        if (currentTimeOfDay >= deltaMinusStart && currentTimeOfDay < TimeSpan.FromHours(24))
                        {
                            return true; // In Delta- on same day as shift start
                        }
                        if (currentTimeOfDay >= TimeSpan.Zero && currentTimeOfDay < shiftEndTime)
                        {
                            // We're after midnight - check if within Delta-
                            if (currentTimeOfDay >= deltaMinusStart || deltaMinusStart > shiftEndTime)
                            {
                                return true; // In Delta- after midnight
                            }
                        }
                    }

                    // Check Delta+ (after shift end, handling potential overflow past midnight)
                    if (deltaPlusEnd >= TimeSpan.FromHours(24))
                    {
                        // Delta+ wraps past midnight
                        TimeSpan deltaPlusEndWrapped = deltaPlusEnd.Subtract(TimeSpan.FromHours(24));
                        if (currentTimeOfDay >= shiftEndTime && currentTimeOfDay < TimeSpan.FromHours(24))
                        {
                            return true; // In Delta+ before midnight wrap
                        }
                        if (currentTimeOfDay >= TimeSpan.Zero && currentTimeOfDay <= deltaPlusEndWrapped)
                        {
                            return true; // In Delta+ after midnight wrap
                        }
                    }
                    else
                    {
                        // Delta+ doesn't wrap past midnight
                        if (currentTimeOfDay >= shiftEndTime && currentTimeOfDay <= deltaPlusEnd)
                        {
                            return true; // Within Delta+
                        }
                    }
                }

                return false; // Outside both delta periods
            }
            catch (Exception ex)
            {
                debugInfo($"IsWithinShiftDelta error: {ex.Message}");
                return true; // On error, allow logout (fail-safe)
            }
        }

        /// <summary>
        /// STEP 1: Evaluates if an operator can login at a target station
        /// Per spec: Must check capacity, replacement eligibility, and delta periods
        /// </summary>
        /// <param name="reason">Reason for denial if login not allowed</param>
        /// <param name="operatorsToReplace">List of operators to replace (if any)</param>
        /// <returns>True if login allowed, false otherwise</returns>
        private bool CanLoginAtTargetStation(
            int operatorId, int machineId, int subId, Shift activeShift, RfIdeasTcpReader tcpReader,
            out string reason, out List<OperatorLoginState> operatorsToReplace, bool isInitialLogin)
        {
            // Get station capacity information
            List<OperatorLoginState> operatorsAtStation = OperatorStates.GetAllLoggedInByMachineSubID(machineId, subId);
            int currentCount = operatorsAtStation.Count;
            int maxLogins = tcpReader.MaxLogins;

            feedback($"[STEP1] Target station check: Machine {machineId}, Sub {subId}, Current: {currentCount}/{maxLogins}");

            // Check if station is empty (Rule A.1.1)
            if (currentCount == 0)
            {
                // Per corrected spec: CANREPLACEOPERATOR restriction only applies to remote logout scenarios
                // Initial login (Scenario A) allows all operators including CANREPLACEOPERATOR to log into empty stations
                // Remote logout (Scenarios C & D) denies CANREPLACEOPERATOR from moving to empty stations
                if (!isInitialLogin && OperatorCanReplace(operatorId))
                {
                    reason = "CANREPLACEOPERATOR requires someone to replace - station is empty (remote logout denied)";
                    operatorsToReplace = null;
                    return false;
                }

                // Empty station, no replacement needed - Rule A.1.1
                reason = null;
                operatorsToReplace = new List<OperatorLoginState>();
                return true;
            }

            // Station has operators - check permissions and capacity
            bool hasReplacePermission = OperatorCanReplace(operatorId);

            // If operator has CANREPLACEOPERATOR, they can replace ALL operators (Rule A.2)
            if (hasReplacePermission)
            {
                feedback($"[STEP1] Operator {operatorId} has CANREPLACEOPERATOR - can replace all {currentCount} operators");
                reason = null;
                operatorsToReplace = operatorsAtStation;
                return true;
            }

            // No CANREPLACEOPERATOR - check if station has available capacity (Rule A.1.2)
            if (currentCount < maxLogins)
            {
                feedback($"[STEP1] Station has available capacity ({currentCount}/{maxLogins}) - can login to available slot");
                reason = null;
                operatorsToReplace = new List<OperatorLoginState>();
                return true;
            }

            // Station is FULL - check if ANY operator is within delta (Rule A.3)
            if (UseShiftManagement && activeShift != null)
            {
                foreach (var existingOp in operatorsAtStation)
                {
                    if (existingOp.ActiveShiftId.HasValue)
                    {
                        Shift existingShift = (Shift)shifts.GetById(existingOp.ActiveShiftId.Value);
                        if (existingShift != null && IsWithinShiftDelta(existingShift, DateTime.Now))
                        {
                            feedback($"[STEP1] Operator {existingOp.OperatorID} at station within delta - can replace");
                            reason = null;
                            operatorsToReplace = new List<OperatorLoginState> { existingOp };
                            return true;
                        }
                    }
                }

                // No one within delta
                reason = string.Format("Station full ({0}/{1}), no operators within delta, no CANREPLACEOPERATOR", currentCount, maxLogins);
                operatorsToReplace = null;
                return false;
            }

            // No shift management or no active shift - station full but no special rules apply
            reason = string.Format("Station full ({0}/{1})", currentCount, maxLogins);
            operatorsToReplace = null;
            return false;
        }

        /// <summary>
        /// STEP 2: Evaluates if an operator can leave their current station
        /// Per spec: Only called if STEP 1 succeeds
        /// </summary>
        /// <param name="reason">Reason for denial if cannot leave</param>
        /// <returns>True if can leave, false otherwise</returns>
        private bool CanLeaveCurrentStation(
            OperatorLoginState opState, int operatorId, bool targetHasOperators, out string reason)
        {
            feedback($"[STEP2] Checking if Operator {operatorId} can leave Machine {opState.MachineID}, Sub {opState.SubID}");

            // If operator has no shift info, can leave freely
            if (!opState.ActiveShiftId.HasValue)
            {
                feedback($"[STEP2] No shift info - can leave freely");
                reason = null;
                return true;
            }

            // Has shift info - check permissions and delta

            // Check CANLOGOUTOFSHIFT permission (bypasses all shift checks)
            if (OperatorCanLogoutShift(operatorId))
            {
                feedback($"[STEP2] Has CANLOGOUTOFSHIFT permission - can leave");
                reason = null;
                return true;
            }

            // Check CANREPLACEOPERATOR permission AND target has operators
            // Per spec: "When moving machines: Allows leaving current station outside delta (only when target has operators to replace)"
            if (OperatorCanReplace(operatorId) && targetHasOperators)
            {
                feedback($"[STEP2] Has CANREPLACEOPERATOR and target has operators to replace - can leave");
                reason = null;
                return true;
            }

            // Get operator's assigned shift details
            Shift operatorShift = (Shift)shifts.GetById(opState.ActiveShiftId.Value);

            if (operatorShift == null)
            {
                // Shift not found - treat as no shift, can leave
                feedback($"[STEP2] Shift ID {opState.ActiveShiftId.Value} not found - clearing orphaned reference, can leave");
                opState.ActiveShiftId = null;
                reason = null;
                return true;
            }

            // Check if within delta period
            bool withinDelta = IsWithinShiftDelta(operatorShift, DateTime.Now);

            if (withinDelta)
            {
                feedback($"[STEP2] Within delta period of shift {operatorShift.ShiftName} - can leave");
                reason = null;
                return true;
            }

            // Outside delta, no permissions - cannot leave
            reason = string.Format("Outside delta period of shift {0}, no permissions to leave", operatorShift.ShiftName);
            return false;
        }

        public void ReloadData(bool useCache)
        {
            try
            {
                getShiftData(useCache);
            }
            catch (Exception ex)
            {
                debugInfo(this.GetType().Name + " " + MethodInfo.GetCurrentMethod() + ex.Message);
            }
            try
            {
                getOperatorsData(useCache);
            }
            catch (Exception ex)
            {
                debugInfo(this.GetType().Name + " " + MethodInfo.GetCurrentMethod() + ex.Message);
            }
            try
            {
                getMachineSubids(useCache);
            }
            catch (Exception ex)
            {
                debugInfo(this.GetType().Name + " " + MethodInfo.GetCurrentMethod() + ex.Message);
            }
            try
            {
                getRemoteOperatorLog(useCache);
            }
            catch (Exception ex)
            {
                debugInfo(this.GetType().Name + " " + MethodInfo.GetCurrentMethod() + ex.Message);
            }
            try
            {
                processRemoteOperatorLog();
            }
            catch (Exception ex)
            {
                debugInfo(this.GetType().Name + " " + MethodInfo.GetCurrentMethod() + ex.Message);
            }
            try
            {
                getTags(useCache);
            }
            catch (Exception ex)
            {
                debugInfo(this.GetType().Name + " " + MethodInfo.GetCurrentMethod() + ex.Message);
            }
        }

        public int EvalCurrentLogins()
        {
            int count = 0;
            foreach (OperatorLoginState ols in OperatorStates)
            {
                if (ols.IsLoggedIn)
                {
                    count++;
                }
            }
            return count;
        }

        public int AutoLogoutNow()
        {
            int count = EvalCurrentLogins();
            if (count > 0)
            {
                feedback("Attempting to log out " + count + " operators" + Environment.NewLine);
                foreach (OperatorLoginState ols in OperatorStates)
                {
                    if (ols.IsLoggedIn)
                    {
                        ols.Logout(DateTime.Now);
                    }
                    
                }
                EvalCurrentLogins();
            }
            
            clearExcessData();
            SetUpdateAllLEDsNow();

            return count;
        }

        private void feedback(string aString)
        {
            if (TextFeedback != null)
            {
                TextFeedback(aString);
            }
        }

        private void debugInfo(string aString)
        {
            if ((TextFeedback != null) && DebugMode)
            {
                PrintToFile("CentralOperator.log", aString);
                TextFeedback(aString + Environment.NewLine);
            }
        }

        public void Start()
        {
            sharedStatus = SharedReaderStatus.CreatePublisher();
            ReloadData(false);
            InitialiseReaderConnections();
            SetUpdateAllLEDsNow();
        }
        private ReaderStatusDto CreateReaderStatusDto(RfIdeasTcpReader reader)
        {
            // Get the logged-in operator for this reader
            int? operatorId = null;
            // Use GetLoggedInByMachineSubID to only get operators who are actually logged in
            OperatorLoginState opState = (OperatorLoginState)OperatorStates.GetLoggedInByMachineSubID(reader.MachineID, reader.SubID);

            debugInfo($"CreateReaderStatusDto: MachineID={reader.MachineID}, SubID={reader.SubID}, opState={(opState != null ? "found" : "null")}, IsLoggedIn={opState?.IsLoggedIn}, OperatorID={opState?.OperatorID}");            

            if (opState != null)
            {
                operatorId = opState.OperatorID;
            }

            return new ReaderStatusDto
            {
                LocationId = reader.LocationID,
                MachineId = reader.MachineID,
                SubId = reader.SubID,
                IsConnected = reader.IsConnected,
                LedColor = reader.CurrentLED ?? "GREY",
                IsActiveStation = reader.IsActiveStation,
                IpAddress = reader.IpAddress,
                Port = reader.Port,
                LastUpdate = DateTime.Now,
                OperatorId = operatorId,
                LastCardHex = reader.LastCardHex
            };
        }
        private void UpdateSingleReaderStatus(RfIdeasTcpReader reader)
        {
            try
            {
                var dto = CreateReaderStatusDto(reader);    
                sharedStatus.UpdateSingleReader(dto);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateSingleReaderStatus error: {ex.Message}");
                feedback($"UpdateSingleReaderStatus error: {ex.Message}");
            }
        }
        public void InitialiseReaderConnections()
        {
            var statuses = new List<ReaderStatusDto>();
            foreach (RfIdeasTcpReader tcpReader in TcpReaders)
            {
                // Get operator state and set IsActiveStation BEFORE creating the DTO
                OperatorLoginState opState = (OperatorLoginState)OperatorStates.GetByMachineSubID(tcpReader.MachineID,tcpReader.SubID);
                bool isloggedin = false;
                if (opState != null)
                    isloggedin = opState.IsLoggedIn;

                // Set IsActiveStation on the reader first
                tcpReader.IsActiveStation = isloggedin;

                tcpReader.AllowFeedback = DebugMode;
                tcpReader.StartMonitorTask(isloggedin);

                // Now create the DTO with the correct IsActiveStation value
                statuses.Add(CreateReaderStatusDto(tcpReader));
            }
            sharedStatus.UpdateAllReaders(statuses);
        }

        public void PingReaders()
        {
            Ping ping = new Ping();
            PingReply reply;
            foreach (RfIdeasTcpReader tcpReader in TcpReaders)
            {
                reply = ping.Send(tcpReader.IpAddress);
                if (reply.Status == IPStatus.Success)
                    feedback("Ping " + tcpReader.IpAddress.ToString() + " Reply" + Environment.NewLine);
                else
                    feedback("Ping " + tcpReader.IpAddress.ToString() + " Fail" + Environment.NewLine);
            }
        }

        public void Read()
        {
            foreach (RfIdeasTcpReader tcpReader in TcpReaders)
            {
                try
                {
                    read(tcpReader);
                }
                catch (Exception ex)
                {
                    feedback("Error during read: " + ex.Message + Environment.NewLine);
                }
            }
        }

        private void logCardRead(RfIdeasTcpReader tcpReader, string cardid)
        {
            TagReaderLogRec rec = new TagReaderLogRec();
            rec.LocationID = tcpReader.LocationID;
            rec.ScanTime = DateTime.Now;
            rec.TagData = cardid;
            rec.ForceNew = true;
            tagReaderLog.Add(rec);
            tagReaderLog.UpdateToDB();
        }
        public void read(RfIdeasTcpReader tcpReader)
        {
            try
            {
                // Smart polling: poll connected readers every cycle, disconnected readers less frequently
                bool shouldPoll = tcpReader.IsConnected; // Always poll if connected

                if (!shouldPoll)
                {
                    // For disconnected readers, only poll every 5 seconds
                    TimeSpan timeSinceLastPoll = DateTime.Now - tcpReader.LastPollTime;
                    shouldPoll = timeSinceLastPoll.TotalMilliseconds >= 5000;
                }

                if (!shouldPoll)
                {
                    // Skip polling this reader this cycle
                    return;
                }

                tcpReader.LastPollTime = DateTime.Now;

                int machine_idJensen = 0;
                int subid = 0;
                bool canLogin = true;
                string cardID = tcpReader.ReadCardID(out machine_idJensen, out subid);
                //string cardID = TestHarness(tcpReader.LocationID, out machineid, out subid);
                if (cardID != string.Empty)
                {
                    logCardRead(tcpReader, cardID);
                    tcpReader.LastCardHex = cardID;  // Store for display in CardReaderMonitor
                    //debugInfo(cardID + ", " + machineid.ToString() + ", " + subid.ToString() + Environment.NewLine);
                    Tag t = (Tag)TagsRfid.GetByName("tblOperators" + cardID);
                    if (t != null)
                    {
                        Operator op = (Operator)operatorsRfid.GetById(t.ReferenceID);
                        if (!op.Retired) // retired operators can't log in or out.
                        {
                            // ============ START STEP 4 CHANGES ============
                            // Get shift information if shift management is enabled
                            Shift activeShift = null;
                            bool machineHasActiveShift = false;
                            bool withinDelta = true;
                            if (UseShiftManagement)
                            {
                                activeShift = GetActiveShift(machine_idJensen);
                                machineHasActiveShift = (activeShift != null);

                                if (machineHasActiveShift)
                                {
                                    withinDelta = IsWithinShiftDelta(activeShift, DateTime.Now);

                                    feedback($"Shift Active: Machine {machine_idJensen}, Shift {activeShift.ShiftName} " +
                                                $"({activeShift.StartTime} - {activeShift.EndTime}), " +
                                                $"Within Delta: {withinDelta}");
                                    
                                }
                            }
                            // ============ END STEP 4 CHANGES ============
                            OperatorLoginState opState = (OperatorLoginState)OperatorStates.GetByOperatorID(t.ReferenceID);
                            if (opState != null)
                            {

                                tcpReader.HasBadRead = false;
                                if (opState.BreakID > 0)
                                {
                                    opState.BreakEnd(DateTime.Now, opState.BreakID);
                                    opState.BreakID = -1;
                                }
                                // ============ DETERMINE SCENARIO ============
                                bool isLoggedIn = opState.IsLoggedIn;
                                bool sameStation = isLoggedIn && (opState.MachineID == machine_idJensen) && (opState.SubID == subid);
                                bool sameMachine = isLoggedIn && (opState.MachineID == machine_idJensen) && (opState.SubID != subid);

                                if (!isLoggedIn)
                                {
                                    // ============ SCENARIO A: OPERATOR NOT LOGGED IN ANYWHERE ============
                                    feedback($"[ShiftManagement] Operator {op.IdJensen} not logged in - attempting login at Machine {machine_idJensen}, Sub {subid}");

                                    // PREREQUISITE CHECK per spec
                                    if (!UseShiftManagement || !machineHasActiveShift)
                                    {
                                        feedback($"[ShiftManagement] Shift management disabled or no active shift - using legacy logic (out of scope)");
                                        // Legacy logic would go here - for now, attempt login
                                        canLogin = true;
                                    }
                                    else
                                    {
                                        // STEP 1: Can login at target station?
                                        string denyReason;
                                        List<OperatorLoginState> operatorsToReplace;
                                        bool canLoginAtTarget = CanLoginAtTargetStation(
                                            t.ReferenceID, machine_idJensen, subid, activeShift, tcpReader,
                                            out denyReason, out operatorsToReplace, isInitialLogin: true);

                                        if (!canLoginAtTarget)
                                        {
                                            canLogin = false;
                                            feedback($"[ShiftManagement] Login DENIED: {denyReason}");
                                            tcpReader.OrangeLED();
                                        }
                                        else
                                        {
                                            canLogin = true;

                                            // Replace operators if needed
                                            if (operatorsToReplace != null && operatorsToReplace.Count > 0)
                                            {
                                                if (OperatorCanReplace(t.ReferenceID) && operatorsToReplace.Count > 1)
                                                {
                                                    // ShiftManagement SCENARIO A - Rule A.2
                                                    feedback($"[ShiftManagement] Mass Replacement: Operator {op.NameAndID} replacing {operatorsToReplace.Count} operators (CANREPLACEOPERATOR)");
                                                }
                                                else if (operatorsToReplace.Count == 1)
                                                {
                                                    // ShiftManagement SCENARIO A - Rule A.3
                                                    feedback($"[ShiftManagement] Replacing Operator {operatorsToReplace[0].OperatorID} (within delta)");
                                                }

                                                // Log out all operators being replaced
                                                foreach (var opToReplace in operatorsToReplace)
                                                {
                                                    opToReplace.Logout(DateTime.Now);
                                                }
                                                tcpReader.RedLED(); // Briefly red before turning green
                                            }
                                            else
                                            {
                                                int currentCount = OperatorStates.TotalLoggedInByMachineSubID(machine_idJensen, subid);
                                                if (currentCount == 0)
                                                {
                                                    // ShiftManagement SCENARIO A - Rule A.1.1
                                                    feedback($"[ShiftManagement] Login to empty station");
                                                }
                                                else
                                                {
                                                    // ShiftManagement SCENARIO A - Rule A.1.1
                                                    feedback($"[ShiftManagement] Login to available slot ({currentCount + 1}/{tcpReader.MaxLogins})");
                                                }
                                            }

                                            // Perform the login
                                            opState.Login(machine_idJensen, subid, DateTime.Now, activeShift);
                                            tcpReader.GreenLED();
                                            tcpReader.IsActiveStation = true;

                                            if (activeShift != null)
                                            {
                                                // ShiftManagement SCENARIO A
                                                feedback($"[ShiftManagement] Login successful: Operator {op.IdJensen} assigned to shift {activeShift.ShiftName} (ID={activeShift.ShiftID})");
                                            }
                                        }
                                    }
                                }
                                else if (sameStation)
                                {
                                    // ============ SCENARIO B: OPERATOR SCANS OWN STATION (LOGOUT ATTEMPT) ============
                                        // Per spec: Check delta against OPERATOR'S ASSIGNED SHIFT, not current active shift

                                        bool canLogoutDueToShift = true;

                                        // Check if operator has shift info
                                        if (UseShiftManagement && opState.ActiveShiftId.HasValue)
                                        {
                                            // Get the operator's ASSIGNED shift (not the current active shift)
                                            Shift operatorShift = (Shift)shifts.GetById(opState.ActiveShiftId.Value);

                                            if (operatorShift != null)
                                            {
                                                // Check if within delta of the OPERATOR'S shift
                                                bool operatorWithinDelta = IsWithinShiftDelta(operatorShift, DateTime.Now);

                                                feedback($"[Logout] Operator {op.IdJensen} assigned to shift {operatorShift.ShiftName} (ID={operatorShift.ShiftID}), within delta: {operatorWithinDelta}");

                                                if (!operatorWithinDelta)
                                                {
                                                    // Outside delta - check if has permission
                                                    if (OperatorCanLogoutShift(t.ReferenceID))
                                                    {
                                                        // Has CanLogoutShift permission - allow logout
                                                        feedback($"[ShiftManagement] Logout Allowed (CanLogoutShift) | " +
                                                                 $"Machine {machine_idJensen}, Sub {subid}, Operator {op.NameAndID}");
                                                        canLogoutDueToShift = true;
                                                    }
                                                    else
                                                    {
                                                        // No permission and outside delta - deny logout
                                                        canLogin = false;
                                                        canLogoutDueToShift = false;
                                                        feedback($"[ShiftManagement] Logout Denied - Outside Delta Period | " +
                                                                 $"Shift: {operatorShift.ShiftName}, Machine {machine_idJensen}, Sub {subid}, Operator {op.NameAndID}");
                                                        tcpReader.OrangeLED();
                                                    }
                                                }
                                                // else: within delta, allow logout
                                            }
                                            else
                                            {
                                                // Shift not found - clear orphaned shift reference and allow logout
                                                feedback($"[Logout] Shift ID {opState.ActiveShiftId.Value} not found - clearing orphaned reference");
                                                opState.ActiveShiftId = null;
                                            }
                                        }

                                        // Execute logout if allowed
                                        if (canLogoutDueToShift)
                                        {
                                            // Check regular operator permissions
                                            if (!UseOperatorPermissions || op.IsAllowedToManuallyLogout)
                                            {
                                                opState.Logout(DateTime.Now);
                                                canLogin = false;
                                                tcpReader.RedLED();
                                                tcpReader.IsActiveStation = false;
                                            }
                                            else
                                            {
                                                canLogin = false;
                                                feedback($"Operator Permission | Logout Disallowed | " +
                                                         $"Machine {machine_idJensen}, Sub {subid}, Operator {op.NameAndID}");
                                                tcpReader.OrangeLED();
                                            }
                                        }

                                        // ============ END SCENARIO B ============
                                }
                                else if (sameMachine)
                                {
                                    // ============ SCENARIO C: OPERATOR SCANS DIFFERENT STATION (SAME MACHINE) ============
                                    // ShiftManagement SCENARIO C
                                    feedback($"[ShiftManagement] Operator {op.IdJensen} at Machine {opState.MachineID}, Sub {opState.SubID} - scanning different sub {subid}");

                                    // PREREQUISITE CHECK per spec
                                    if (!UseShiftManagement || !machineHasActiveShift)
                                    {
                                        // ShiftManagement SCENARIO C
                                        feedback($"[ShiftManagement] Shift management disabled or no active shift - using legacy logic (out of scope)");
                                        // Legacy logic would allow station movement - perform remote logout
                                        opState.Logout(DateTime.Now);
                                        foreach (RfIdeasTcpReader reader in TcpReaders)
                                        {
                                            if (reader.MachineID == opState.MachineID && reader.SubID == opState.SubID)
                                            {
                                                reader.RedLED();
                                                reader.IsActiveStation = false;
                                            }
                                        }
                                        canLogin = true;
                                    }
                                    else
                                    {
                                        // STEP 1: Can login at target station?
                                        string denyReason;
                                        List<OperatorLoginState> operatorsToReplace;
                                        bool canLoginAtTarget = CanLoginAtTargetStation(
                                            t.ReferenceID, machine_idJensen, subid, activeShift, tcpReader,
                                            out denyReason, out operatorsToReplace, isInitialLogin: false);

                                        if (!canLoginAtTarget)
                                        {
                                            canLogin = false;
                                            // ShiftManagement SCENARIO C STEP 1 FAILED
                                            feedback($"[ShiftManagement] Cannot login at target: {denyReason}");
                                            tcpReader.OrangeLED();
                                        }
                                        else
                                        {
                                            // STEP 2: Can leave current station?
                                            bool targetHasOperators = (operatorsToReplace != null && operatorsToReplace.Count > 0);
                                            string leaveReason;
                                            bool canLeave = CanLeaveCurrentStation(opState, t.ReferenceID, targetHasOperators, out leaveReason);

                                            if (!canLeave)
                                            {
                                                canLogin = false;
                                                // ShiftManagement SCENARIO C STEP 2 FAILED
                                                feedback($"[ShiftManagement] Cannot leave current station: {leaveReason}");
                                                tcpReader.OrangeLED();
                                            }
                                            else
                                            {
                                                // Both steps passed - execute station movement
                                                // ShiftManagement SCENARIO C
                                                feedback($"[ShiftManagement] Both steps passed - executing station movement");

                                                // Replace operators at target if needed
                                                if (targetHasOperators)
                                                {
                                                    if (OperatorCanReplace(t.ReferenceID) && operatorsToReplace.Count > 1)
                                                    {
                                                        // ShiftManagement SCENARIO C
                                                        feedback($"[ShiftManagement] Mass replacing {operatorsToReplace.Count} operators at target station (CANREPLACEOPERATOR)");
                                                    }

                                                    foreach (var opToReplace in operatorsToReplace)
                                                    {
                                                        opToReplace.Logout(DateTime.Now);
                                                    }
                                                    tcpReader.RedLED(); // Briefly
                                                }

                                                // Logout from current station
                                                int oldMachineId = opState.MachineID;
                                                int oldSubId = opState.SubID;
                                                opState.Logout(DateTime.Now);

                                                // Update old station LED
                                                foreach (RfIdeasTcpReader reader in TcpReaders)
                                                {
                                                    if (reader.MachineID == oldMachineId && reader.SubID == oldSubId)
                                                    {
                                                        reader.RedLED();
                                                        reader.IsActiveStation = false;
                                                    }
                                                }

                                                // Login at new station (retain same shift since same machine)
                                                opState.Login(machine_idJensen, subid, DateTime.Now, activeShift);
                                                tcpReader.GreenLED();
                                                tcpReader.IsActiveStation = true;

                                                // ShiftManagement SCENARIO C
                                                feedback($"[ShiftManagement] Station movement complete: Machine {machine_idJensen}, Sub {oldSubId} -> Sub {subid}");
                                                canLogin = false; // Already logged in, don't fall through to login logic
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    // ============ SCENARIO D: OPERATOR SCANS DIFFERENT MACHINE ============
                                    // ShiftManagement SCENARIO D
                                    feedback($"[ShiftManagement] Operator {op.IdJensen} at Machine {opState.MachineID}, Sub {opState.SubID} - scanning Machine {machine_idJensen}, Sub {subid}");

                                    // PREREQUISITE CHECK per spec
                                    if (!UseShiftManagement || !machineHasActiveShift)
                                    {
                                        // ShiftManagement SCENARIO D
                                        feedback($"[ShiftManagement] Shift management disabled or no active shift - using legacy logic (out of scope)");
                                        // Legacy logic would allow remote logout - perform it
                                        opState.Logout(DateTime.Now);
                                        foreach (RfIdeasTcpReader reader in TcpReaders)
                                        {
                                            if (reader.MachineID == opState.MachineID && reader.SubID == opState.SubID)
                                            {
                                                reader.RedLED();
                                                reader.IsActiveStation = false;
                                            }
                                        }
                                        canLogin = true;
                                    }
                                    else
                                    {
                                        // STEP 1: Can login at target station?
                                        string denyReason;
                                        List<OperatorLoginState> operatorsToReplace;
                                        bool canLoginAtTarget = CanLoginAtTargetStation(
                                            t.ReferenceID, machine_idJensen, subid, activeShift, tcpReader,
                                            out denyReason, out operatorsToReplace, isInitialLogin: false);

                                        if (!canLoginAtTarget)
                                        {
                                            canLogin = false;
                                            // ShiftManagement SCENARIO D - STEP 1 FAILED
                                            feedback($"[ShiftManagement] Cannot login at target: {denyReason}");
                                            tcpReader.OrangeLED();
                                        }
                                        else
                                        {
                                            // STEP 2: Can leave current machine?
                                            bool targetHasOperators = (operatorsToReplace != null && operatorsToReplace.Count > 0);
                                            string leaveReason;
                                            bool canLeave = CanLeaveCurrentStation(opState, t.ReferenceID, targetHasOperators, out leaveReason);

                                            if (!canLeave)
                                            {
                                                canLogin = false;
                                                // ShiftManagement SCENARIO D - STEP 2 FAILED
                                                feedback($"[ShiftManagement] Cannot leave current machine: {leaveReason}");
                                                tcpReader.OrangeLED();
                                            }
                                            else
                                            {
                                                // Both steps passed - execute remote logout
                                                // ShiftManagement SCENARIO D
                                                feedback($"[ShiftManagement] Both steps passed - executing remote logout");

                                                // Replace operators at target if needed
                                                if (targetHasOperators)
                                                {
                                                    if (OperatorCanReplace(t.ReferenceID) && operatorsToReplace.Count > 1)
                                                    {
                                                        // ShiftManagement SCENARIO D
                                                        feedback($"[ShiftManagement] Mass replacing {operatorsToReplace.Count} operators at target machine (CANREPLACEOPERATOR)");
                                                    }

                                                    foreach (var opToReplace in operatorsToReplace)
                                                    {
                                                        opToReplace.Logout(DateTime.Now);
                                                    }
                                                    tcpReader.RedLED(); // Briefly
                                                }

                                                // Logout from old machine (clears shift info per spec)
                                                int oldMachineId = opState.MachineID;
                                                int oldSubId = opState.SubID;
                                                opState.Logout(DateTime.Now);

                                                // Update old machine LED
                                                foreach (RfIdeasTcpReader reader in TcpReaders)
                                                {
                                                    if (reader.MachineID == oldMachineId && reader.SubID == oldSubId)
                                                    {
                                                        reader.RedLED();
                                                        reader.IsActiveStation = false;
                                                    }
                                                }

                                                // Login at new machine (gets new machine's shift per spec)
                                                opState.Login(machine_idJensen, subid, DateTime.Now, activeShift);
                                                tcpReader.GreenLED();
                                                tcpReader.IsActiveStation = true;

                                                // ShiftManagement SCENARIO D
                                                feedback($"[ShiftManagement] Remote logout complete: Machine {oldMachineId} -> Machine {machine_idJensen}");
                                                if (activeShift != null)
                                                {
                                                    // ShiftManagement SCENARIO D
                                                    feedback($"[ShiftManagement] Operator assigned to new shift {activeShift.ShiftName} (ID={activeShift.ShiftID})");
                                                }
                                                canLogin = false; // Already logged in, don't fall through to login logic
                                            }
                                        }
                                    }
                                }

                                // Refresh LED state for this station in case login was denied but others are logged in
                                if (!canLogin)
                                {
                                    // Check if there are still operators logged on to this station - make the light green again
                                    if (OperatorStates.TotalLoggedInByMachineSubID(machine_idJensen, subid) > 0)
                                    {
                                        tcpReader.GreenLED();
                                        tcpReader.IsActiveStation = true;
                                    }
                                }
                                EvalCurrentLogins();
                            }

                        }
                        else
                        {
                            //operator is retired  
                            debugInfo("Retired | Bad Read | " + machine_idJensen.ToString() + ", " + subid.ToString() + ", " + op.NameAndID + Environment.NewLine);
                            tcpReader.OrangeLED();
                            tcpReader.HasBadRead = true;
                            tcpReader.BadReadTime = DateTime.Now;
                        }
                    }
                    else
                    {
                        //invalid tag
                        debugInfo("Invalid | Bad Read | " + machine_idJensen.ToString() + ", " + subid.ToString());
                        tcpReader.OrangeLED();
                        tcpReader.HasBadRead = true;
                        tcpReader.BadReadTime = DateTime.Now;
                    }
                }
                else
                {
                    if (tcpReader.HasBadRead)
                    {
                        TimeSpan ts = DateTime.Now.Subtract(tcpReader.BadReadTime);
                        if (ts.TotalMilliseconds > 1000)
                        {
                            feedback("Bad Read");
                            OperatorLoginState opState = (OperatorLoginState)OperatorStates.GetLoggedInByMachineSubID(tcpReader.MachineID, tcpReader.SubID);
                            Boolean isloggedin = false;
                            if (opState != null)
                                isloggedin = opState.IsLoggedIn;
                            if (isloggedin)
                                tcpReader.GreenLED();
                            else
                                tcpReader.RedLED();
                            tcpReader.HasBadRead = false;
                            tcpReader.IsActiveStation = isloggedin;
                        }
                    }
                }
                // ensure the card reader is in the correct state
                OperatorLoginState refreshOpState = (OperatorLoginState)OperatorStates.GetLoggedInByMachineSubID(tcpReader.MachineID, tcpReader.SubID);

                if (refreshOpState != null)
                {
                    //Debug.WriteLine("update all leds now " + tcpReader.MachineID + ", " + tcpReader.SubID + " Operator " + opState.OperatorID + " loggedin " + opState.IsLoggedIn);
                    if (refreshOpState.IsLoggedIn)
                    {
                        tcpReader.GreenLED();
                        tcpReader.IsActiveStation = true;
                    }
                    else
                    {
                        tcpReader.RedLED();
                        tcpReader.IsActiveStation = false;
                    }
                }
                else
                {
                    tcpReader.RedLED();
                    tcpReader.IsActiveStation = false;
                }

                string fb;
                while (tcpReader.IsFeedBack)
                {
                    fb = tcpReader.ReadFeedback();
                    if (fb != string.Empty)
                        feedback(fb + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                tcpReader.OrangeLED();
                debugInfo("Error in read cycle: " + ex.Message + Environment.NewLine);
            }
        }

        public void Stop()
        {
            foreach (RfIdeasTcpReader tcpReader in TcpReaders)
            {
                tcpReader.Disconnect();
                tcpReader.CurrentLED = "GREY";
            }
            // Clear shared memory to signal service stopped (invalidates timestamp)
            try
            {
                sharedStatus?.Clear();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error clearing shared status: {ex.Message}");
            }
            sharedStatus?.Dispose();
        }

        public void clearExcessData()
        {
            try
            {
                logData.Clear();
                tagReaderLog.Clear();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(this.GetType().Name + " " + MethodInfo.GetCurrentMethod() + ex.Message);
            }
        }

        public string GetInfo(string key)
        {
            String aString = string.Empty;
            return aString;
        }
    }

    public enum ServiceState
    {
        Stopped,
        Running,
        Paused
    }


    public class RfIdeasTagReaders : List<RfIdeasTcpReader>, INotifyCollectionChanged
    {
        private ReaderWriterLockSlim _lock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        #region INotifyCollectionChanged Members

        public event NotifyCollectionChangedEventHandler CollectionChanged;

        public void Reset()
        {
            if (CollectionChanged != null)
            {
                _lock.EnterWriteLock();
                try
                {
                    CollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
        }

        new public void Add(RfIdeasTcpReader rec)
        {
            _lock.EnterWriteLock();
            try
            {
                base.Add(rec);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
            if (CollectionChanged != null)
            {
                CollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, rec));
            }
        }

        new public void Remove(RfIdeasTcpReader rec)
        {
            _lock.EnterWriteLock();
            try
            {
                base.RemoveAt(this.IndexOf(rec));
            }
            finally
            {
                _lock.ExitWriteLock();
            }            
            if (CollectionChanged != null)
            {
                CollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, rec));
            }
        }

        #endregion
    }

    public class RfIdeasTcpReader : INotifyPropertyChanged
    {
        private ReaderWriterLockSlim _lock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        private StringQueue CommandQ;
        private StringQueue CardReadQ;
        private StringQueue FeedbackQ;

        public Action<RfIdeasTcpReader> OnStatusChanged { get; set; }

        private bool _updateLEDNow = true;
        public bool UpdateLEDNow
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return _updateLEDNow;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            set
            {
                _lock.EnterWriteLock();
                try
                {
                    _updateLEDNow = value;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
        }

        private bool allowFeedback = false;

        public bool AllowFeedback
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return allowFeedback;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            set
            {
                _lock.EnterWriteLock();
                try
                {
                    allowFeedback = value;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
        }
        private bool _isActiveStation = false;
        public bool IsActiveStation
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return _isActiveStation;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            set
            {
                _lock.EnterWriteLock();
                try
                {
                    _isActiveStation = value;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
        }
        private DateTime badReadTime = DateTime.MinValue;

        public DateTime BadReadTime
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return badReadTime;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            set
            {
                _lock.EnterWriteLock();
                try
                {
                    badReadTime = value;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
        }
        private bool hasBadRead = false;

        public bool HasBadRead
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return hasBadRead;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            set
            {
                _lock.EnterWriteLock();
                try
                {
                    hasBadRead = value;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
        }

        private Task commsTask;
        private Task ledMonitorTask;
        private CancellationTokenSource commsCts;

        // Card deduplication fields
        private string lastQueuedCard = string.Empty;
        private DateTime lastQueuedTime = DateTime.MinValue;
        private const int CARD_DEDUPE_MS = 2000;  // 2 second dedupe window

        // Last scanned card for display purposes
        private string _lastCardHex = string.Empty;
        public string LastCardHex
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return _lastCardHex;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            set
            {
                _lock.EnterWriteLock();
                try
                {
                    _lastCardHex = value;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
        }

        // Last poll time for smart polling
        private DateTime _lastPollTime = DateTime.MinValue;
        public DateTime LastPollTime
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return _lastPollTime;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            set
            {
                _lock.EnterWriteLock();
                try
                {
                    _lastPollTime = value;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
        }

        private bool isGo = true;

        public bool IsGo
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return isGo;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            set
            {
                _lock.EnterWriteLock();
                try
                {
                    isGo = value;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
        }
        private bool _hasChanged;
        public bool HasChanged
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return _hasChanged;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            set
            {
                _lock.EnterWriteLock();
                try
                {
                    _hasChanged = value;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
        }
        private int _locationId;
        public int LocationID
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return _locationId;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            set
            {
                _lock.EnterWriteLock();
                try
                {
                    _locationId = value;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
        }
        public event PropertyChangedEventHandler PropertyChanged;

        public void NotifyPropertyChanged(String info)
        {
            HasChanged = true;
            if (PropertyChanged != null)
            {
                _lock.EnterWriteLock();
                try
                {
                    PropertyChanged(this, new PropertyChangedEventArgs(info));
                    PropertyChanged(this, new PropertyChangedEventArgs("HasChanged"));
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
        }

        private string ipAddress;

        public string IpAddress
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return ipAddress;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            set
            {
                _lock.EnterWriteLock();
                try
                {
                    ipAddress = value;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
        }

        public string Description
        {
            get { return ipAddress + ", " + port.ToString(); }
        }

        private int maxLogins;

        public int MaxLogins
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return maxLogins;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            private set
            {
                _lock.EnterWriteLock();
                try
                {
                    maxLogins = value;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
        }

        private int port;

        public int Port
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return port;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            set
            {
                _lock.EnterWriteLock();
                try
                {
                    port = value;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
        }

        private int machineID;

        public int MachineID
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return machineID;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            set
            {
                _lock.EnterWriteLock();
                try
                {
                    machineID = value;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
        }
        private int subID;

        public int SubID
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return subID;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            set
            {
                _lock.EnterWriteLock();
                try
                {
                    subID = value;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
        }
        private string cardID;

        public string CardID
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return cardID;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            set
            {
                if (CardID != value)
                {
                    _lock.EnterWriteLock();
                    try
                    {
                        cardID = value;
                        NotifyPropertyChanged("CardID");
                    }
                    finally
                    {
                        _lock.ExitWriteLock();
                    }
                }
            }

        }

        private string status;

        public string Status
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return status;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            set
            {
                if (Status != value)
                {
                    _lock.EnterWriteLock();
                    try
                    {
                        status = value;
                        NotifyPropertyChanged("Status");
                    }
                    finally
                    {
                        _lock.ExitWriteLock();
                    }
                }
            }
        }
        private string _currentLED;

        public string CurrentLED
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return _currentLED;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            set
            {
                if (Status != value)
                {
                    _lock.EnterWriteLock();
                    try
                    {
                        _currentLED = value;
                        NotifyPropertyChanged("CurrentLED");
                    }
                    finally
                    {
                        _lock.ExitWriteLock();
                    }
                }
            }
        }
        private bool isConnected = false;

        public bool IsConnected
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return isConnected;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            set
            {
                if (IsConnected != value)
                {
                    _lock.EnterWriteLock();
                    try
                    {
                        isConnected = value;
                        NotifyPropertyChanged("IsConnected");
                        OnStatusChanged?.Invoke(this);
                    }
                    finally
                    {
                        _lock.ExitWriteLock();
                    }
                }
            }
        }

        public RfIdeasTcpReader(int locationid, string ipaddress, int _port, int machineid, int sub, int maxlogins, bool allowfeedback)
        {
            LocationID = locationid;
            IpAddress = ipaddress;
            Port = _port;
            MachineID = machineid;
            SubID = sub;
            MaxLogins = maxlogins;
            AllowFeedback = allowfeedback;
            CommandQ = new StringQueue();
            CardReadQ = new StringQueue();
            FeedbackQ = new StringQueue();
        }

        public bool IsFeedBack
        {
            get { return (FeedbackQ.Count > 0); }
        }

        public string ReadFeedback()
        {
            string feedback = string.Empty;
            string fb = string.Empty;
            bool gotFeedback = FeedbackQ.TryDequeue(out fb);
            if (gotFeedback)
            {
                if (fb.Trim() != string.Empty)
                {
                    feedback += ipAddress + " " + fb;
                }
            }
            return feedback;
        }

        public bool StartMonitorTask(bool loggedin)
        {
            IsGo = true;
            IsActiveStation = loggedin;

            if (allowFeedback) FeedbackQ.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] [{ipAddress}:{port}] Starting persistent connection and LED monitor");

            // Start persistent connection task
            commsTask = Task.Factory.StartNew(() => PersistentCommsLoop());

            // Start LED status monitor task
            ledMonitorTask = Task.Factory.StartNew(() => LEDStatusMonitor());

            return true;
        }

        private void PersistentCommsLoop()
        {
            try
            {
                if (allowFeedback) FeedbackQ.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] [{ipAddress}:{port}] PersistentCommsLoop started");

                // Create one token source for the entire connection lifecycle
                commsCts = new CancellationTokenSource();

                while (IsGo)
                {
                    try
                    {
                        if (allowFeedback) FeedbackQ.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] [{ipAddress}:{port}] Attempting connection");

                        // commsProcess will set IsConnected=true on success, false on failure
                        commsProcess(commsCts.Token);

                        // If commsProcess exits normally (not from exception), brief pause before retry
                        if (IsGo)
                        {
                            if (allowFeedback) FeedbackQ.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] [{ipAddress}:{port}] Connection ended, reconnecting in 2 seconds");
                            Thread.Sleep(2000);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Connection failed or error occurred
                        IsConnected = false;
                        if (allowFeedback) FeedbackQ.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] [{ipAddress}:{port}] Connection error: {ex.Message}, retrying in 2 seconds");
                        Thread.Sleep(2000);
                    }
                }

                if (allowFeedback) FeedbackQ.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] [{ipAddress}:{port}] PersistentCommsLoop ended");
            }
            catch (Exception ex)
            {
                if (allowFeedback) FeedbackQ.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] [{ipAddress}:{port}] PersistentCommsLoop fatal error: {ex.Message}");
            }
            finally
            {
                if (commsCts != null)
                {
                    commsCts.Dispose();
                    commsCts = null;
                }
            }
        }

        private void LEDStatusMonitor()
        {
            try
            {
                if (allowFeedback) FeedbackQ.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] [{ipAddress}:{port}] LED monitor started");

                string lastLEDState = "";

                while (IsGo)
                {
                    try
                    {
                        string currentLEDState;

                        // Determine desired LED color based on connection and station status
                        if (IsConnected)
                        {
                            if (IsActiveStation)
                            {
                                currentLEDState = "GREEN (connected, active)";
                            }
                            else
                            {
                                currentLEDState = "RED (connected, inactive)";
                            }
                        }
                        else
                        {
                            currentLEDState = "RED (disconnected)";
                        }

                        // Only send LED command when state changes
                        if (currentLEDState != lastLEDState)
                        {
                            // Send appropriate LED command
                            if (currentLEDState.StartsWith("GREEN"))
                            {
                                GreenLED();
                            }
                            else if (currentLEDState.StartsWith("RED"))
                            {
                                RedLED();
                            }

                            if (allowFeedback) FeedbackQ.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] [{ipAddress}:{port}] LED state: {currentLEDState}");
                            lastLEDState = currentLEDState;
                        }

                        // Check every second
                        Thread.Sleep(1000);
                    }
                    catch (Exception ex)
                    {
                        if (allowFeedback) FeedbackQ.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] [{ipAddress}:{port}] LED monitor error: {ex.Message}");
                        Thread.Sleep(1000);
                    }
                }

                if (allowFeedback) FeedbackQ.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] [{ipAddress}:{port}] LED monitor ended");
            }
            catch (Exception ex)
            {
                if (allowFeedback) FeedbackQ.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] [{ipAddress}:{port}] LED monitor fatal error: {ex.Message}");
            }
        }

        // OLD MonitorCommsProcess removed - replaced by PersistentCommsLoop and LEDStatusMonitor

        public void Disconnect()
        {
            try
            {
                IsConnected = false;
                IsGo = false;  // This will stop both PersistentCommsLoop and LEDStatusMonitor

                // Cancel the comms token to stop any active TCP operations
                if (commsCts != null && !commsCts.IsCancellationRequested)
                {
                    commsCts.Cancel();
                }

                // Wait for tasks to finish gracefully
                Thread.Sleep(500);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        public string ReadCardID(out int machineid, out int subid)
        {
            string card = string.Empty;
            machineid = MachineID;
            subid = SubID;
            if (IsConnected)
            {
                card = ReadCardID();
            }
            return card;
        }
        private string ReadCardID()
        {
            string card = string.Empty;
            string rawResponse = string.Empty;
            // cardReadQ is a buffer of everything returned from a card reader
            // it contains the initialisation acknowledgments on start up
            // and regular information about the state of the device
            // such as led status

            // Always request card ID for fast polling
            SendCommand("rfid:qid.id");

            // Process any responses in the queue
            if (CardReadQ.Count > 0)
            {
                bool gotCardID = CardReadQ.TryDequeue(out rawResponse);
                if (gotCardID)
                {
                    if (rawResponse != string.Empty)
                    {
                        // Parse the card ID from response
                        int begin = rawResponse.IndexOf("{") + 1;
                        int end = rawResponse.IndexOf("}");
                        if ((begin > 0) && (end > begin))
                        {
                            card = rawResponse.Substring(begin, end - begin);
                            begin = card.IndexOf("0x");
                            while (begin >= 0)
                            {
                                card = card.Substring(begin + 2);
                                begin = card.IndexOf("0x");
                            }
                            while (card.Length < 8)
                                card += "0";
                            if (card == "00000000")
                                card = string.Empty;

                            // LEVEL 2 DEDUPE: Remove ALL other instances of this card from queue
                            if (card != string.Empty)
                            {
                                List<string> remainingItems = new List<string>();
                                string tempResponse;
                                while (CardReadQ.TryDequeue(out tempResponse))
                                {
                                    string parsedTempCard = ExtractCardID(tempResponse);
                                    if (parsedTempCard != card)  // Keep if different card
                                    {
                                        remainingItems.Add(tempResponse);
                                    }
                                    // Discard if same card
                                }

                                // Re-enqueue non-duplicate items
                                foreach (string item in remainingItems)
                                {
                                    CardReadQ.Enqueue(item);
                                }
                            }
                        }
                        else card = string.Empty;
                    }
                }
                else card = string.Empty;
            }
            return card;
        }
        
        public int ReadLUID()
        {
            SendCommand("rfid:dev.luid?");
            return 1;
        }

        public void RedLED()
        {
            SendCommand("rfid:out.led=1");
            CurrentLED = "RED";
            OnStatusChanged?.Invoke(this);
        }

        public void GreenLED()
        {
            SendCommand("rfid:out.led=2");
            CurrentLED = "GREEN";
            OnStatusChanged?.Invoke(this);
        }

        public void OrangeLED()
        {
            SendCommand("rfid:out.led=3");
            CurrentLED = "ORANGE";
            OnStatusChanged?.Invoke(this);
        }

        public void NoLED()
        {
            SendCommand("rfid:out.led=0");
            CurrentLED = "NO LED";
            OnStatusChanged?.Invoke(this);
        }

        public void Beep()
        {
            SendCommand("rfid:beep.now=1");
        }

        private void SendCommand(string cmd)
        {
            CommandQ.Enqueue(cmd);
        }

        private string ExtractCardID(string response)
        {
            // Extract card ID from response string (same logic as ReadCardID parsing)
            try
            {
                if (response == string.Empty) return string.Empty;

                int begin = response.IndexOf("{") + 1;
                int end = response.IndexOf("}");
                if ((begin > 0) && (end > begin))
                {
                    string card = response.Substring(begin, end - begin);
                    begin = card.IndexOf("0x");
                    while (begin >= 0)
                    {
                        card = card.Substring(begin + 2);
                        begin = card.IndexOf("0x");
                    }
                    while (card.Length < 8)
                        card += "0";
                    if (card == "00000000")
                        return string.Empty;
                    return card;
                }
            }
            catch
            {
                // Parsing failed, return empty
            }
            return string.Empty;
        }

        private bool canConnect()
        {
            // Skip ping for localhost - go straight to TCP connect to avoid delays
            if (ipAddress == "127.0.0.1" || ipAddress.ToLower() == "localhost")
            {
                if (allowFeedback) FeedbackQ.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] [{ipAddress}:{port}] Skipping ping for localhost");
                return true;  // Assume localhost is always reachable
            }

            bool test = false;
            try
            {
                if (allowFeedback) FeedbackQ.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] [{ipAddress}:{port}] Ping starting");

                Ping ping = new Ping();
                PingReply reply;
                // Use 500ms timeout instead of default 5000ms to reduce connection delays
                reply = ping.Send(IpAddress, 500);
                test = (reply.Status == IPStatus.Success);

                if (allowFeedback)
                    FeedbackQ.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] [{ipAddress}:{port}] Ping completed - Status: {reply.Status}, RoundtripTime: {reply.RoundtripTime}ms");
            }
            catch (Exception ex)
            {
                if (allowFeedback) FeedbackQ.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] [{ipAddress}:{port}] Exception in CanConnect: {ex.Message}");
                Debug.WriteLine("Exception in CanConnect " + ex.Message);
            }

            if (allowFeedback) FeedbackQ.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] [{ipAddress}:{port}] canConnect result: {test}");
            return test;
        }

        private void commsProcess(CancellationToken token)
        {
            if (allowFeedback) FeedbackQ.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] [{ipAddress}:{port}] commsProcess starting");
            TcpClient tcpClient = null;
            bool error = !canConnect();
            if (allowFeedback) FeedbackQ.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] [{ipAddress}:{port}] canConnect check completed, error={error}");
            int nocardCount = 0;
            try
            {
                if (!error && !token.IsCancellationRequested)
                {
                    tcpClient = new TcpClient();
                    if (allowFeedback) FeedbackQ.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] [{ipAddress}:{port}] Attempting TCP connection");
                    tcpClient.Connect(ipAddress, port);
                    IsConnected = tcpClient.Connected;
                    if (allowFeedback) FeedbackQ.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] [{ipAddress}:{port}] TCP connected: IsConnected={IsConnected}");
                    error = !IsConnected;

                    while (IsGo && !error && !token.IsCancellationRequested)
                    {
                        if (CommandQ.Count > 0)
                        {
                            string cmd;
                            bool gotCommand = CommandQ.TryDequeue(out cmd);
                            if (gotCommand)
                            {
                                string aString = string.Empty;
                                try
                                {
                                    if (tcpClient != null && tcpClient.Connected)
                                    {
                                        String str = cmd + Environment.NewLine;
                                        NetworkStream stm = tcpClient.GetStream();

                                        ASCIIEncoding asen = new ASCIIEncoding();
                                        byte[] ba = asen.GetBytes(str);

                                        stm.Write(ba, 0, ba.Length);

                                        byte[] bb = new byte[1024];  // Increased from 100 to 1024 bytes
                                        int readIterations = 0;
                                        const int MAX_READ_ITERATIONS = 10;

                                        while (stm.DataAvailable && readIterations < MAX_READ_ITERATIONS)
                                        {
                                            int k = stm.Read(bb, 0, bb.Length);
                                            for (int i = 0; i < k; i++)
                                            {
                                                char achar = Convert.ToChar(bb[i]);
                                                if ((achar == '\n') || (achar == '\r'))
                                                    achar = ' ';
                                                aString += achar;
                                            }
                                            aString += Environment.NewLine;
                                            readIterations++;
                                        }

                                        if (stm.DataAvailable)
                                        {
                                            // Still data available after max reads - log with details
                                            if (allowFeedback) FeedbackQ.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] [{ipAddress}:{port}] Read overflow after {readIterations} iterations, data length: {aString.Length} bytes");
                                            Debug.WriteLine($"Read overflow: {ipAddress}:{port}, iterations: {readIterations}, length: {aString.Length}");
                                        }
                                    }
                                    else error = true;
                                }
                                catch (Exception ex)
                                {
                                    error = true;
                                    if (allowFeedback) FeedbackQ.Enqueue("commsProcess " + ex.Message);
                                }

                                if (aString != string.Empty)
                                {
                                    if (!(aString.Contains("{0x0000,0,0x00,0;0x00}") || (aString.Contains("{0xFFFF,0,0x00,0;0x00}"))))
                                    {
                                        // LEVEL 1 DEDUPE: Don't enqueue if same card within time window
                                        string cardFromResponse = ExtractCardID(aString);
                                        TimeSpan timeSinceLastQueued = DateTime.Now - lastQueuedTime;

                                        if (cardFromResponse != lastQueuedCard || timeSinceLastQueued.TotalMilliseconds >= CARD_DEDUPE_MS)
                                        {
                                            CardReadQ.Enqueue(aString);
                                            lastQueuedCard = cardFromResponse;
                                            lastQueuedTime = DateTime.Now;
                                        }
                                        nocardCount = 0;
                                    }
                                    else
                                    {
                                        if ((allowFeedback) && (nocardCount == 0))
                                        {
                                            nocardCount++;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                token.WaitHandle.WaitOne(10);
                            }
                        }
                        token.WaitHandle.WaitOne(10);  // Reduced from 100ms to 10ms for faster command processing
                    }
                }
            }
            catch (Exception ex)
            {
                if (allowFeedback) FeedbackQ.Enqueue("commsProcess " + ex.Message);
            }
            finally
            {
                if (tcpClient != null)
                    tcpClient.Close();
                if (IsConnected)
                {
                    IsConnected = false;
                    if (allowFeedback) FeedbackQ.Enqueue("Disconnected from " + ipAddress + ":" + port);
                }
            }

            // Reconnection delay now handled by PersistentCommsLoop (2 seconds)
        }

    }




    public class OperatorLoginStates : List<OperatorLoginState>, INotifyCollectionChanged
    {
        public delegate void FeedbackDelegate(string feedback);
        public FeedbackDelegate StatesFeedback = null;

        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        #region INotifyCollectionChanged Members

        public event NotifyCollectionChangedEventHandler CollectionChanged;
        public void Feedback(string info)
        {
            if (StatesFeedback != null)
                StatesFeedback(info);
            else
                Debug.WriteLine(info);
        }
        public void Reset()
        {
            if (CollectionChanged != null)
            {
                _lock.EnterWriteLock();
                try
                {
                    CollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
        }

        public new void Add(OperatorLoginState rec)
        {
            _lock.EnterWriteLock();
            try
            {
                base.Add(rec);
                rec.StateFeedback = Feedback;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
            if (CollectionChanged != null)
            {
                CollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, rec));
            }
        }

        public new void Remove(OperatorLoginState rec)
        {
            _lock.EnterWriteLock();
            try
            {
                base.RemoveAt(this.IndexOf(rec));
            }
            finally
            {
                _lock.ExitWriteLock();
            }
            if (CollectionChanged != null)
            {
                CollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, rec));
            }
        }
        #endregion

        public OperatorLoginState GetByOperatorID(int id)
        {
            _lock.EnterReadLock();
            try
            {
                return this.Find(delegate(OperatorLoginState item)
                {
                    return item.OperatorID == id;
                });
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public OperatorLoginState GetLoggedInByMachineSubID(int machineid, int subid)
        {
            _lock.EnterReadLock();
            try
            {
                return this.Find(delegate(OperatorLoginState item)
                {
                    return ((item.MachineID == machineid)
                        && (item.SubID == subid)
                        && (item.IsLoggedIn));
                });
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public OperatorLoginState GetByMachineSubID(int machineid, int subid)
        {
            _lock.EnterReadLock();
            try
            {
                return this.Find(delegate(OperatorLoginState item)
                {
                    return ((item.MachineID == machineid)
                        && (item.SubID == subid));
                });
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public int TotalLoggedInByMachineSubID(int machineid, int subid)
        {
            int count = 0;
            _lock.EnterReadLock();
            try
            {
                foreach (OperatorLoginState rec in this)
                {
                    if ((rec.MachineID == machineid)
                        && (rec.SubID == subid)
                            && (rec.IsLoggedIn))
                    {
                        count++;
                    }
                }
                return count;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Gets ALL logged-in operators at a specific machine/station
        /// Used for mass replacement when CANREPLACEOPERATOR permission is used
        /// </summary>
        public List<OperatorLoginState> GetAllLoggedInByMachineSubID(int machineid, int subid)
        {
            List<OperatorLoginState> operators = new List<OperatorLoginState>();
            _lock.EnterReadLock();
            try
            {
                foreach (OperatorLoginState rec in this)
                {
                    if ((rec.MachineID == machineid)
                        && (rec.SubID == subid)
                        && (rec.IsLoggedIn))
                    {
                        operators.Add(rec);
                    }
                }
                return operators;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    public class OperatorLoginState : ObservableObject
    {
        public delegate void FeedbackDelegate(string feedback);
        public FeedbackDelegate StateFeedback = null;
        public void Feedback(string info)
        {
            if (StateFeedback != null)
                StateFeedback(info);
            else
                Debug.WriteLine(info);
        }
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        private int _logId;
        public int LogId
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return _logId;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            set
            {
                _lock.EnterWriteLock();
                try
                {
                    _logId = value;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
        }
        private int _operatorId;
        public int OperatorID 
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return _operatorId;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
        }
        private int machineID;

        public int MachineID
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return machineID;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            set
            {
                _lock.EnterWriteLock();
                try
                {
                    machineID = AssignNotify(ref machineID, value, "MachineID");
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
        }


        private int subID;

        public int SubID
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return subID;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            set
            {
                _lock.EnterWriteLock();
                try
                {
                    subID = AssignNotify(ref subID, value, "SubID");
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
        }
        private DateTime _loginTime;
        public DateTime LoginTime
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return _loginTime;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            set
            {
                _lock.EnterWriteLock();
                try
                {
                    _loginTime = value;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
        }
        private DateTime _logoutTime;
        public DateTime LogoutTime
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return _logoutTime;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            set
            {
                _lock.EnterWriteLock();
                try
                {
                    _logoutTime = value;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
        }
        private bool isLoggedIn;
        public bool IsLoggedIn
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return isLoggedIn;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            set
            {
                _lock.EnterWriteLock();
                try
                {
                    isLoggedIn = AssignNotify(ref isLoggedIn, value, "IsLoggedIn");
                    //Feedback("Memory: Operator= " + OperatorID + ", Machine= " + MachineID + ", SubId= " + SubID + ", IsLoggedIn=" + value + Environment.NewLine);
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            }
        }
        private bool _clockedIn;
        public bool ClockedIn 
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return _clockedIn;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            set
            {
                _lock.EnterWriteLock();
                try
                {
                    _clockedIn = value;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            } 
        }
        private int _breakId;
        public int BreakID 
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return _breakId;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
            set
            {
                _lock.EnterWriteLock();
                try
                {
                    _breakId = value;
                }
                finally
                {
                    _lock.ExitWriteLock();
                }
            } 
        }
        private int? _activeShiftId;
        public int? ActiveShiftId
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return _activeShiftId;
                }
                finally { _lock.ExitReadLock(); }
            }
            set
            {
                _lock.EnterWriteLock();
                try
                {
                    _activeShiftId = value;
                }
                finally
                { _lock.ExitWriteLock(); }
            }
        }
        // Per spec: DO NOT store LoginShiftStart or LoginShiftEnd
        // Only store ActiveShiftId and retrieve shift details from datalayer when needed

        public bool InBreak(int id)
        {
            return (id == BreakID) && (id > 0);
        }

        private readonly RemoteOperatorLogsGroupedByOperator remoteOperatorLogs;
        private readonly JGLogData logData;
        private readonly MachineSubIDs machineSubIDs;
        private readonly SqlDataAccess da;

        public OperatorLoginState(int opid, int machineid, int subid, DateTime login, DateTime logout,
            bool isloggedin, RemoteOperatorLogsGroupedByOperator remoteoperatorlogs, JGLogData logdata, MachineSubIDs machinesubids)
        {
            _operatorId = opid;
            MachineID = machineid;
            SubID = subid;
            LoginTime = login;
            LogoutTime = logout;
            IsLoggedIn = isloggedin;
            remoteOperatorLogs = remoteoperatorlogs;
            logData = logdata;
            machineSubIDs = machinesubids;
            da = SqlDataAccess.Singleton;
        }


        public void Login(int machineid, int subid, DateTime login, Shift activeShift = null)
        {
            MachineID = machineid;
            SubID = subid;
            LoginTime = login;
            LogoutTime = DateTime.MaxValue;

            // Store ONLY the active shift ID (never store shift times per spec)
            // Shift times will be retrieved from datalayer when needed for delta calculations
            if (activeShift != null)
            {
                ActiveShiftId = activeShift.ShiftID;
            }
            else
            {
                ActiveShiftId = null;
            }

            LogInOut(1);
            IsLoggedIn = true;
        }

        public void ClockIn(DateTime clockintime)
        {
            JGLogDataRec rec = createLogDataRecord(clockintime, 1);
            rec.SubRegType = 5;
            logData.Add(rec);
            logData.UpdateToDB();
            RemoteOperatorLog rol = createOperatorLogRecord(clockintime, 1);
            rol.SubregType = 5;
            remoteOperatorLogs.Add(rol);
            remoteOperatorLogs.UpdateToDB();
        }

        public void BreakEnd(DateTime endtime, int breakid)
        {
            JGLogDataRec rec = createLogDataRecord(endtime, 0);
            rec.SubRegType = 6;
            rec.SubRegTypeID = breakid;
            logData.Add(rec);
            logData.UpdateToDB();
            RemoteOperatorLog rol = createOperatorLogRecord(endtime, 0);
            rol.SubregType = 6;
            remoteOperatorLogs.Add(rol);
            remoteOperatorLogs.UpdateToDB();
        }

        public void Logout(DateTime logout)
        {
            LogoutTime = logout;
            LogInOut(0);
            IsLoggedIn = false;
        }

        private JGLogDataRec createLogDataRecord(DateTime logtime, int state)
        {
            JGLogDataRec ldr = new JGLogDataRec();
            ldr.RecNum = 0;
            ldr.RemoteID = -1;
            ldr.CompanyID = 99;
            ldr.TimeStamp = logtime;
            ldr.MachineID = MachineID;
            ldr.PositionID = -1;
            ldr.SubID = SubID;
            ldr.SubIDName = string.Empty;
            ldr.RegType = 6;
            ldr.SubRegType = 0;
            ldr.SubRegTypeID = 0;
            ldr.State = state;
            ldr.MessageA = "Operator";
            ldr.MessageB = "Operator";
            ldr.BatchID = -1;
            ldr.SourceID = -1;
            ldr.ProcessCode = -1;
            ldr.ProcessName = string.Empty;
            ldr.CustomerID = -1;
            ldr.SortCategoryID = -1;
            ldr.ArticleID = -1;
            ldr.OperatorID = OperatorID;
            ldr.Value = 0;
            ldr.Unit = -1;
            ldr.ForceNew = true;
            return ldr;
        }

        private RemoteOperatorLog createOperatorLogRecord(DateTime logtime, int state)
        {
            RemoteOperatorLog remoteOperatorLog = new RemoteOperatorLog();
            remoteOperatorLog.RemoteOperatorLogID = 0;
            remoteOperatorLog.ForceNew = true;
            remoteOperatorLog.LogTime = logtime;
            remoteOperatorLog.MachineID = MachineID;
            remoteOperatorLog.OperatorID = OperatorID;
            remoteOperatorLog.SubID = SubID;
            remoteOperatorLog.State = state;
            return remoteOperatorLog;
        }
        private void insertOrUpdateMachineSubid(int machineid, int subid, int state)
        {
            MachineSubID machineSubid = (MachineSubID)machineSubIDs.GetByIDSubID(machineID, subID);
            int opRemoteField = analyseOperatorRemoteField(state);
            if (machineSubid == null) //shouldn't happen
            {
                machineSubid = new MachineSubID();
                machineSubid.MachineID = MachineID;
                machineSubid.SubID = SubID;
                machineSubid.ForceNew = true;
                machineSubIDs.Add(machineSubid);
            }

            machineSubid.OperatorRemote = opRemoteField;
            machineSubIDs.UpdateToDB();

            cleanupMachineSubIdTable(machineSubid.MachineID, machineSubid.SubID, opRemoteField);
        }
        private int analyseOperatorRemoteField(int state)
        {
            return (state == 0 ? -1 : OperatorID);
        }
        private bool cleanupMachineSubIdTable(int _machineID, int _subID, int _value)
        {
            bool success = true;
            try
            {
                // this has been replaced by datalayer update
                //da.ExecuteNonQuery("JEGR_DB", string.Format("UPDATE [JEGR_DB].[dbo].[tblMachineSubID] SET [Operator_Remote] = {0} WHERE [Machine_idJensen]={1} AND [SubID]={2}", _value, _machineID, _subID));
                
                // if this is a log in by an operator, this ensures that the operator is not logged into and other machine / subid in tblMachineSubId.
                if(_value != -1)
                    da.ExecuteNonQuery("JEGR_DB", string.Format("UPDATE [JEGR_DB].[dbo].[tblMachineSubID] SET [Operator_Remote] = -1 WHERE [Operator_Remote]={0} AND ([Machine_idJensen]<>{1} OR [SubID]<>{2})", OperatorID, _machineID, _subID));
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                success = false;
            }
            return success;
        }

        private void LogInOut(int state)
        {
            try
            {
                DateTime logtime = LoginTime;
                if (state == 0)
                    logtime = LogoutTime;

                string action = state == 1 ? "LOGIN" : "LOGOUT";
                Feedback($"[LogInOut] {action} - Operator {OperatorID}, Machine {MachineID}, Sub {SubID}, Time {logtime:HH:mm:ss}");

                try
                {
                    logData.Add(createLogDataRecord(logtime, state));
                    logData.UpdateToDB();
                }
                catch (Exception ex)
                {
                    Feedback($"[LogInOut] ERROR writing to JGLogData: {ex.Message}");
                    Debug.WriteLine("Logdata: " + ex.Message);
                }
                try
                {
                    var oldRecord = remoteOperatorLogs.GetByOperatorId(OperatorID);
                    remoteOperatorLogs.Add(createOperatorLogRecord(logtime, state));
                    remoteOperatorLogs.UpdateToDB();
                    if (oldRecord != null)
                        remoteOperatorLogs.Remove(oldRecord);
                    Feedback($"[LogInOut] {action} record written to RemoteOperatorLog for Operator {OperatorID}");
                }
                catch (Exception ex)
                {
                    Feedback($"[LogInOut] ERROR writing to RemoteOperatorLogs: {ex.Message}");
                    Debug.WriteLine("RemoteOperatorLogs: " + ex.Message);
                }
                try
                {
                    insertOrUpdateMachineSubid(machineID, subID, state);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("MachineSubIds: " + ex.Message);
                }
            }
            catch (Exception ex)
            {               
                Debug.WriteLine(ex.Message);
            }
        }


    }

    public class StringQueue : ConcurrentQueue<string>
    { 
    }


}
