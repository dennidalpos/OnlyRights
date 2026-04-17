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
            get { return _pathCompatibility == null ? string.Empty : _pathCompatibility.SummaryText; }
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

        public IReadOnlyList<ServiceScheduleFrequencyKind> AvailableScheduleFrequencies
        {
            get
            {
                return new[]
                {
                    ServiceScheduleFrequencyKind.OneShot,
                    ServiceScheduleFrequencyKind.Daily,
                    ServiceScheduleFrequencyKind.Weekly,
                    ServiceScheduleFrequencyKind.Monthly
                };
            }
        }

        public IReadOnlyList<DayOfWeek> AvailableScheduleDays
        {
            get
            {
                return new[]
                {
                    DayOfWeek.Monday,
                    DayOfWeek.Tuesday,
                    DayOfWeek.Wednesday,
                    DayOfWeek.Thursday,
                    DayOfWeek.Friday,
                    DayOfWeek.Saturday,
                    DayOfWeek.Sunday
                };
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
                OnPropertyChanged("ScheduleTimeOfDay");
                OnPropertyChanged("ScheduleTimeText");
                RaiseScheduleEditorChanged();
            }
        }

        public string ScheduleTimeText
        {
            get { return ScheduleTimeOfDay.ToString("HH:mm"); }
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                if (TimeSpan.TryParse(value, out var parsed))
                {
                    ScheduleTimeOfDay = DateTime.Today.Add(parsed);
                }
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
                    ? "Il servizio Windows è disponibile: puoi salvare definizioni schedule in %ProgramData%."
                    : "Installa il servizio Windows per abilitare scheduling e job persistiti.";
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
                    NextRunText = nextRun.HasValue ? nextRun.Value.ToString("g") : "Nessuna prossima esecuzione",
                    RootsSummary = string.Join("; ", (definition.Template == null ? new List<string>() : definition.Template.Roots).Where(root => !string.IsNullOrWhiteSpace(root))),
                    LastMessage = status == null || string.IsNullOrWhiteSpace(status.LastMessage) ? "In attesa" : status.LastMessage,
                    StatusBadgeText = definition.IsEnabled ? "ACTIVE" : "PAUSED",
                    StatusBadgeBackground = definition.IsEnabled ? "#FF2E7D32" : "#FF607D8B"
                });
            }

            ServiceScheduleSummaryText = ServiceSchedules.Count == 0
                ? "Nessuna schedule definita."
                : string.Format("{0} schedule caricate, {1} abilitate.", ServiceSchedules.Count, ServiceSchedules.Count(item => item.Definition.IsEnabled));

            var nextScheduledRun = ServiceSchedules
                .Select(item => item.Runtime != null && item.Runtime.NextRunLocal.HasValue ? item.Runtime.NextRunLocal : _serviceSchedulePlanner.GetNextOccurrence(item.Definition, nowLocal))
                .Where(value => value.HasValue)
                .OrderBy(value => value.Value)
                .FirstOrDefault();
            ServiceNextRunText = nextScheduledRun.HasValue ? nextScheduledRun.Value.ToString("g") : "Nessuna";

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
            ProgressText = string.Format("Schedule salvata: {0}", definition.Name);
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
            ProgressText = string.Format("Schedule rimossa: {0}", removedName);
        }

        private void LoadScheduleIntoEditor(ServiceScheduleDefinition definition)
        {
            if (definition == null)
            {
                ScheduleName = "Daily audit";
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
                CompareBaseline = EnableAdvancedAudit && CompareBaseline
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

        private static string FormatScheduleFrequency(ServiceScheduleDefinition definition)
        {
            switch (definition.FrequencyKind)
            {
                case ServiceScheduleFrequencyKind.OneShot:
                    return definition.OneShotLocalDateTime.HasValue ? string.Format("One-shot {0}", definition.OneShotLocalDateTime.Value.ToString("g")) : "One-shot";
                case ServiceScheduleFrequencyKind.Daily:
                    return string.Format("Daily {0}", DateTime.Today.Add(definition.TimeOfDay).ToString("HH:mm"));
                case ServiceScheduleFrequencyKind.Weekly:
                    return string.Format("Weekly {0} {1}", definition.DayOfWeek, DateTime.Today.Add(definition.TimeOfDay).ToString("HH:mm"));
                case ServiceScheduleFrequencyKind.Monthly:
                    return string.Format("Monthly day {0} {1}", definition.DayOfMonth, DateTime.Today.Add(definition.TimeOfDay).ToString("HH:mm"));
                default:
                    return definition.FrequencyKind.ToString();
            }
        }

        private void RaiseScheduleEditorChanged()
        {
            if (SaveScheduleCommand != null)
            {
                SaveScheduleCommand.RaiseCanExecuteChanged();
            }
        }
    }
}
