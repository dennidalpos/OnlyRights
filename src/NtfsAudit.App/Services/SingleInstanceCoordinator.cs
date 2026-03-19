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
using System.Threading;

namespace NtfsAudit.App.Services
{
    internal static class SingleInstanceCoordinator
    {
        internal static bool TryAcquire(string mutexName, out Mutex mutex)
        {
            mutex = null;

            try
            {
                var createdNew = false;
                var candidate = new Mutex(true, mutexName, out createdNew);
                if (!createdNew)
                {
                    candidate.Dispose();
                    return false;
                }

                mutex = candidate;
                return true;
            }
            catch
            {
                return true;
            }
        }
    }
}
