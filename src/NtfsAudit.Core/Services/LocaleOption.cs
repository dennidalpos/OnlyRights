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
namespace NtfsAudit.App.Services
{
    public sealed class LocaleOption
    {
        public LocaleOption(string code, string displayName)
        {
            Code = code;
            DisplayName = displayName;
        }

        public string Code { get; private set; }
        public string DisplayName { get; private set; }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
