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
using System.ComponentModel;
using System.IO;
using System.Security.AccessControl;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    internal static class ScanExceptionClassifier
    {
        private const int LogonFailureErrorCode = 1326;

        internal static ScanExceptionDiagnostic Classify(string path, Exception ex)
        {
            var technicalDetails = ex == null ? string.Empty : ex.Message ?? string.Empty;

            if (IsLogonFailure(ex))
            {
                return new ScanExceptionDiagnostic(
                    "PathCredentialRejected",
                    string.Format("Credenziali non valide o rifiutate per il percorso: {0}", path),
                    technicalDetails,
                    false);
            }

            if (IsSecurityPrivilegeFailure(ex))
            {
                return new ScanExceptionDiagnostic(
                    "SecurityPrivilegeUnavailable",
                    string.Format("Privilegi insufficienti per leggere owner o audit del percorso: {0}. La scansione prosegue con i dati disponibili.", path),
                    technicalDetails,
                    false);
            }

            if (ex is UnauthorizedAccessException)
            {
                return new ScanExceptionDiagnostic(
                    "PathAccessDenied",
                    string.Format("Accesso negato al percorso: {0}", path),
                    technicalDetails,
                    false);
            }

            if (ex is NotSupportedException || ex is PlatformNotSupportedException)
            {
                return new ScanExceptionDiagnostic(
                    "PathUnsupported",
                    string.Format("Filesystem o provider non supportato per il percorso: {0}", path),
                    technicalDetails,
                    false);
            }

            if (ex is IOException
                || ex is InvalidOperationException
                || ex is Win32Exception
                || ex is ArgumentException)
            {
                return new ScanExceptionDiagnostic(
                    "PathUnavailable",
                    string.Format("Percorso non valido o non raggiungibile: {0}", path),
                    technicalDetails,
                    false);
            }

            return new ScanExceptionDiagnostic(
                ex == null ? "Error" : ex.GetType().Name,
                string.Format("Impossibile accedere al percorso: {0}", path),
                technicalDetails,
                false);
        }

        internal static ErrorEntry BuildErrorEntry(string path, Exception ex)
        {
            var diagnostic = Classify(path, ex);
            return new ErrorEntry
            {
                Path = path,
                ErrorType = diagnostic.ErrorType,
                Message = CombineMessage(diagnostic.Message, diagnostic.TechnicalDetails)
            };
        }

        internal static string BuildValidationMessage(string path, Exception ex)
        {
            return Classify(path, ex).Message;
        }

        private static string CombineMessage(string message, string technicalDetails)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return technicalDetails ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(technicalDetails)
                || string.Equals(message, technicalDetails, StringComparison.Ordinal))
            {
                return message;
            }

            return string.Format("{0} Dettagli: {1}", message, technicalDetails);
        }

        private static bool IsSecurityPrivilegeFailure(Exception ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (current is PrivilegeNotHeldException)
                {
                    return true;
                }

                var message = current.Message ?? string.Empty;
                if (message.IndexOf("SeSecurityPrivilege", StringComparison.OrdinalIgnoreCase) >= 0
                    || message.IndexOf("privilege which is required for this operation", StringComparison.OrdinalIgnoreCase) >= 0
                    || message.IndexOf("privilegio", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsLogonFailure(Exception ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (current is Win32Exception win32Exception && win32Exception.NativeErrorCode == LogonFailureErrorCode)
                {
                    return true;
                }

                if ((current.HResult & 0xFFFF) == LogonFailureErrorCode)
                {
                    return true;
                }

                var message = current.Message ?? string.Empty;
                if (message.IndexOf("1326", StringComparison.OrdinalIgnoreCase) >= 0
                    || message.IndexOf("logon failure", StringComparison.OrdinalIgnoreCase) >= 0
                    || message.IndexOf("unknown user name or bad password", StringComparison.OrdinalIgnoreCase) >= 0
                    || message.IndexOf("username or password is incorrect", StringComparison.OrdinalIgnoreCase) >= 0
                    || message.IndexOf("user name or password is incorrect", StringComparison.OrdinalIgnoreCase) >= 0
                    || message.IndexOf("nome utente o password non corretta", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal sealed class ScanExceptionDiagnostic
    {
        internal ScanExceptionDiagnostic(string errorType, string message, string technicalDetails, bool blocksStart)
        {
            ErrorType = string.IsNullOrWhiteSpace(errorType) ? "Error" : errorType;
            Message = message ?? string.Empty;
            TechnicalDetails = technicalDetails ?? string.Empty;
            BlocksStart = blocksStart;
        }

        internal string ErrorType { get; }
        internal string Message { get; }
        internal string TechnicalDetails { get; }
        internal bool BlocksStart { get; }
    }
}
