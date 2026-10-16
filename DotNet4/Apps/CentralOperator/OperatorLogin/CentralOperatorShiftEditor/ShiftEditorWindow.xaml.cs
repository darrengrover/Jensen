using Dynamic.DataLayer;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace CentralOperatorShiftEditor
{
    /// <summary>
    /// Helper class for displaying operators in combo box
    /// </summary>
    public class OperatorDisplayItem
    {
        public int RecNum { get; set; }
        public int IdJensen { get; set; }
        public string ShortDescription { get; set; }
        public string DisplayText => $"{IdJensen} - {ShortDescription}";
    }

    /// <summary>
    /// Shows a time of day as HH:mm and reads one back from what the user types: "10:30", "10.30",
    /// "1030" or just "10" (= 10:00). Anything else is rejected, so the cell stays in edit mode with a
    /// red border instead of storing a wrong time.
    /// </summary>
    public class TimeOfDayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return value is TimeSpan time ? time.ToString(@"hh\:mm") : string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return TryParse(value as string, out TimeSpan time) ? (object)time : DependencyProperty.UnsetValue;
        }

        public static bool TryParse(string text, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string digits = text.Trim().Replace('.', ':');
            int hours, minutes = 0;
            int colon = digits.IndexOf(':');
            if (colon >= 0)
            {
                if (!int.TryParse(digits.Substring(0, colon), out hours)) return false;
                if (!int.TryParse(digits.Substring(colon + 1), out minutes)) return false;
            }
            else if (digits.Length <= 2)
            {
                if (!int.TryParse(digits, out hours)) return false;
            }
            else if (digits.Length <= 4)
            {
                // "930" = 09:30, "1030" = 10:30
                if (!int.TryParse(digits.Substring(0, digits.Length - 2), out hours)) return false;
                if (!int.TryParse(digits.Substring(digits.Length - 2), out minutes)) return false;
            }
            else
            {
                return false;
            }

            if (hours < 0 || hours > 23 || minutes < 0 || minutes > 59) return false;
            time = new TimeSpan(hours, minutes, 0);
            return true;
        }
    }

    public partial class ShiftEditorWindow : Window
    {
        #region Fields
        private SqlDataAccess dataAccess;

        // Data collections
        private Shifts shifts;
        private ShiftMachineAssignments assignments;
        private ShiftBreaks breaks;
        private OperatorSettings operatorSettings;
        private List<OperatorDisplayItem> availableOperators;

        // Property for XAML binding
        public List<OperatorDisplayItem> AvailableOperators
        {
            get { return availableOperators; }
            set { availableOperators = value; }
        }

        // Current selections
        private Shift currentlySelectedShift = null;
        private ShiftMachineAssignments currentShiftMachines;
        private ShiftBreaks currentShiftBreaks;

        // Collection views
        private ListCollectionView shiftsView;
        private ListCollectionView shiftMachinesView;
        private ListCollectionView shiftBreaksView;

        // Change tracking flags
        private bool hasUnsavedShifts = false;
        private bool hasUnsavedOperatorSettings = false;
        private bool hasUnsavedShiftMachines = false;
        private bool hasUnsavedBreaks = false;
        #endregion

        #region Initialization
        public ShiftEditorWindow()
        {
            InitializeComponent();
            dataAccess = SqlDataAccess.Singleton;
            DataContext = this; // Set DataContext for binding to AvailableOperators
            LoadData();
        }

        private void LoadData()
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                LoadAvailableOperators();
                LoadShifts();
                LoadAllAssignments(); // Load all assignments into cache
                LoadAllBreaks();
                LoadOperatorSettings();
                UpdateStatusBar();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading data: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        /// <summary>
        /// Loads all shift-machine assignments into the cache for filtering
        /// </summary>
        private void LoadAllAssignments()
        {
            assignments = dataAccess.GetShiftMachineAssignments(null, true);
        }

        /// <summary>
        /// Loads every shift's break times into the cache for filtering
        /// </summary>
        private void LoadAllBreaks()
        {
            breaks = dataAccess.GetShiftBreaks(null, true);
        }
        #endregion

        #region Status Bar & Change Tracking
        private void UpdateStatusBar()
        {
            bool hasAnyChanges = hasUnsavedShifts || hasUnsavedBreaks ||
                                 hasUnsavedOperatorSettings || hasUnsavedShiftMachines;

            if (hasAnyChanges)
            {
                txtUnsavedChanges.Text = "⚠ Unsaved Changes";
                txtStatus.Text = "Modified";

                if (!this.Title.EndsWith("*"))
                    this.Title += " *";
            }
            else
            {
                txtUnsavedChanges.Text = "";
                txtStatus.Text = "Ready";
                this.Title = this.Title.TrimEnd('*', ' ');
            }
        }

        private void MarkShiftsChanged()
        {
            hasUnsavedShifts = true;
            UpdateStatusBar();
        }

        private void MarkBreaksChanged()
        {
            hasUnsavedBreaks = true;
            UpdateStatusBar();
        }

        private void MarkOperatorSettingsChanged()
        {
            hasUnsavedOperatorSettings = true;
            UpdateStatusBar();
        }

        private void MarkShiftMachinesChanged()
        {
            hasUnsavedShiftMachines = true;
            UpdateStatusBar();
        }

        /// <summary>
        /// Shown when UpdateToDB reports the database rejected a change. UpdateToDB stops at the
        /// first failure, so nothing after it was saved either.
        /// </summary>
        private void ShowSaveFailed(string action, string likelyCause)
        {
            MessageBox.Show(
                $"The database rejected a change while {action}. That change, and any after it, were not saved.\n\n" +
                $"Likely cause: {likelyCause}\n\n" +
                "The list has been reloaded from the database - please check it.",
                "Save Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            bool hasAnyChanges = hasUnsavedShifts || hasUnsavedBreaks ||
                                 hasUnsavedOperatorSettings || hasUnsavedShiftMachines;

            if (hasAnyChanges)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Are you sure you want to close without saving?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                }
            }
        }
        #endregion

        #region Shifts Management
        private void LoadShifts()
        {
            int selectedShiftId = currentlySelectedShift == null ? 0 : currentlySelectedShift.ShiftID;
            string selectedShiftName = currentlySelectedShift == null ? null : currentlySelectedShift.ShiftName;

            shifts = dataAccess.GetShifts(null, true);
            shiftsView = CollectionViewSource.GetDefaultView(shifts) as ListCollectionView;
            dgShifts.ItemsSource = shiftsView;

            // Subscribe to collection changes
            if (shifts is INotifyCollectionChanged collectionChanged)
            {
                collectionChanged.CollectionChanged += Shifts_CollectionChanged;
            }

            // Subscribe to property changes for each shift
            foreach (Shift shift in shifts)
            {
                shift.PropertyChanged += Shift_PropertyChanged;
            }

            hasUnsavedShifts = false;

            // The list was replaced: select the same shift again and show its details afresh.
            // By ID, or for a shift that was new until this save, by its name if that is unique.
            Shift reselect = null;
            if (selectedShiftId != 0)
            {
                reselect = shifts.Cast<Shift>().FirstOrDefault(s => s.ShiftID == selectedShiftId);
            }
            else if (!string.IsNullOrEmpty(selectedShiftName))
            {
                var sameName = shifts.Cast<Shift>().Where(s => s.ShiftName == selectedShiftName).ToList();
                if (sameName.Count == 1) reselect = sameName[0];
            }
            revertingShiftSelection = true;
            try { dgShifts.SelectedItem = reselect; }
            finally { revertingShiftSelection = false; }
            ShowShiftDetails(reselect);
        }

        private void Shifts_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (Shift shift in e.NewItems)
                {
                    shift.PropertyChanged += Shift_PropertyChanged;
                }
                MarkShiftsChanged();
            }
        }

        private void Shift_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            MarkShiftsChanged();
        }

        private bool revertingShiftSelection = false;

        private void DgShifts_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (revertingShiftSelection) return;

            Shift selectedShift = dgShifts.SelectedItem as Shift;
            if (ReferenceEquals(selectedShift, currentlySelectedShift)) return;

            // Unsaved break times belong to the shift being left - ask before dropping them
            if (hasUnsavedBreaks && currentlySelectedShift != null && shifts.Contains(currentlySelectedShift))
            {
                var result = MessageBox.Show(
                    $"The break times of shift '{currentlySelectedShift.ShiftName}' have unsaved changes.\n\n" +
                    "Discard them and show the other shift?",
                    "Unsaved Break Times",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    revertingShiftSelection = true;
                    try { dgShifts.SelectedItem = currentlySelectedShift; }
                    finally { revertingShiftSelection = false; }
                    return;
                }

                LoadAllBreaks(); // drop the edits: they were made on the cached break records
            }

            ShowShiftDetails(selectedShift);
        }

        /// <summary>
        /// Shows the machine assignments and break times of a shift in the lower panel (null clears it).
        /// </summary>
        private void ShowShiftDetails(Shift shift)
        {
            currentlySelectedShift = shift;
            if (shift != null)
            {
                LoadMachinesForShift(shift);
            }
            else
            {
                dgShiftMachines.ItemsSource = null;
                txtShiftMachineCount.Text = "0 assignments";
            }
            LoadBreaksForShift(shift);
            UpdateStatusBar();
        }

        /// <summary>
        /// Saving, refreshing or deleting shifts reloads the shift list and with it the Breaks tab.
        /// </summary>
        private bool BreaksAreSaved(string action)
        {
            if (!hasUnsavedBreaks) return true;

            MessageBox.Show($"Save or refresh the break times before {action}.", "Unsaved Break Times",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        private void BtnAddShift_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Shift newShift = new Shift
                {
                    ShiftName = "New Shift",
                    StartTime = new TimeSpan(8, 0, 0),
                    EndTime = new TimeSpan(16, 0, 0),
                    IsActive = true,
                    CreatedDate = DateTime.Now,
                    ModifiedDate = DateTime.Now,
                    ForceNew = true
                };
                shifts.Add(newShift);
                shiftsView.Refresh();
                MarkShiftsChanged();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adding shift: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnDeleteShift_Click(object sender, RoutedEventArgs e)
        {
            if (dgShifts.SelectedItems.Count == 0) return;
            if (!BreaksAreSaved("deleting shifts")) return;

            var itemsToDelete = dgShifts.SelectedItems.Cast<Shift>().ToList();

            // The database won't delete a shift that still has machine assignments (foreign key),
            // so they are deleted with it. Its break times are deleted by the database (cascade).
            var shiftIds = itemsToDelete.Where(s => s.ShiftID != 0).Select(s => s.ShiftID).ToList();
            int linkedBreaks = breaks == null ? 0 : breaks.Cast<ShiftBreak>().Count(b => shiftIds.Contains(b.ShiftID));
            var linkedAssignments = assignments == null
                ? new List<ShiftMachineAssignment>()
                : assignments.Cast<ShiftMachineAssignment>()
                    .Where(a => a.AssignmentID != 0 && shiftIds.Contains(a.ShiftID)).ToList();

            if (linkedAssignments.Any() && hasUnsavedShiftMachines)
            {
                MessageBox.Show("Save or refresh the machine assignments before deleting shifts.", "Unsaved Changes",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var linked = new List<string>();
            if (linkedAssignments.Any()) linked.Add($"{linkedAssignments.Count} machine assignment(s)");
            if (linkedBreaks > 0) linked.Add($"{linkedBreaks} break time(s)");
            string prompt = linked.Any()
                ? $"Delete selected shift(s) and their {string.Join(" and ", linked)}?"
                : "Delete selected shift(s)?";
            var result = MessageBox.Show(prompt, "Confirm Delete",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;

                    bool saved = true;
                    if (linkedAssignments.Any())
                    {
                        var assignmentsToDelete = new ShiftMachineAssignments();
                        foreach (var assignment in linkedAssignments)
                        {
                            assignment.DeleteRecord = true;
                            assignmentsToDelete.Add(assignment);
                        }
                        saved = assignmentsToDelete.UpdateToDB();
                    }

                    if (saved)
                    {
                        foreach (var shift in itemsToDelete)
                        {
                            shift.DeleteRecord = true;
                        }

                        dgShifts.SelectedItems.Clear();
                        saved = shifts.UpdateToDB();
                    }

                    LoadAllAssignments();
                    LoadAllBreaks();
                    LoadShifts();

                    hasUnsavedShifts = false;
                    UpdateStatusBar();

                    if (!saved)
                    {
                        Mouse.OverrideCursor = null;
                        ShowSaveFailed("deleting shift(s)", "the database connection was lost, or another change was made at the same time.");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error deleting shifts: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }
        }

        private void BtnSaveShifts_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                dgShifts.CommitEdit(DataGridEditingUnit.Row, true);
                dgShifts.CommitEdit(DataGridEditingUnit.Cell, true);

                if (!BreaksAreSaved("saving shifts")) return;

                var timeProblems = CheckShiftTimes();
                if (timeProblems.Any())
                {
                    MessageBox.Show(
                        "Cannot save shifts:\n\n" + string.Join("\n\n", timeProblems),
                        "Shift Times",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                // The database only checks overlaps when assignments change, not when shift times are edited
                var conflicts = CheckForOverlappingShiftTimes();
                if (conflicts.Any())
                {
                    MessageBox.Show(
                        "Cannot save shifts: the new times would overlap another shift on the same machine and day:\n\n" +
                        string.Join("\n\n", conflicts) + "\n\n" +
                        "Please correct the times or remove the conflicting machine assignments first.",
                        "Overlap Conflicts Detected",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                Mouse.OverrideCursor = Cursors.Wait;
                bool saved = shifts.UpdateToDB();

                LoadShifts();
                hasUnsavedShifts = false;
                UpdateStatusBar();
                Mouse.OverrideCursor = null;

                if (saved)
                    MessageBox.Show("Shifts saved successfully", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                else
                    ShowSaveFailed("saving shifts", "a shift name is blank.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving shifts: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void BtnRefreshShifts_Click(object sender, RoutedEventArgs e)
        {
            if (!BreaksAreSaved("refreshing shifts")) return;

            if (hasUnsavedShifts)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Refreshing will lose these changes. Continue?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No) return;
            }

            LoadShifts();
            hasUnsavedShifts = false;
            UpdateStatusBar();
        }

        private static bool IsTimeOfDay(TimeSpan time)
        {
            return time >= TimeSpan.Zero && time < TimeSpan.FromDays(1);
        }

        /// <summary>
        /// True if the time falls strictly between the shift's start and end (handles shifts that cross midnight)
        /// </summary>
        private static bool IsInsideShift(Shift shift, TimeSpan time)
        {
            if (shift.StartTime < shift.EndTime)
                return time > shift.StartTime && time < shift.EndTime;
            if (shift.StartTime > shift.EndTime)
                return time > shift.StartTime || time < shift.EndTime;
            return false; // start = end: the shift never runs
        }

        /// <summary>
        /// Checks the shift times about to be saved: each must be a time of day (a time typed without
        /// a colon, such as "9", is read as a number of days), and the shift's break times must still
        /// fall inside it.
        /// </summary>
        private List<string> CheckShiftTimes()
        {
            var problems = new List<string>();

            foreach (Shift shift in shifts.Cast<Shift>().Where(s => !s.DeleteRecord))
            {
                if (!IsTimeOfDay(shift.StartTime) || !IsTimeOfDay(shift.EndTime))
                {
                    problems.Add($"'{shift.ShiftName}': the start or end time is not a time of day. " +
                                 "Enter times as HH:mm, for example 09:00.");
                    continue;
                }

                // Only shifts edited here - don't hold up a save over a break someone put outside a shift by SQL
                if (breaks == null || shift.ShiftID == 0 || !shift.HasChanged) continue;

                var outside = breaks.Cast<ShiftBreak>()
                    .Where(b => b.ShiftID == shift.ShiftID && !IsInsideShift(shift, b.BreakTime))
                    .Select(b => b.BreakTime.ToString(@"hh\:mm"))
                    .ToList();
                if (outside.Any())
                {
                    problems.Add($"'{shift.ShiftName}' ({shift.StartTime:hh\\:mm}-{shift.EndTime:hh\\:mm}) would leave its break at " +
                                 $"{string.Join(", ", outside)} outside the shift. Change or remove the break first (Breaks tab), or correct the times.");
                }
            }

            return problems;
        }
        #endregion

        #region Machine Assignments for Selected Shift
        /// <summary>
        /// Loads machine assignments for the currently selected shift by filtering the cached assignments
        /// </summary>
        private void LoadMachinesForShift(Shift shift)
        {
            if (shift == null || shift.ShiftID == 0)
            {
                dgShiftMachines.ItemsSource = null;
                txtShiftMachineCount.Text = "Save shift first to add machines";
                return;
            }

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;

                // Filter from cached assignments instead of querying database
                currentShiftMachines = new ShiftMachineAssignments();

                if (assignments != null)
                {
                    foreach (ShiftMachineAssignment assignment in assignments)
                    {
                        if (assignment.ShiftID == shift.ShiftID)
                        {
                            // Ensure lookup fields are populated
                            PopulateAssignmentLookupFields(assignment);
                            currentShiftMachines.Add(assignment);
                        }
                    }
                }

                shiftMachinesView = CollectionViewSource.GetDefaultView(currentShiftMachines) as ListCollectionView;
                dgShiftMachines.ItemsSource = shiftMachinesView;
                txtShiftMachineCount.Text = $"{currentShiftMachines.Count} assignment(s)";

                // Subscribe to changes
                if (currentShiftMachines is INotifyCollectionChanged collectionChanged)
                {
                    collectionChanged.CollectionChanged -= ShiftMachines_CollectionChanged;
                    collectionChanged.CollectionChanged += ShiftMachines_CollectionChanged;
                }

                foreach (ShiftMachineAssignment assignment in currentShiftMachines)
                {
                    assignment.PropertyChanged -= ShiftMachine_PropertyChanged;
                    assignment.PropertyChanged += ShiftMachine_PropertyChanged;
                }

                hasUnsavedShiftMachines = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading machines for shift: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        /// <summary>
        /// Populates the lookup fields (MachineIdJensen and DayOfWeekName) for an assignment
        /// </summary>
        private void PopulateAssignmentLookupFields(ShiftMachineAssignment assignment)
        {
            // Populate MachineIdJensen if not already set
            if (assignment.MachineIdJensen > 0)
            {
                var machines = dataAccess.GetAllMachines(null, false);
                var machine = machines.Cast<Machine>().FirstOrDefault(m => m.RecNum == assignment.MachineRecNum);
                if (machine != null)
                {
                    assignment.MachineIdJensen = machine.IdJensen;
                }
            }
        }

        private void ShiftMachines_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (ShiftMachineAssignment assignment in e.NewItems)
                {
                    assignment.PropertyChanged += ShiftMachine_PropertyChanged;
                }
                MarkShiftMachinesChanged();
            }

            if (currentShiftMachines != null)
            {
                txtShiftMachineCount.Text = $"{currentShiftMachines.Count} assignment(s)";
            }
        }

        private void ShiftMachine_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            MarkShiftMachinesChanged();
        }

        private void BtnAddMachinesToShift_Click(object sender, RoutedEventArgs e)
        {
            if (currentlySelectedShift == null)
            {
                MessageBox.Show("Please select a shift first.", "No Shift Selected",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (currentlySelectedShift.ShiftID == 0)
            {
                MessageBox.Show("Please save the shift before adding machines.", "Unsaved Shift",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var dialog = new AddMachinesDialog(currentlySelectedShift, assignments);
                if (dialog.ShowDialog() == true)
                {
                    // Add to both collections
                    foreach (var assignment in dialog.NewAssignments)
                    {
                        // Ensure lookup fields are populated (they should already be set by the dialog)
                        PopulateAssignmentLookupFields(assignment);

                        currentShiftMachines.Add(assignment);
                        assignments.Add(assignment);
                    }

                    shiftMachinesView?.Refresh();
                    txtShiftMachineCount.Text = $"{currentShiftMachines.Count} assignment(s)";
                    MarkShiftMachinesChanged();

                    MessageBox.Show(
                        $"Added {dialog.NewAssignments.Count} assignment(s) to the list.\n\n" +
                        $"Click 'Save Assignments' to save to database.\n\n" +
                        $"Note: The save will fail if any assignments create overlapping shifts.",
                        "Assignments Added",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adding machines to shift: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRemoveMachineFromShift_Click(object sender, RoutedEventArgs e)
        {
            if (dgShiftMachines.SelectedItems.Count == 0)
            {
                MessageBox.Show("Please select assignment(s) to remove.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Remove {dgShiftMachines.SelectedItems.Count} selected assignment(s)?",
                "Confirm Remove",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;

                    var itemsToRemove = dgShiftMachines.SelectedItems.Cast<ShiftMachineAssignment>().ToList();

                    foreach (var assignment in itemsToRemove)
                    {
                        assignment.DeleteRecord = true;
                    }

                    dgShiftMachines.SelectedItems.Clear();
                    bool saved = currentShiftMachines.UpdateToDB();

                    // Reload both collections
                    LoadAllAssignments();
                    LoadMachinesForShift(currentlySelectedShift);

                    hasUnsavedShiftMachines = false;
                    UpdateStatusBar();
                    Mouse.OverrideCursor = null;

                    if (saved)
                        MessageBox.Show("Assignment(s) removed successfully.", "Success",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    else
                        ShowSaveFailed("removing assignments", "an unsaved new assignment in the list overlaps another shift.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error removing assignments: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }
        }

        private void BtnSaveShiftAssignments_Click(object sender, RoutedEventArgs e)
        {
            if (currentShiftMachines == null || currentShiftMachines.Count == 0)
            {
                MessageBox.Show("No assignments to save.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;

                // Validate for overlaps BEFORE attempting to save
                var (hasOverlap, conflicts) = CheckForOverlappingAssignments(currentShiftMachines);

                if (hasOverlap)
                {
                    Mouse.OverrideCursor = null;

                    // Show detailed conflict information
                    string conflictDetails = string.Join("\n\n", conflicts);
                    MessageBox.Show(
                        "Cannot save assignments: The following conflicts were detected:\n\n" +
                        conflictDetails + "\n\n" +
                        "Please remove the conflicting assignments and try again.",
                        "Overlap Conflicts Detected",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                // No overlaps detected - proceed with save
                bool saved = currentShiftMachines.UpdateToDB();

                // Reload from database to get any generated IDs
                LoadAllAssignments();
                LoadMachinesForShift(currentlySelectedShift);

                hasUnsavedShiftMachines = false;
                UpdateStatusBar();
                Mouse.OverrideCursor = null;

                if (saved)
                    MessageBox.Show("Assignments saved successfully!", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                else
                    ShowSaveFailed("saving assignments", "an assignment overlaps another shift on the same machine and day (checked by the database).");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving assignments: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }
        private void BtnRefreshShiftMachines_Click(object sender, RoutedEventArgs e)
        {
            if (hasUnsavedShiftMachines)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes to machine assignments. Refreshing will lose these changes. Continue?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No) return;
            }

            LoadAllAssignments();
            LoadMachinesForShift(currentlySelectedShift);
            hasUnsavedShiftMachines = false;
            UpdateStatusBar();
        }
        /// <summary>
        /// Checks if any of the new assignments would overlap with existing assignments
        /// </summary>
        private (bool hasOverlap, List<string> conflicts) CheckForOverlappingAssignments(ShiftMachineAssignments assignmentsToCheck)
        {
            var conflicts = new List<string>();
            var checkedNewAssignments = new List<ShiftMachineAssignment>();

            foreach (ShiftMachineAssignment newAssignment in assignmentsToCheck)
            {
                // Skip if this assignment already exists in the database (has an ID)
                if (newAssignment.AssignmentID != 0) continue;

                // Get the shift times for this assignment
                var newShift = shifts.Cast<Shift>().FirstOrDefault(s => s.ShiftID == newAssignment.ShiftID);
                if (newShift == null) continue;

                // Check against ALL assignments in the global cache (saved and new) for the same machine and day
                foreach (ShiftMachineAssignment existingAssignment in assignments)
                {
                    // Skip if same assignment, or a new one already checked (don't report a pair twice)
                    if (ReferenceEquals(existingAssignment, newAssignment)) continue;
                    if (checkedNewAssignments.Contains(existingAssignment)) continue;

                    // Skip if different machine or day
                    if (existingAssignment.MachineRecNum != newAssignment.MachineRecNum) continue;
                    if (existingAssignment.DayOfWeek != newAssignment.DayOfWeek) continue;
                    if (!existingAssignment.IsActive) continue;

                    // Get the existing shift times
                    var existingShift = shifts.Cast<Shift>().FirstOrDefault(s => s.ShiftID == existingAssignment.ShiftID);
                    if (existingShift == null) continue;

                    // Check for time overlap
                    if (ShiftsOverlap(newShift.StartTime, newShift.EndTime,
                                    existingShift.StartTime, existingShift.EndTime))
                    {
                        conflicts.Add(
                            $"Machine {newAssignment.MachineIdJensen} on {newAssignment.DayOfWeekName}: " +
                            $"'{newShift.ShiftName}' ({newShift.StartTime:hh\\:mm}-{newShift.EndTime:hh\\:mm}) " +
                            $"overlaps with '{existingShift.ShiftName}' ({existingShift.StartTime:hh\\:mm}-{existingShift.EndTime:hh\\:mm})"
                        );
                    }
                }
                checkedNewAssignments.Add(newAssignment);
            }

            return (conflicts.Count > 0, conflicts);
        }

        /// <summary>
        /// Checks whether edited shift times would overlap another active shift assigned to the same
        /// machine and day. The database trigger only runs when assignments change, not shift times.
        /// </summary>
        private List<string> CheckForOverlappingShiftTimes()
        {
            var conflicts = new List<string>();
            if (assignments == null) return conflicts;

            var activeAssignments = assignments.Cast<ShiftMachineAssignment>()
                .Where(a => a.AssignmentID != 0 && a.IsActive).ToList();

            for (int i = 0; i < activeAssignments.Count; i++)
            {
                for (int j = i + 1; j < activeAssignments.Count; j++)
                {
                    var a = activeAssignments[i];
                    var b = activeAssignments[j];
                    if (a.MachineRecNum != b.MachineRecNum || a.DayOfWeek != b.DayOfWeek || a.ShiftID == b.ShiftID) continue;

                    var shiftA = shifts.Cast<Shift>().FirstOrDefault(s => s.ShiftID == a.ShiftID);
                    var shiftB = shifts.Cast<Shift>().FirstOrDefault(s => s.ShiftID == b.ShiftID);
                    if (shiftA == null || shiftB == null || shiftA.DeleteRecord || shiftB.DeleteRecord) continue;
                    if (!shiftA.IsActive || !shiftB.IsActive) continue;
                    if (!shiftA.HasChanged && !shiftB.HasChanged) continue; // only report conflicts caused by these edits

                    if (ShiftsOverlap(shiftA.StartTime, shiftA.EndTime, shiftB.StartTime, shiftB.EndTime))
                    {
                        conflicts.Add(
                            $"Machine {a.MachineIdJensen} on {a.DayOfWeekName}: " +
                            $"'{shiftA.ShiftName}' ({shiftA.StartTime:hh\\:mm}-{shiftA.EndTime:hh\\:mm}) " +
                            $"overlaps with '{shiftB.ShiftName}' ({shiftB.StartTime:hh\\:mm}-{shiftB.EndTime:hh\\:mm})"
                        );
                    }
                }
            }

            return conflicts;
        }

        /// <summary>
        /// Determines if two time ranges overlap, handling cross-midnight shifts
        /// </summary>
        private bool ShiftsOverlap(TimeSpan start1, TimeSpan end1, TimeSpan start2, TimeSpan end2)
        {
            bool shift1CrossesMidnight = end1 < start1;
            bool shift2CrossesMidnight = end2 < start2;

            if (!shift1CrossesMidnight && !shift2CrossesMidnight)
            {
                // Normal case: neither shift crosses midnight
                // Overlap if: NOT (end1 <= start2 OR start1 >= end2)
                return !(end1 <= start2 || start1 >= end2);
            }
            else if (shift1CrossesMidnight && !shift2CrossesMidnight)
            {
                // Shift 1 crosses midnight, shift 2 doesn't
                // Shift 1 occupies [start1 to midnight) and [midnight to end1)
                // They overlap unless shift 2 is entirely in the gap
                return !(end2 <= start1 && start2 >= end1);
            }
            else if (!shift1CrossesMidnight && shift2CrossesMidnight)
            {
                // Shift 2 crosses midnight, shift 1 doesn't
                return !(end1 <= start2 && start1 >= end2);
            }
            else
            {
                // Both shifts cross midnight - they always overlap
                return true;
            }
        }
        #endregion

        #region Break Times for Selected Shift
        // Saved breaks taken out of the grid with Remove Selected - deleted from the database by Save Breaks
        private List<ShiftBreak> removedBreaks = new List<ShiftBreak>();
        // BreakID -> time as loaded, to tell which saved breaks were edited
        private Dictionary<int, TimeSpan> originalBreakTimes = new Dictionary<int, TimeSpan>();

        private static TimeSpan WrapToTimeOfDay(TimeSpan time)
        {
            long ticks = time.Ticks % TimeSpan.TicksPerDay;
            return new TimeSpan(ticks < 0 ? ticks + TimeSpan.TicksPerDay : ticks);
        }

        /// <summary>
        /// Minutes from the shift's start to the given time of day (handles shifts that cross midnight)
        /// </summary>
        private static double MinutesIntoShift(Shift shift, TimeSpan time)
        {
            return WrapToTimeOfDay(time - shift.StartTime).TotalMinutes;
        }

        /// <summary>
        /// Loads the break times of the currently selected shift by filtering the cached breaks.
        /// Nothing in this grid is written to the database until Save Breaks.
        /// </summary>
        private void LoadBreaksForShift(Shift shift)
        {
            removedBreaks.Clear();
            originalBreakTimes.Clear();
            hasUnsavedBreaks = false;

            if (shift == null || shift.ShiftID == 0)
            {
                currentShiftBreaks = null;
                shiftBreaksView = null;
                dgShiftBreaks.ItemsSource = null;
                txtShiftBreakCount.Text = shift == null ? "0 breaks" : "Save shift first to add breaks";
                return;
            }

            try
            {
                // A fresh list for this shift, in the order the breaks happen during the shift
                currentShiftBreaks = new ShiftBreaks();

                if (breaks != null)
                {
                    foreach (ShiftBreak shiftBreak in breaks.Cast<ShiftBreak>()
                        .Where(b => b.ShiftID == shift.ShiftID)
                        .OrderBy(b => MinutesIntoShift(shift, b.BreakTime))
                        .ToList())
                    {
                        originalBreakTimes[shiftBreak.BreakID] = shiftBreak.BreakTime;
                        shiftBreak.PropertyChanged -= ShiftBreak_PropertyChanged;
                        shiftBreak.PropertyChanged += ShiftBreak_PropertyChanged;
                        currentShiftBreaks.Add(shiftBreak);
                    }
                }

                shiftBreaksView = CollectionViewSource.GetDefaultView(currentShiftBreaks) as ListCollectionView;
                dgShiftBreaks.ItemsSource = shiftBreaksView;
                UpdateBreakCount();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading break times for shift: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateBreakCount()
        {
            int count = currentShiftBreaks == null ? 0 : currentShiftBreaks.Count;
            txtShiftBreakCount.Text = count == 1 ? "1 break" : $"{count} breaks";
        }

        private void ShiftBreak_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            MarkBreaksChanged();
        }

        /// <summary>
        /// A free time for a new break: half-way through the shift, or the next free 5 minutes after that.
        /// </summary>
        private TimeSpan SuggestBreakTime(Shift shift)
        {
            var used = new HashSet<TimeSpan>(currentShiftBreaks.Cast<ShiftBreak>().Select(b => b.BreakTime));
            int length = (int)MinutesIntoShift(shift, shift.EndTime);
            int half = Math.Max(5, length / 2 / 5 * 5);

            for (int step = 0; step < length; step += 5)
            {
                int offset = (half + step) % length;
                if (offset == 0) continue;

                TimeSpan candidate = WrapToTimeOfDay(shift.StartTime + TimeSpan.FromMinutes(offset));
                if (!used.Contains(candidate)) return candidate;
            }
            return WrapToTimeOfDay(shift.StartTime + TimeSpan.FromMinutes(Math.Min(half, Math.Max(1, length - 1))));
        }

        private void BtnAddBreak_Click(object sender, RoutedEventArgs e)
        {
            if (currentlySelectedShift == null)
            {
                MessageBox.Show("Please select a shift first.", "No Shift Selected",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (currentlySelectedShift.ShiftID == 0 || currentShiftBreaks == null)
            {
                MessageBox.Show("Please save the shift before adding breaks.", "Unsaved Shift",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Shift shift = currentlySelectedShift;
            if (!IsTimeOfDay(shift.StartTime) || !IsTimeOfDay(shift.EndTime) || MinutesIntoShift(shift, shift.EndTime) < 2)
            {
                MessageBox.Show("This shift's start and end times leave no room for a break. Correct and save the shift first.",
                    "Shift Times", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                dgShiftBreaks.CommitEdit(DataGridEditingUnit.Row, true);

                ShiftBreak newBreak = new ShiftBreak
                {
                    ShiftID = shift.ShiftID,
                    BreakTime = SuggestBreakTime(shift),
                    ForceNew = true
                };
                newBreak.PropertyChanged += ShiftBreak_PropertyChanged;
                currentShiftBreaks.Add(newBreak);
                shiftBreaksView?.Refresh();
                UpdateBreakCount();
                MarkBreaksChanged();

                // Select the new row and open its time for typing
                dgShiftBreaks.SelectedItem = newBreak;
                dgShiftBreaks.ScrollIntoView(newBreak);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        dgShiftBreaks.CurrentCell = new DataGridCellInfo(newBreak, dgShiftBreaks.Columns[0]);
                        dgShiftBreaks.Focus();
                        dgShiftBreaks.BeginEdit();
                    }
                    catch { /* the row is still there to edit by hand */ }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adding break: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRemoveBreak_Click(object sender, RoutedEventArgs e)
        {
            if (currentShiftBreaks == null || dgShiftBreaks.SelectedItems.Count == 0)
            {
                MessageBox.Show("Please select break time(s) to remove.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // Leave edit mode first (a half-typed time in a row being removed doesn't matter)
                if (!dgShiftBreaks.CommitEdit(DataGridEditingUnit.Row, true))
                    dgShiftBreaks.CancelEdit(DataGridEditingUnit.Row);

                var itemsToRemove = dgShiftBreaks.SelectedItems.Cast<ShiftBreak>().ToList();
                dgShiftBreaks.SelectedItems.Clear();

                foreach (var shiftBreak in itemsToRemove)
                {
                    currentShiftBreaks.Remove(shiftBreak);
                    if (shiftBreak.BreakID != 0)
                        removedBreaks.Add(shiftBreak); // deleted from the database by Save Breaks
                }

                shiftBreaksView?.Refresh();
                UpdateBreakCount();
                MarkBreaksChanged();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error removing breaks: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// A break must be a whole-minute time of day strictly between the shift's start and end, and
        /// appear only once in the shift.
        /// </summary>
        private List<string> CheckBreakTimes(Shift shift, IEnumerable<ShiftBreak> shiftBreaks)
        {
            var problems = new List<string>();
            var seen = new HashSet<TimeSpan>();

            foreach (ShiftBreak shiftBreak in shiftBreaks)
            {
                TimeSpan time = shiftBreak.BreakTime;
                string shown = IsTimeOfDay(time) ? time.ToString(@"hh\:mm") : time.ToString();

                if (!IsTimeOfDay(time) || time.Seconds != 0 || time.Milliseconds != 0)
                {
                    problems.Add($"{shown} is not a time of day in whole minutes. Enter times as HH:mm, for example 10:00.");
                }
                else if (!IsInsideShift(shift, time))
                {
                    problems.Add($"{shown} is not between the shift's start ({shift.StartTime:hh\\:mm}) and end ({shift.EndTime:hh\\:mm}).");
                }
                else if (!seen.Add(time))
                {
                    problems.Add($"{shown} is entered more than once.");
                }
            }

            return problems;
        }

        private void BtnSaveBreaks_Click(object sender, RoutedEventArgs e)
        {
            if (currentlySelectedShift == null || currentShiftBreaks == null)
            {
                MessageBox.Show("Please select a saved shift first.", "No Shift Selected",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                if (!dgShiftBreaks.CommitEdit(DataGridEditingUnit.Row, true))
                {
                    MessageBox.Show("Correct the highlighted break time first. Enter times as HH:mm, for example 10:00.",
                        "Break Times", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Shift shift = currentlySelectedShift;

                // Breaks are checked against the shift's times, so those must be the saved ones
                if (shift.HasChanged)
                {
                    MessageBox.Show($"Shift '{shift.ShiftName}' has unsaved changes. Save the shifts first, then save its breaks.",
                        "Unsaved Shift", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var problems = CheckBreakTimes(shift, currentShiftBreaks.Cast<ShiftBreak>());
                if (problems.Any())
                {
                    MessageBox.Show(
                        $"Cannot save the break times of shift '{shift.ShiftName}':\n\n" + string.Join("\n", problems),
                        "Break Times",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                // Deletes first, then inserts: a saved break whose time was edited is deleted and added
                // again, so the save can never trip over the one-break-per-time rule part way through
                var toSave = new ShiftBreaks();
                var toInsert = new List<ShiftBreak>();

                foreach (ShiftBreak removed in removedBreaks)
                {
                    removed.DeleteRecord = true;
                    toSave.Add(removed);
                }

                foreach (ShiftBreak shiftBreak in currentShiftBreaks.Cast<ShiftBreak>().ToList())
                {
                    if (shiftBreak.BreakID == 0)
                    {
                        toInsert.Add(shiftBreak);
                    }
                    else if (!originalBreakTimes.TryGetValue(shiftBreak.BreakID, out TimeSpan original) || original != shiftBreak.BreakTime)
                    {
                        toInsert.Add(new ShiftBreak { ShiftID = shift.ShiftID, BreakTime = shiftBreak.BreakTime, ForceNew = true });
                        shiftBreak.DeleteRecord = true;
                        toSave.Add(shiftBreak);
                    }
                }

                foreach (ShiftBreak shiftBreak in toInsert)
                {
                    toSave.Add(shiftBreak);
                }

                Mouse.OverrideCursor = Cursors.Wait;
                bool saved = toSave.Count == 0 || toSave.UpdateToDB();

                // Reload from database to get any generated IDs
                LoadAllBreaks();
                LoadBreaksForShift(currentlySelectedShift);
                UpdateStatusBar();
                Mouse.OverrideCursor = null;

                if (saved)
                    MessageBox.Show("Break times saved successfully.\n\nThe service starts using them within about a minute.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                else
                    ShowSaveFailed("saving break times", "the database connection was lost, or the same break was added by someone else at the same time.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving break times: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void BtnRefreshBreaks_Click(object sender, RoutedEventArgs e)
        {
            if (hasUnsavedBreaks)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes to break times. Refreshing will lose these changes. Continue?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No) return;
            }

            dgShiftBreaks.CancelEdit(DataGridEditingUnit.Row);
            LoadAllBreaks();
            LoadBreaksForShift(currentlySelectedShift);
            UpdateStatusBar();
        }
        #endregion

        #region Operator Settings
        private void LoadAvailableOperators()
        {
            try
            {
                var operators = dataAccess.GetOperators(null, true);
                availableOperators = operators
                    .Cast<Operator>()
                    .Select(op => new OperatorDisplayItem
                    {
                        RecNum = op.RecNum,
                        IdJensen = op.IdJensen,
                        ShortDescription = op.ShortDescription
                    })
                    .OrderBy(op => op.IdJensen)
                    .ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading operators: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                availableOperators = new List<OperatorDisplayItem>();
            }
        }

        private void LoadOperatorSettings()
        {
            operatorSettings = dataAccess.GetOperatorSettings(null, true);
            dgOperatorSettings.ItemsSource = operatorSettings;

            if (operatorSettings is INotifyCollectionChanged collectionChanged)
            {
                collectionChanged.CollectionChanged += OperatorSettings_CollectionChanged;
            }

            foreach (OperatorSetting setting in operatorSettings)
            {
                setting.PropertyChanged += OperatorSettings_PropertyChanged;
            }

            hasUnsavedOperatorSettings = false;
        }

        private void OperatorSettings_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (OperatorSetting setting in e.NewItems)
                {
                    setting.PropertyChanged += OperatorSettings_PropertyChanged;
                }
                MarkOperatorSettingsChanged();
            }
        }

        private void OperatorSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            MarkOperatorSettingsChanged();
        }

        private void BtnAddOperatorSetting_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Ensure operators are loaded
                if (availableOperators == null || !availableOperators.Any())
                {
                    MessageBox.Show("No operators available. Please check that operators exist in the system.",
                        "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Check if all operators already have settings
                var usedOperatorRecNums = operatorSettings
                    .Cast<OperatorSetting>()
                    .Where(s => !s.DeleteRecord)
                    .Select(s => s.OperatorRecNum)
                    .ToHashSet();

                var availableOps = availableOperators
                    .Where(op => !usedOperatorRecNums.Contains(op.RecNum))
                    .ToList();

                if (!availableOps.Any())
                {
                    MessageBox.Show("All operators already have settings configured.", "Information",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Create new setting with RecNum = 0 (user must select from dropdown)
                OperatorSetting newSetting = new OperatorSetting
                {
                    OperatorRecNum = 0,
                    CanReplaceOperator = false,
                    CanLogoutShift = false,
                    ModifiedDate = DateTime.Now,
                    ForceNew = true
                };
                operatorSettings.Add(newSetting);
                dgOperatorSettings.Items.Refresh();
                MarkOperatorSettingsChanged();

                // Select the newly added row for immediate editing
                dgOperatorSettings.SelectedItem = newSetting;
                dgOperatorSettings.ScrollIntoView(newSetting);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adding operator setting: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnDeleteOperatorSetting_Click(object sender, RoutedEventArgs e)
        {
            if (dgOperatorSettings.SelectedItems.Count == 0) return;

            var result = MessageBox.Show("Delete selected operator setting(s)?", "Confirm Delete",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    Mouse.OverrideCursor = Cursors.Wait;

                    var itemsToDelete = dgOperatorSettings.SelectedItems.Cast<OperatorSetting>().ToList();

                    foreach (var setting in itemsToDelete)
                    {
                        setting.DeleteRecord = true;
                    }

                    dgOperatorSettings.SelectedItems.Clear();
                    bool saved = operatorSettings.UpdateToDB();
                    LoadOperatorSettings();

                    hasUnsavedOperatorSettings = false;
                    UpdateStatusBar();

                    if (!saved)
                    {
                        Mouse.OverrideCursor = null;
                        ShowSaveFailed("deleting operator settings", "another unsaved operator setting in the list is invalid or duplicated.");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error deleting operator settings: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }
            }
        }

        private void BtnSaveOperatorSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                dgOperatorSettings.CommitEdit(DataGridEditingUnit.Row, true);
                dgOperatorSettings.CommitEdit(DataGridEditingUnit.Cell, true);

                // Validation: Check for unselected operators (RecNum = 0)
                var unselectedSettings = operatorSettings.Cast<OperatorSetting>().Where(s => !s.DeleteRecord && s.OperatorRecNum == 0).ToList();
                if (unselectedSettings.Any())
                {
                    MessageBox.Show("Please select an operator for all settings before saving.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Validation: Check for duplicate operators
                var activeSettings = operatorSettings.Cast<OperatorSetting>().Where(s => !s.DeleteRecord).ToList();
                var duplicates = activeSettings
                    .GroupBy(s => s.OperatorRecNum)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToList();

                if (duplicates.Any())
                {
                    var duplicateOperators = string.Join(", ", duplicates.Select(recNum =>
                    {
                        var op = availableOperators.FirstOrDefault(o => o.RecNum == recNum);
                        return op != null ? $"{op.IdJensen} - {op.ShortDescription}" : recNum.ToString();
                    }));
                    MessageBox.Show($"Duplicate operators found: {duplicateOperators}\n\nEach operator can only have one settings entry.",
                        "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Mouse.OverrideCursor = Cursors.Wait;
                bool saved = operatorSettings.UpdateToDB();

                LoadOperatorSettings();
                hasUnsavedOperatorSettings = false;
                UpdateStatusBar();
                Mouse.OverrideCursor = null;

                if (saved)
                    MessageBox.Show("Operator settings saved successfully", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                else
                    ShowSaveFailed("saving operator settings", "the operator already has a settings entry saved by someone else.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving operator settings: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private void BtnRefreshOperatorSettings_Click(object sender, RoutedEventArgs e)
        {
            if (hasUnsavedOperatorSettings)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Refreshing will lose these changes. Continue?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No) return;
            }

            LoadOperatorSettings();
            hasUnsavedOperatorSettings = false;
            UpdateStatusBar();
        }
        #endregion
    }
}