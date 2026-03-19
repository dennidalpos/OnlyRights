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
using Newtonsoft.Json;

namespace NtfsAudit.App.Models
{
    public class ScanCredential
    {
        public string UserName { get; set; }
        public string ProtectedPassword { get; set; }
        public string ProtectionScope { get; set; }

        [JsonIgnore]
        public string Password { get; set; }

        [JsonIgnore]
        public bool IsConfigured
        {
            get
            {
                return !string.IsNullOrWhiteSpace(UserName)
                    && (!string.IsNullOrWhiteSpace(Password) || !string.IsNullOrWhiteSpace(ProtectedPassword));
            }
        }

        public ScanCredential Clone()
        {
            return new ScanCredential
            {
                UserName = UserName,
                Password = Password,
                ProtectedPassword = ProtectedPassword,
                ProtectionScope = ProtectionScope
            };
        }
    }
}
