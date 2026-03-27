using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace Ge_Mac.DataLayer
{
    public partial class SqlDataAccess
    {
        #region Cached Data
        
        #endregion
    }
    public class Shift : DataItem, IComparable<Machine>, INotifyPropertyChanged
    {
        #region Shift Data Record
        protected class DataRecord : ICopyableObject
        {
            internal int ShiftID;
            internal string ShiftName;
            internal TimeSpan StartTime;
            internal TimeSpan EndTime;
            internal int DeltaMinus;
            internal int DeltaPlus;
            internal bool CrossesMidnight;
            internal DateTime CreatedDate;
            internal DateTime ModifiedDate;

            public ICopyableObject ShallowCopy()
            {
                return (ICopyableObject)MemberwiseClone();
            }
        }
        #endregion
        #region Constructor
        public Shift()
        {
            ActiveData = new DataRecord();
        }
        #endregion
        #region Abstract Member Variable Properties
        private DataRecord activeData
        {
            get
            {
                return (DataRecord)ActiveData;
            }
            set
            {
                ActiveData = value;
            }
        }

        private DataRecord backupData
        {
            get
            {
                return (DataRecord)BackupData;
            }
            set
            {
                BackupData = value;
            }
        }
        #endregion
        #region Record Status Properties

        /// <summary>This is a new record, ie Not yet created in the database</summary>
        public override bool IsNew
        {
            get
            {
                return activeData.ShiftID == -1;
            }
        }

        /// <summary>The record exists in the database</summary>
        public override bool IsExisting
        {
            get
            {
                return activeData.ShiftID != -1;
            }
        }
        #endregion
        #region Data Column Properties
        public int ShiftID
        {
            get
            {
                return activeData.ShiftID;
            }
            set
            {
                if(activeData.ShiftID != value)
                {
                    activeData.ShiftID = value;
                    NotifyPropertyChanged(nameof(ShiftID));
                }
            }
        }
        #endregion

    }
}
