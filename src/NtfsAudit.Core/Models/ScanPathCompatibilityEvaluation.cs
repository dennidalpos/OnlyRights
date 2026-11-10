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
using System.Collections.Generic;

#nullable enable

namespace NtfsAudit.Core.Models
{
    public sealed class ScanPathCompatibilityEvaluation
    {
        public PathKind EffectivePathKind { get; set; }
        public bool IsSupported { get; set; }
        public bool SupportsConfiguredCredential { get; set; }
        public bool SupportsSharePermissions { get; set; }
        public string SummaryText { get; set; } = string.Empty;
        public string DisabledOptionsText { get; set; } = string.Empty;
        public IReadOnlyList<string> DisabledOptions { get; set; } = new string[0];
        public IReadOnlyList<string> ReasonTexts { get; set; } = new string[0];
    }
}
