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

namespace NtfsAudit.Core.Services
{
    internal static class SingleInstanceCoordinator
    {
        internal static SingleInstanceAcquireResult TryAcquire(string mutexName, out Mutex mutex)
        {
            return TryAcquire(mutexName, out mutex, CreateMutex);
        }

        internal static SingleInstanceAcquireResult TryAcquire(string mutexName, out Mutex mutex, Func<string, MutexFactoryResult> mutexFactory)
        {
            mutex = null;

            try
            {
                var attempt = (mutexFactory ?? CreateMutex)(mutexName);
                if (attempt.Mutex == null)
                {
                    return SingleInstanceAcquireResult.Failure("Il mutex dell'istanza unica non è stato creato.");
                }

                if (!attempt.CreatedNew)
                {
                    attempt.Mutex.Dispose();
                    return SingleInstanceAcquireResult.AlreadyRunning();
                }

                mutex = attempt.Mutex;
                return SingleInstanceAcquireResult.Acquired();
            }
            catch (Exception ex)
            {
                return SingleInstanceAcquireResult.Failure(ex.Message);
            }
        }

        private static MutexFactoryResult CreateMutex(string mutexName)
        {
            var createdNew = false;
            var candidate = new Mutex(true, mutexName, out createdNew);
            return new MutexFactoryResult(candidate, createdNew);
        }

        internal readonly struct MutexFactoryResult
        {
            internal MutexFactoryResult(Mutex mutex, bool createdNew)
            {
                Mutex = mutex;
                CreatedNew = createdNew;
            }

            internal Mutex Mutex { get; }
            internal bool CreatedNew { get; }
        }

        internal readonly struct SingleInstanceAcquireResult
        {
            private SingleInstanceAcquireResult(bool isAcquired, bool isAlreadyRunning, string errorMessage)
            {
                IsAcquired = isAcquired;
                IsAlreadyRunning = isAlreadyRunning;
                ErrorMessage = errorMessage;
            }

            internal bool IsAcquired { get; }
            internal bool IsAlreadyRunning { get; }
            internal string ErrorMessage { get; }

            internal static SingleInstanceAcquireResult Acquired()
            {
                return new SingleInstanceAcquireResult(true, false, null);
            }

            internal static SingleInstanceAcquireResult AlreadyRunning()
            {
                return new SingleInstanceAcquireResult(false, true, null);
            }

            internal static SingleInstanceAcquireResult Failure(string errorMessage)
            {
                return new SingleInstanceAcquireResult(false, false, errorMessage);
            }
        }
    }
}
