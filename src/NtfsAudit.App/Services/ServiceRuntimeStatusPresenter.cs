using System;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    internal sealed class ServiceRuntimeStatusPresenter
    {
        public ServiceRuntimeViewState Build(bool isServiceInstalled, bool isServiceRunning, ServiceRuntimeStatus status)
        {
            if (!isServiceInstalled)
            {
                return new ServiceRuntimeViewState
                {
                    BadgeText = "Servizio non installato",
                    BadgeBackground = "#FF9E9E9E",
                    StatusText = "Servizio: non installato",
                    IsServiceRuntimeRunning = false
                };
            }

            if (status == null)
            {
                return new ServiceRuntimeViewState
                {
                    BadgeText = isServiceRunning ? "Servizio attivo" : "Servizio installato",
                    BadgeBackground = isServiceRunning ? "#FF2E7D32" : "#FF1565C0",
                    StatusText = isServiceRunning
                        ? "Servizio: avviato (stato dettagliato non disponibile)"
                        : "Servizio: installato e fermo",
                    IsServiceRuntimeRunning = false
                };
            }

            var queuedJobs = Math.Max(0, status.PendingJobs);
            var queuedRoots = Math.Max(0, status.RemainingRootsInCurrentJob);
            var queueText = string.Format(" | code scansioni: {0}", queuedJobs + queuedRoots);

            if (status.IsRunning)
            {
                var rootLabel = string.IsNullOrWhiteSpace(status.CurrentRootPath) ? "root sconosciuta" : status.CurrentRootPath;
                var progress = status.TotalRoots > 0
                    ? string.Format("{0}/{1}", status.CurrentRootIndex, status.TotalRoots)
                    : "?/?";

                return new ServiceRuntimeViewState
                {
                    BadgeText = "Servizio attivo",
                    BadgeBackground = "#FF2E7D32",
                    StatusText = string.Format("Servizio in esecuzione: {0} (root {1}){2}", rootLabel, progress, queueText),
                    IsServiceRuntimeRunning = true
                };
            }

            return new ServiceRuntimeViewState
            {
                BadgeText = isServiceRunning ? "Servizio attivo" : "Servizio installato",
                BadgeBackground = isServiceRunning ? "#FF2E7D32" : "#FF1565C0",
                StatusText = string.IsNullOrWhiteSpace(status.LastMessage)
                    ? string.Format("Servizio: in attesa{0}", queueText)
                    : string.Format("Servizio: {0}{1}", status.LastMessage, queueText),
                IsServiceRuntimeRunning = false
            };
        }
    }

    internal sealed class ServiceRuntimeViewState
    {
        public string BadgeText { get; set; }
        public string BadgeBackground { get; set; }
        public string StatusText { get; set; }
        public bool IsServiceRuntimeRunning { get; set; }
    }
}
