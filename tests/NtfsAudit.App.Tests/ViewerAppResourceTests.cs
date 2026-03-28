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
using Xunit;

namespace NtfsAudit.App.Tests
{
    public class ViewerAppResourceTests
    {
        [Fact]
        public void SharedResourcesSource_PointsToAppSharedDictionary()
        {
            Assert.Equal(
                "/NtfsAudit.App;component/Resources/SharedResources.xaml",
                NtfsAudit.Viewer.App.SharedResourcesSource);
        }
    }
}
