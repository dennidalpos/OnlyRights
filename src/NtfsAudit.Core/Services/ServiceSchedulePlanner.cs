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
using NtfsAudit.App.Models;

#nullable enable

namespace NtfsAudit.App.Services
{
    public sealed class ServiceSchedulePlanner
    {
        public ServiceScheduleEvaluation EvaluateDueRun(
            ServiceScheduleDefinition definition,
            DateTime nowLocal,
            DateTime? lastEnqueuedRunLocal = null)
        {
            if (definition == null || !definition.IsEnabled)
            {
                return new ServiceScheduleEvaluation();
            }

            if (definition.FrequencyKind == ServiceScheduleFrequencyKind.OneShot)
            {
                var runAt = definition.OneShotLocalDateTime;
                if (!runAt.HasValue)
                {
                    return new ServiceScheduleEvaluation();
                }

                var alreadyProcessed = lastEnqueuedRunLocal.HasValue;
                return new ServiceScheduleEvaluation
                {
                    IsDue = !alreadyProcessed && runAt.Value <= nowLocal,
                    DueRunLocal = !alreadyProcessed && runAt.Value <= nowLocal ? runAt : null,
                    NextRunLocal = !alreadyProcessed && runAt.Value > nowLocal ? runAt : null,
                    IsExpired = alreadyProcessed || runAt.Value <= nowLocal
                };
            }

            var mostRecent = GetMostRecentOccurrence(definition, nowLocal);
            var nextRun = GetNextOccurrence(definition, nowLocal);
            var isDue = mostRecent.HasValue
                && mostRecent.Value <= nowLocal
                && (!lastEnqueuedRunLocal.HasValue || mostRecent.Value > lastEnqueuedRunLocal.Value);

            return new ServiceScheduleEvaluation
            {
                IsDue = isDue,
                DueRunLocal = isDue ? mostRecent : null,
                NextRunLocal = isDue ? GetNextOccurrence(definition, mostRecent.Value) : nextRun,
                IsExpired = false
            };
        }

        public DateTime? GetNextOccurrence(ServiceScheduleDefinition definition, DateTime afterLocal)
        {
            if (definition == null || !definition.IsEnabled)
            {
                return null;
            }

            switch (definition.FrequencyKind)
            {
                case ServiceScheduleFrequencyKind.OneShot:
                    return definition.OneShotLocalDateTime.HasValue && definition.OneShotLocalDateTime.Value > afterLocal
                        ? definition.OneShotLocalDateTime
                        : null;
                case ServiceScheduleFrequencyKind.Daily:
                    return GetNextDaily(definition, afterLocal);
                case ServiceScheduleFrequencyKind.Weekly:
                    return GetNextWeekly(definition, afterLocal);
                case ServiceScheduleFrequencyKind.Monthly:
                    return GetNextMonthly(definition, afterLocal);
                default:
                    return null;
            }
        }

        private static DateTime? GetMostRecentOccurrence(ServiceScheduleDefinition definition, DateTime nowLocal)
        {
            switch (definition.FrequencyKind)
            {
                case ServiceScheduleFrequencyKind.Daily:
                    return GetMostRecentDaily(definition, nowLocal);
                case ServiceScheduleFrequencyKind.Weekly:
                    return GetMostRecentWeekly(definition, nowLocal);
                case ServiceScheduleFrequencyKind.Monthly:
                    return GetMostRecentMonthly(definition, nowLocal);
                default:
                    return null;
            }
        }

        private static DateTime GetNextDaily(ServiceScheduleDefinition definition, DateTime afterLocal)
        {
            var candidate = afterLocal.Date.Add(definition.TimeOfDay);
            return candidate > afterLocal ? candidate : candidate.AddDays(1);
        }

        private static DateTime GetMostRecentDaily(ServiceScheduleDefinition definition, DateTime nowLocal)
        {
            var candidate = nowLocal.Date.Add(definition.TimeOfDay);
            return candidate <= nowLocal ? candidate : candidate.AddDays(-1);
        }

        private static DateTime? GetNextWeekly(ServiceScheduleDefinition definition, DateTime afterLocal)
        {
            if (!definition.DayOfWeek.HasValue)
            {
                return null;
            }

            for (var offset = 0; offset < 14; offset++)
            {
                var candidateDate = afterLocal.Date.AddDays(offset);
                if (candidateDate.DayOfWeek != definition.DayOfWeek.Value)
                {
                    continue;
                }

                var candidate = candidateDate.Add(definition.TimeOfDay);
                if (candidate > afterLocal)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static DateTime? GetMostRecentWeekly(ServiceScheduleDefinition definition, DateTime nowLocal)
        {
            if (!definition.DayOfWeek.HasValue)
            {
                return null;
            }

            for (var offset = 0; offset < 14; offset++)
            {
                var candidateDate = nowLocal.Date.AddDays(-offset);
                if (candidateDate.DayOfWeek != definition.DayOfWeek.Value)
                {
                    continue;
                }

                var candidate = candidateDate.Add(definition.TimeOfDay);
                if (candidate <= nowLocal)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static DateTime? GetNextMonthly(ServiceScheduleDefinition definition, DateTime afterLocal)
        {
            if (!definition.DayOfMonth.HasValue)
            {
                return null;
            }

            for (var offset = 0; offset < 24; offset++)
            {
                var month = afterLocal.Date.AddMonths(offset);
                var candidate = BuildMonthlyOccurrence(month.Year, month.Month, definition.DayOfMonth.Value, definition.TimeOfDay);
                if (candidate > afterLocal)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static DateTime? GetMostRecentMonthly(ServiceScheduleDefinition definition, DateTime nowLocal)
        {
            if (!definition.DayOfMonth.HasValue)
            {
                return null;
            }

            for (var offset = 0; offset < 24; offset++)
            {
                var month = nowLocal.Date.AddMonths(-offset);
                var candidate = BuildMonthlyOccurrence(month.Year, month.Month, definition.DayOfMonth.Value, definition.TimeOfDay);
                if (candidate <= nowLocal)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static DateTime BuildMonthlyOccurrence(int year, int month, int dayOfMonth, TimeSpan timeOfDay)
        {
            var lastDay = DateTime.DaysInMonth(year, month);
            var clampedDay = Math.Max(1, Math.Min(dayOfMonth, lastDay));
            return new DateTime(year, month, clampedDay).Add(timeOfDay);
        }
    }
}
