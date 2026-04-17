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
using System.IO;
using NtfsAudit.App.Models;
using NtfsAudit.App.Services;
using Xunit;

namespace NtfsAudit.App.Tests
{
    public class ServiceSchedulingTests
    {
        [Fact]
        public void EvaluateDueRun_OneShot_BecomesDueOnceAndThenExpires()
        {
            var planner = new ServiceSchedulePlanner();
            var definition = new ServiceScheduleDefinition
            {
                ScheduleId = "one-shot",
                Name = "One shot",
                IsEnabled = true,
                FrequencyKind = ServiceScheduleFrequencyKind.OneShot,
                OneShotLocalDateTime = new DateTime(2026, 4, 17, 9, 0, 0)
            };

            var firstEvaluation = planner.EvaluateDueRun(definition, new DateTime(2026, 4, 17, 9, 5, 0));
            var secondEvaluation = planner.EvaluateDueRun(definition, new DateTime(2026, 4, 17, 9, 6, 0), firstEvaluation.DueRunLocal);

            Assert.True(firstEvaluation.IsDue);
            Assert.Equal(new DateTime(2026, 4, 17, 9, 0, 0), firstEvaluation.DueRunLocal);
            Assert.True(secondEvaluation.IsExpired);
            Assert.False(secondEvaluation.IsDue);
        }

        [Fact]
        public void GetNextOccurrence_ComputesDailyWeeklyAndMonthlySchedules()
        {
            var planner = new ServiceSchedulePlanner();

            var daily = planner.GetNextOccurrence(
                new ServiceScheduleDefinition
                {
                    FrequencyKind = ServiceScheduleFrequencyKind.Daily,
                    IsEnabled = true,
                    TimeOfDay = new TimeSpan(18, 30, 0)
                },
                new DateTime(2026, 4, 17, 18, 45, 0));
            var weekly = planner.GetNextOccurrence(
                new ServiceScheduleDefinition
                {
                    FrequencyKind = ServiceScheduleFrequencyKind.Weekly,
                    IsEnabled = true,
                    DayOfWeek = DayOfWeek.Monday,
                    TimeOfDay = new TimeSpan(8, 0, 0)
                },
                new DateTime(2026, 4, 17, 10, 0, 0));
            var monthly = planner.GetNextOccurrence(
                new ServiceScheduleDefinition
                {
                    FrequencyKind = ServiceScheduleFrequencyKind.Monthly,
                    IsEnabled = true,
                    DayOfMonth = 31,
                    TimeOfDay = new TimeSpan(7, 15, 0)
                },
                new DateTime(2026, 2, 1, 6, 0, 0));

            Assert.Equal(new DateTime(2026, 4, 18, 18, 30, 0), daily);
            Assert.Equal(new DateTime(2026, 4, 20, 8, 0, 0), weekly);
            Assert.Equal(new DateTime(2026, 2, 28, 7, 15, 0), monthly);
        }

        [Fact]
        public void ServiceScheduleFileStore_QuarantinesInvalidScheduleFiles()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "NtfsAudit.Tests", Guid.NewGuid().ToString("N"));
            var schedulesRoot = Path.Combine(tempRoot, "schedules");
            Directory.CreateDirectory(schedulesRoot);

            try
            {
                var invalidPath = Path.Combine(schedulesRoot, "schedule_invalid.json");
                File.WriteAllText(invalidPath, "{ invalid json");
                var store = new ServiceScheduleFileStore(schedulesRoot, Path.Combine(tempRoot, "schedule-status.json"));

                var definitions = store.LoadDefinitions();

                Assert.Empty(definitions);
                Assert.Single(Directory.GetFiles(Path.Combine(schedulesRoot, "invalid"), "schedule_invalid_*.json"));
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }
    }
}
