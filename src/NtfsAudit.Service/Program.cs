using NtfsAudit.Core.Logging;
using NtfsAudit.Core.Export;
using NtfsAudit.Core.Cache;
using NtfsAudit.Core.Models;
using NtfsAudit.Core.Services;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NtfsAudit.Service
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            Host.CreateDefaultBuilder(args)
                .UseWindowsService()
                .ConfigureServices(services =>
                {
                    services.AddHostedService<ScanWorker>();
                })
                .Build()
                .Run();
        }
    }
}
