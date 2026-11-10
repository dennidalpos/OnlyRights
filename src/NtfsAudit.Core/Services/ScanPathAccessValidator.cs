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
using NtfsAudit.Core.Models;

namespace NtfsAudit.Core.Services
{
    internal static class ScanPathAccessValidator
    {
        internal static string ValidateDirectoryRoot(string rootPath, ScanCredential credential)
        {
            return ValidateDirectoryRootDetailed(rootPath, credential).Message;
        }

        internal static ScanPathValidationResult ValidateDirectoryRootDetailed(string rootPath, ScanCredential credential)
        {
            return ValidateDirectoryRootDetailed(
                rootPath,
                credential,
                path => File.GetAttributes(PathResolver.ToExtendedPath(path)));
        }

        internal static ScanPathValidationResult ValidateDirectoryRootDetailed(
            string rootPath,
            ScanCredential credential,
            Func<string, FileAttributes> attributeReader)
        {
            try
            {
                var message = WindowsImpersonationHelper.Run(
                    credential,
                    () => ValidateDirectoryRootCore(rootPath, attributeReader));
                return string.IsNullOrWhiteSpace(message)
                    ? ScanPathValidationResult.Valid()
                    : ScanPathValidationResult.Blocking(message);
            }
            catch (Exception ex)
            {
                return ScanPathValidationResult.NonBlocking(BuildValidationMessage(rootPath, ex));
            }
        }

        internal static string ValidateDirectoryRootCore(string rootPath, Func<string, FileAttributes> attributeReader)
        {
            if (attributeReader == null)
            {
                throw new ArgumentNullException("attributeReader");
            }

            var kind = PathResolver.DetectPathKind(rootPath);
            if (kind == PathKind.Unsupported)
            {
                return ScanPathCompatibilityPolicy.BuildUnsupportedRootMessage(rootPath);
            }

            var attributes = attributeReader(rootPath);
            if ((attributes & FileAttributes.Directory) == 0)
            {
                return string.Format("The selected path is a file, not a folder: {0}", rootPath);
            }

            return null;
        }

        internal static string BuildValidationMessage(string rootPath, Exception ex)
        {
            return ScanExceptionClassifier.BuildValidationMessage(rootPath, ex);
        }
    }

    internal sealed class ScanPathValidationResult
    {
        private ScanPathValidationResult(string message, bool isBlocking)
        {
            Message = message;
            IsBlocking = isBlocking;
        }

        internal string Message { get; }
        internal bool IsBlocking { get; }

        internal static ScanPathValidationResult Valid()
        {
            return new ScanPathValidationResult(null, false);
        }

        internal static ScanPathValidationResult Blocking(string message)
        {
            return new ScanPathValidationResult(message, true);
        }

        internal static ScanPathValidationResult NonBlocking(string message)
        {
            return new ScanPathValidationResult(message, false);
        }
    }
}
