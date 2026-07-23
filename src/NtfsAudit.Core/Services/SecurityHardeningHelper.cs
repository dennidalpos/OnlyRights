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
using System.Security.AccessControl;
using System.Security.Principal;

namespace NtfsAudit.Core.Services
{
    public static class SecurityHardeningHelper
    {
        public static void SecureDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return;
            }

            try
            {
                var directoryInfo = new DirectoryInfo(path);
                var directorySecurity = new DirectorySecurity();

                // Remove inherited rules and prevent inheritance propagation
                directorySecurity.SetAccessRuleProtection(true, false);

                // Add SYSTEM
                directorySecurity.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));

                // Add BUILTIN\Administrators
                directorySecurity.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));

                // Add Current User
                var currentUser = WindowsIdentity.GetCurrent().User;
                if (currentUser != null)
                {
                    directorySecurity.AddAccessRule(new FileSystemAccessRule(
                        currentUser,
                        FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None,
                        AccessControlType.Allow));
                }

                directoryInfo.SetAccessControl(directorySecurity);
            }
            catch
            {
                // Soft fail: do not crash if OS does not support or user has insufficient rights
            }
        }

        public static void SecureFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                var fileInfo = new FileInfo(path);
                var fileSecurity = new FileSecurity();

                // Remove inherited rules and prevent inheritance propagation
                fileSecurity.SetAccessRuleProtection(true, false);

                // Add SYSTEM
                fileSecurity.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                    FileSystemRights.FullControl,
                    AccessControlType.Allow));

                // Add BUILTIN\Administrators
                fileSecurity.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                    FileSystemRights.FullControl,
                    AccessControlType.Allow));

                // Add Current User
                var currentUser = WindowsIdentity.GetCurrent().User;
                if (currentUser != null)
                {
                    fileSecurity.AddAccessRule(new FileSystemAccessRule(
                        currentUser,
                        FileSystemRights.FullControl,
                        AccessControlType.Allow));
                }

                fileInfo.SetAccessControl(fileSecurity);
            }
            catch
            {
                // Soft fail: do not crash if OS does not support or user has insufficient rights
            }
        }
    }
}
