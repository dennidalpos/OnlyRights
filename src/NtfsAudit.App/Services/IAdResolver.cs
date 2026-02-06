using System.Collections.Generic;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    public interface IAdResolver
    {
        bool IsAvailable { get; }
        ResolvedPrincipal ResolvePrincipal(string sid);
        List<ResolvedPrincipal> GetGroupMembers(string groupSid);
        List<ResolvedPrincipal> GetUserGroups(string userSid);
    }
}
