/*
 * OnlyRights
 * Copyright (c) 2026 Danny Perondi
 * All rights reserved.
 *
 * Proprietary and confidential.
 * Viewing is permitted only for reference, evaluation, or internal review.
 * Unauthorized copying, modification, distribution, sublicensing,
 * commercial use, or reuse of this file is prohibited without prior
 * written permission from Danny Perondi.
 */
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using NtfsAudit.App.Models;
using NtfsAudit.App.Services;

namespace NtfsAudit.App.ViewModels
{
    public partial class MainViewModel
    {
        public RelayCommand ToggleSettingsCommand { get; private set; }
        public RelayCommand CloseSettingsCommand { get; private set; }
        public RelayCommand RefreshSchedulesCommand { get; private set; }
        public RelayCommand StartServiceRuntimeCommand { get; private set; }
        public RelayCommand StopServiceRuntimeCommand { get; private set; }
        public RelayCommand NewScheduleCommand { get; private set; }
        public RelayCommand SaveScheduleCommand { get; private set; }
        public RelayCommand DeleteScheduleCommand { get; private set; }

        public bool IsSettingsOpen
        {
            get { return _isSettingsOpen; }
            set
            {
                if (_isSettingsOpen == value)
                {
                    return;
                }

                _isSettingsOpen = value;
                OnPropertyChanged("IsSettingsOpen");
                if (CloseSettingsCommand != null)
                {
                    CloseSettingsCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string PathCompatibilitySummaryText
        {
            get
            {
                if (GetCurrentScheduleRoots().Count == 0)
                {
                    return LocalizationManager.Text("Scan.CompatibilityPending");
                }

                return _pathCompatibility == null ? string.Empty : _pathCompatibility.SummaryText;
            }
        }

        public string PathCompatibilityDisabledText
        {
            get { return _pathCompatibility == null ? string.Empty : _pathCompatibility.DisabledOptionsText; }
        }

        public bool HasPathCompatibilityWarnings
        {
            get { return _pathCompatibility != null && _pathCompatibility.ReasonTexts.Count > 0; }
        }

        public bool SupportsConfiguredCredentialForSelection
        {
            get { return _pathCompatibility != null && _pathCompatibility.SupportsConfiguredCredential; }
        }

        public bool SupportsSharePermissionsForSelection
        {
            get { return _pathCompatibility != null && _pathCompatibility.SupportsSharePermissions; }
        }

        public string ServiceNextRunText
        {
            get { return _serviceNextRunText; }
            private set
            {
                _serviceNextRunText = value;
                OnPropertyChanged("ServiceNextRunText");
            }
        }

        public string ServiceScheduleSummaryText
        {
            get { return _serviceScheduleSummaryText; }
            private set
            {
                _serviceScheduleSummaryText = value;
                OnPropertyChanged("ServiceScheduleSummaryText");
            }
        }

        public ObservableCollection<ServiceScheduleItemViewModel> ServiceSchedules
        {
            get { return _serviceSchedules; }
        }

        public ServiceScheduleItemViewModel SelectedServiceSchedule
        {
            get { return _selectedServiceSchedule; }
            set
            {
                _selectedServiceSchedule = value;
                OnPropertyChanged("SelectedServiceSchedule");
                LoadScheduleIntoEditor(value == null ? null : value.Definition);
                if (DeleteScheduleCommand != null)
                {
                    DeleteScheduleCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public IReadOnlyList<SelectionOption<ServiceScheduleFrequencyKind>> AvailableScheduleFrequencyOptions
        {
            get
            {
                return new[]
                {
                    new SelectionOption<ServiceScheduleFrequencyKind>(ServiceScheduleFrequencyKind.OneShot, LocalizationManager.Text("Schedule.Frequency.OneShot")),
                    new SelectionOption<ServiceScheduleFrequencyKind>(ServiceScheduleFrequencyKind.Daily, LocalizationManager.Text("Schedule.Frequency.Daily")),
                    new SelectionOption<ServiceScheduleFrequencyKind>(ServiceScheduleFrequencyKind.Weekly, LocalizationManager.Text("Schedule.Frequency.Weekly")),
                    new SelectionOption<ServiceScheduleFrequencyKind>(ServiceScheduleFrequencyKind.Monthly, LocalizationManager.Text("Schedule.Frequency.Monthly"))
                };
            }
        }

        public SelectionOption<ServiceScheduleFrequencyKind> SelectedScheduleFrequencyOption
        {
            get { return AvailableScheduleFrequencyOptions.FirstOrDefault(option => option.Value == ScheduleFrequencyKind) ?? AvailableScheduleFrequencyOptions[0]; }
            set
            {
                if (value != null)
                {
                    ScheduleFrequencyKind = value.Value;
                }
            }
        }

        public IReadOnlyList<SelectionOption<DayOfWeek>> AvailableScheduleDayOptions
        {
            get
            {
                return new[]
                {
                    new SelectionOption<DayOfWeek>(DayOfWeek.Monday, LocalizationManager.Text("Schedule.Day.Monday")),
                    new SelectionOption<DayOfWeek>(DayOfWeek.Tuesday, LocalizationManager.Text("Schedule.Day.Tuesday")),
                    new SelectionOption<DayOfWeek>(DayOfWeek.Wednesday, LocalizationManager.Text("Schedule.Day.Wednesday")),
                    new SelectionOption<DayOfWeek>(DayOfWeek.Thursday, LocalizationManager.Text("Schedule.Day.Thursday")),
                    new SelectionOption<DayOfWeek>(DayOfWeek.Friday, LocalizationManager.Text("Schedule.Day.Friday")),
                    new SelectionOption<DayOfWeek>(DayOfWeek.Saturday, LocalizationManager.Text("Schedule.Day.Saturday")),
                    new SelectionOption<DayOfWeek>(DayOfWeek.Sunday, LocalizationManager.Text("Schedule.Day.Sunday"))
                };
            }
        }

        public SelectionOption<DayOfWeek> SelectedScheduleDayOption
        {
            get { return AvailableScheduleDayOptions.FirstOrDefault(option => option.Value == ScheduleDayOfWeek) ?? AvailableScheduleDayOptions[0]; }
            set
            {
                if (value != null)
                {
                    ScheduleDayOfWeek = value.Value;
                }
            }
        }

        public string ScheduleName
        {
            get { return _scheduleName; }
            set
            {
                _scheduleName = value;
                OnPropertyChanged("ScheduleName");
                RaiseScheduleEditorChanged();
            }
        }

        public ServiceScheduleFrequencyKind ScheduleFrequencyKind
        {
            get { return _scheduleFrequencyKind; }
            set
            {
                _scheduleFrequencyKind = value;
                OnPropertyChanged("ScheduleFrequencyKind");
                OnPropertyChanged("IsOneShotSchedule");
                OnPropertyChanged("IsWeeklySchedule");
                OnPropertyChanged("IsMonthlySchedule");
                RaiseScheduleEditorChanged();
            }
        }

        public bool IsOneShotSchedule
        {
            get { return _scheduleFrequencyKind == ServiceScheduleFrequencyKind.OneShot; }
        }

        public bool IsWeeklySchedule
        {
            get { return _scheduleFrequencyKind == ServiceScheduleFrequencyKind.Weekly; }
        }

        public bool IsMonthlySchedule
        {
            get { return _scheduleFrequencyKind == ServiceScheduleFrequencyKind.Monthly; }
        }

        public DateTime ScheduleOneShotDate
        {
            get { return _scheduleOneShotDate; }
            set
            {
                _scheduleOneShotDate = value;
                OnPropertyChanged("ScheduleOneShotDate");
                RaiseScheduleEditorChanged();
            }
        }

        public DateTime ScheduleTimeOfDay
        {
            get { return _scheduleTimeOfDay; }
            set
            {
                _scheduleTimeOfDay = value;
                _scheduleTimeText = value.ToString("HH:mm");
                OnPropertyChanged("ScheduleTimeOfDay");
                OnPropertyChanged("ScheduleTimeText");
                RaiseScheduleEditorChanged();
            }
        }

        public string ScheduleTimeText
        {
            get { return _scheduleTimeText; }
            set
            {
                _scheduleTimeText = value;
                OnPropertyChanged("ScheduleTimeText");
                if (string.IsNullOrWhiteSpace(value))
                {
                    RaiseScheduleEditorChanged();
                    return;
                }

                if (TimeSpan.TryParse(value, out var parsed))
                {
                    ScheduleTimeOfDay = DateTime.Today.Add(parsed);
                    return;
                }

                RaiseScheduleEditorChanged();
            }
        }

        public DayOfWeek ScheduleDayOfWeek
        {
            get { return _scheduleDayOfWeek; }
            set
            {
                _scheduleDayOfWeek = value;
                OnPropertyChanged("ScheduleDayOfWeek");
                RaiseScheduleEditorChanged();
            }
        }

        public int ScheduleDayOfMonth
        {
            get { return _scheduleDayOfMonth; }
            set
            {
                _scheduleDayOfMonth = value < 1 ? 1 : value > 31 ? 31 : value;
                OnPropertyChanged("ScheduleDayOfMonth");
                RaiseScheduleEditorChanged();
            }
        }

        public bool ScheduleEnabled
        {
            get { return _scheduleEnabled; }
            set
            {
                _scheduleEnabled = value;
                OnPropertyChanged("ScheduleEnabled");
                RaiseScheduleEditorChanged();
            }
        }

        public bool CanManageServiceSchedules
        {
            get { return !_isViewerMode && IsServiceInstalled; }
        }

        public bool CanSaveSchedule
        {
            get
            {
                return CanManageServiceSchedules
                    && !string.IsNullOrWhiteSpace(ScheduleName)
                    && string.IsNullOrWhiteSpace(GetScheduleTimeValidationMessage())
                    && GetCurrentScheduleRoots().Count > 0;
            }
        }

        public bool CanDeleteSchedule
        {
            get { return CanManageServiceSchedules && SelectedServiceSchedule != null; }
        }

        public string ServiceSchedulingAvailabilityText
        {
            get
            {
                return IsServiceInstalled
                    ? LocalizationManager.Text("Settings.ServiceSchedulingAvailable")
                    : LocalizationManager.Text("Settings.ServiceSchedulingUnavailable");
            }
        }

        public string ScheduleEditorFeedbackText
        {
            get { return _scheduleEditorFeedbackText; }
            private set
            {
                _scheduleEditorFeedbackText = value;
                OnPropertyChanged("ScheduleEditorFeedbackText");
            }
        }

        private void ToggleSettings()
        {
            IsSettingsOpen = !IsSettingsOpen;
        }

        private void CloseSettings()
        {
            IsSettingsOpen = false;
        }

        private void RefreshCompatibilityState()
        {
            _pathCompatibility = ScanPathCompatibilityPolicy.EvaluatePaths(GetCurrentScheduleRoots());
            if (!_pathCompatibility.SupportsSharePermissions && _includeSharePermissions)
            {
                _includeSharePermissions = false;
                OnPropertyChanged("IncludeSharePermissions");
            }

            OnPropertyChanged("PathCompatibilitySummaryText");
            OnPropertyChanged("PathCompatibilityDisabledText");
            OnPropertyChanged("HasPathCompatibilityWarnings");
            OnPropertyChanged("SupportsConfiguredCredentialForSelection");
            OnPropertyChanged("SupportsSharePermissionsForSelection");
            OnPropertyChanged("SelectedScanRootEffectiveCredentialSource");
        }

        private void RefreshServiceSchedules()
        {
            var selectedId = SelectedServiceSchedule == null ? null : SelectedServiceSchedule.Definition.ScheduleId;
            var definitions = _serviceScheduleStore.LoadDefinitions();
            var runtimeSnapshot = _serviceScheduleStore.LoadRuntimeSnapshot();
            var statuses = runtimeSnapshot.Schedules == null
                ? new List<ServiceScheduleStatusSnapshot>()
                : runtimeSnapshot.Schedules;
            var nowLocal = DateTime.Now;

            ServiceSchedules.Clear();
            foreach (var definition in definitions.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                var status = statuses.FirstOrDefault(item => string.Equals(item.ScheduleId, definition.ScheduleId, StringComparison.OrdinalIgnoreCase));
                var nextRun = status != null && status.NextRunLocal.HasValue
                    ? status.NextRunLocal
                    : _serviceSchedulePlanner.GetNextOccurrence(definition, nowLocal);
                ServiceSchedules.Add(new ServiceScheduleItemViewModel
                {
                    Definition = definition,
                    Runtime = status,
                    FrequencyLabel = FormatScheduleFrequency(definition),
                    NextRunText = nextRun.HasValue ? nextRun.Value.ToString("g") : LocalizationManager.Text("Settings.NoNextExecution"),
                    RootsSummary = string.Join("; ", (definition.Template == null ? new List<string>() : definition.Template.Roots).Where(root => !string.IsNullOrWhiteSpace(root))),
                    LastMessage = status == null || string.IsNullOrWhiteSpace(status.LastMessage) ? LocalizationManager.Text("Settings.StatusWaiting") : status.LastMessage,
                    StatusBadgeText = definition.IsEnabled ? LocalizationManager.Text("Settings.StatusActive") : LocalizationManager.Text("Settings.StatusPaused"),
                    StatusBadgeBackground = definition.IsEnabled ? "#FF2E7D32" : "#FF607D8B"
                });
            }

            ServiceScheduleSummaryText = ServiceSchedules.Count == 0
                ? LocalizationManager.Text("Settings.NoSchedules")
                : LocalizationManager.Format("Settings.ScheduleSummaryFormat", ServiceSchedules.Count, ServiceSchedules.Count(item => item.Definition.IsEnabled));

            var nextScheduledRun = ServiceSchedules
                .Select(item => item.Runtime != null && item.Runtime.NextRunLocal.HasValue ? item.Runtime.NextRunLocal : _serviceSchedulePlanner.GetNextOccurrence(item.Definition, nowLocal))
                .Where(value => value.HasValue)
                .OrderBy(value => value.Value)
                .FirstOrDefault();
            ServiceNextRunText = nextScheduledRun.HasValue ? nextScheduledRun.Value.ToString("g") : LocalizationManager.Text("Settings.NoNextRun");

            SelectedServiceSchedule = string.IsNullOrWhiteSpace(selectedId)
                ? ServiceSchedules.FirstOrDefault()
                : ServiceSchedules.FirstOrDefault(item => string.Equals(item.Definition.ScheduleId, selectedId, StringComparison.OrdinalIgnoreCase)) ?? ServiceSchedules.FirstOrDefault();
        }

        private void PrepareNewSchedule()
        {
            SelectedServiceSchedule = null;
            LoadScheduleIntoEditor(null);
        }

        private void SaveScheduleDefinition()
        {
            if (!CanSaveSchedule)
            {
                return;
            }

            var definition = SelectedServiceSchedule == null
                ? new ServiceScheduleDefinition
                {
                    ScheduleId = Guid.NewGuid().ToString("N"),
                    CreatedAtUtc = DateTime.UtcNow
                }
                : SelectedServiceSchedule.Definition.Clone();

            definition.Name = (ScheduleName ?? string.Empty).Trim();
            definition.IsEnabled = ScheduleEnabled;
            definition.FrequencyKind = ScheduleFrequencyKind;
            definition.TimeOfDay = ScheduleTimeOfDay.TimeOfDay;
            definition.DayOfWeek = IsWeeklySchedule ? (DayOfWeek?)ScheduleDayOfWeek : null;
            definition.DayOfMonth = IsMonthlySchedule ? (int?)ScheduleDayOfMonth : null;
            definition.OneShotLocalDateTime = IsOneShotSchedule
                ? ScheduleOneShotDate.Date.Add(ScheduleTimeOfDay.TimeOfDay)
                : null;
            definition.Template = BuildCurrentScheduleTemplate();
            definition.UpdatedAtUtc = DateTime.UtcNow;

            _serviceScheduleStore.SaveDefinition(definition);
            RefreshServiceSchedules();
            SelectedServiceSchedule = ServiceSchedules.FirstOrDefault(item => string.Equals(item.Definition.ScheduleId, definition.ScheduleId, StringComparison.OrdinalIgnoreCase));
            ProgressText = LocalizationManager.Format("Settings.ScheduleSaved", definition.Name);
        }

        private void DeleteScheduleDefinition()
        {
            if (!CanDeleteSchedule)
            {
                return;
            }

            var removedName = SelectedServiceSchedule.Definition.Name;
            _serviceScheduleStore.DeleteDefinition(SelectedServiceSchedule.Definition.ScheduleId);
            RefreshServiceSchedules();
            ProgressText = LocalizationManager.Format("Settings.ScheduleRemoved", removedName);
        }

        private void LoadScheduleIntoEditor(ServiceScheduleDefinition definition)
        {
            if (definition == null)
            {
                ScheduleName = LocalizationManager.Text("Settings.DefaultScheduleName");
                ScheduleFrequencyKind = ServiceScheduleFrequencyKind.Daily;
                ScheduleOneShotDate = DateTime.Today;
                ScheduleTimeOfDay = DateTime.Today.AddHours(9);
                ScheduleDayOfWeek = DayOfWeek.Monday;
                ScheduleDayOfMonth = 1;
                ScheduleEnabled = true;
                return;
            }

            ScheduleName = definition.Name;
            ScheduleFrequencyKind = definition.FrequencyKind;
            ScheduleOneShotDate = (definition.OneShotLocalDateTime ?? DateTime.Now).Date;
            ScheduleTimeOfDay = DateTime.Today.Add(definition.TimeOfDay);
            ScheduleDayOfWeek = definition.DayOfWeek ?? DayOfWeek.Monday;
            ScheduleDayOfMonth = definition.DayOfMonth ?? 1;
            ScheduleEnabled = definition.IsEnabled;
        }

        private ServiceScheduledScanTemplate BuildCurrentScheduleTemplate()
        {
            var roots = GetCurrentScheduleRoots();
            return new ServiceScheduledScanTemplate
            {
                Roots = roots.ToList(),
                OutputDirectory = AuditOutputDirectory,
                MaxDepth = ScanAllDepths ? int.MaxValue : MaxDepth,
                ScanAllDepths = ScanAllDepths,
                IncludeInherited = IncludeInherited,
                ResolveIdentities = ResolveIdentities,
                ExcludeServiceAccounts = ResolveIdentities && ExcludeServiceAccounts,
                ExcludeAdminAccounts = ResolveIdentities && ExcludeAdminAccounts,
                ExpandGroups = ResolveIdentities && ExpandGroups,
                UsePowerShell = ResolveIdentities && UsePowerShell,
                EnableAdvancedAudit = EnableAdvancedAudit,
                ComputeEffectiveAccess = EnableAdvancedAudit && ComputeEffectiveAccess,
                IncludeSharePermissions = EnableAdvancedAudit && IncludeSharePermissions && SupportsSharePermissionsForSelection,
                IncludeFiles = EnableAdvancedAudit && IncludeFiles,
                ReadOwnerAndSacl = EnableAdvancedAudit && ReadOwnerAndSacl,
                CompareBaseline = EnableAdvancedAudit && CompareBaseline,
                AnonymizeIdentities = AnonymizeIdentities
            };
        }

        private IReadOnlyList<string> GetCurrentScheduleRoots()
        {
            var roots = ScanRoots.Count > 0
                ? ScanRoots.Select(GetEffectiveScanRoot).ToList()
                : new List<string> { string.IsNullOrWhiteSpace(SelectedDfsTarget) ? RootPath : SelectedDfsTarget };
            return roots
                .Where(root => !string.IsNullOrWhiteSpace(root))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private string FormatScheduleFrequency(ServiceScheduleDefinition definition)
        {
            switch (definition.FrequencyKind)
            {
                case ServiceScheduleFrequencyKind.OneShot:
                    return definition.OneShotLocalDateTime.HasValue
                        ? LocalizationManager.Format("Settings.Frequency.OneShotAt", definition.OneShotLocalDateTime.Value.ToString("g"))
                        : LocalizationManager.Text("Schedule.Frequency.OneShot");
                case ServiceScheduleFrequencyKind.Daily:
                    return LocalizationManager.Format("Settings.Frequency.DailyAt", DateTime.Today.Add(definition.TimeOfDay).ToString("HH:mm"));
                case ServiceScheduleFrequencyKind.Weekly:
                    return LocalizationManager.Format(
                        "Settings.Frequency.WeeklyAt",
                        FormatDayOfWeek(definition.DayOfWeek ?? DayOfWeek.Monday),
                        DateTime.Today.Add(definition.TimeOfDay).ToString("HH:mm"));
                case ServiceScheduleFrequencyKind.Monthly:
                    return LocalizationManager.Format("Settings.Frequency.MonthlyAt", definition.DayOfMonth ?? 1, DateTime.Today.Add(definition.TimeOfDay).ToString("HH:mm"));
                default:
                    return definition.FrequencyKind.ToString();
            }
        }

        private static string FormatDayOfWeek(DayOfWeek dayOfWeek)
        {
            switch (dayOfWeek)
            {
                case DayOfWeek.Monday:
                    return LocalizationManager.Text("Schedule.Day.Monday");
                case DayOfWeek.Tuesday:
                    return LocalizationManager.Text("Schedule.Day.Tuesday");
                case DayOfWeek.Wednesday:
                    return LocalizationManager.Text("Schedule.Day.Wednesday");
                case DayOfWeek.Thursday:
                    return LocalizationManager.Text("Schedule.Day.Thursday");
                case DayOfWeek.Friday:
                    return LocalizationManager.Text("Schedule.Day.Friday");
                case DayOfWeek.Saturday:
                    return LocalizationManager.Text("Schedule.Day.Saturday");
                case DayOfWeek.Sunday:
                    return LocalizationManager.Text("Schedule.Day.Sunday");
                default:
                    return dayOfWeek.ToString();
            }
        }

        private void RaiseScheduleEditorChanged()
        {
            ScheduleEditorFeedbackText = BuildScheduleEditorFeedback();
            OnPropertyChanged("CanSaveSchedule");
            if (SaveScheduleCommand != null)
            {
                SaveScheduleCommand.RaiseCanExecuteChanged();
            }
        }

        private string BuildScheduleEditorFeedback()
        {
            if (!CanManageServiceSchedules)
            {
                return LocalizationManager.Text("Settings.ServiceSchedulingUnavailable");
            }

            if (string.IsNullOrWhiteSpace(ScheduleName))
            {
                return LocalizationManager.Text("Settings.ScheduleNameRequired");
            }

            var scheduleTimeValidationMessage = GetScheduleTimeValidationMessage();
            if (!string.IsNullOrWhiteSpace(scheduleTimeValidationMessage))
            {
                return scheduleTimeValidationMessage;
            }

            if (GetCurrentScheduleRoots().Count == 0)
            {
                return LocalizationManager.Text("Settings.ScheduleRootRequired");
            }

            return LocalizationManager.Text("Settings.ScheduleReadyHint");
        }

        private string GetScheduleTimeValidationMessage()
        {
            if (string.IsNullOrWhiteSpace(_scheduleTimeText))
            {
                return LocalizationManager.Text("Settings.ScheduleTimeRequired");
            }

            return TimeSpan.TryParse(_scheduleTimeText, out _)
                ? string.Empty
                : LocalizationManager.Text("Settings.ScheduleTimeInvalid");
        }
    }
}
