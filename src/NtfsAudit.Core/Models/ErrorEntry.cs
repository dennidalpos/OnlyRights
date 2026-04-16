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
#nullable enable

namespace NtfsAudit.App.Models
{
    public class ErrorEntry
    {
        public string? Path { get; set; }
        public string? ErrorType { get; set; }
        public string? Message { get; set; }
    }
}
