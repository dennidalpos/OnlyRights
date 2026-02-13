using System.Collections.Generic;
using System.Threading;
using NtfsAudit.App.Cache;
using NtfsAudit.App.Models;
using NtfsAudit.App.Services;
using Xunit;

namespace NtfsAudit.App.Tests
{
    public class GroupExpansionServiceTests
    {
        [Fact]
        public void ExpandGroup_ReturnsEmptyList_WhenResolverReturnsNullMembers()
        {
            var resolver = new FakeAdResolver
            {
                IsAvailableValue = true,
                GroupMembers = null
            };
            var cache = new GroupMembershipCache(10);
            var service = new GroupExpansionService(resolver, cache);

            var result = service.ExpandGroup("S-1-5-32-544", CancellationToken.None);

            Assert.NotNull(result);
            Assert.Empty(result);
        }

        private class FakeAdResolver : IAdResolver
        {
            public bool IsAvailableValue { get; set; }
            public List<ResolvedPrincipal> GroupMembers { get; set; } = new List<ResolvedPrincipal>();

            public bool IsAvailable => IsAvailableValue;

            public ResolvedPrincipal ResolvePrincipal(string sid)
            {
                return null;
            }

            public List<ResolvedPrincipal> GetGroupMembers(string groupSid)
            {
                return GroupMembers;
            }

            public List<ResolvedPrincipal> GetUserGroups(string userSid)
            {
                return new List<ResolvedPrincipal>();
            }
        }
    }
}
