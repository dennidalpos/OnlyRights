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
