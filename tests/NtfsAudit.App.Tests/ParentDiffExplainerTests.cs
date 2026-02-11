using NtfsAudit.App.Models;
using NtfsAudit.App.Services;
using Xunit;

namespace NtfsAudit.App.Tests
{
    public class ParentDiffExplainerTests
    {
        private readonly ParentDiffExplainer _explainer = new ParentDiffExplainer();

        [Fact]
        public void Explain_ReturnsMorePermissive_WhenModifyIsAdded()
        {
            var parent = BuildDetail("Read", "Allow", "S-1-5-21-1", "Gruppo A");
            var child = BuildDetail("Modify", "Allow", "S-1-5-21-1", "Gruppo A");

            var result = _explainer.Explain(child, parent, false, false, false);

            Assert.Equal(FolderStatus.MorePermissive, result.Status);
            Assert.Contains(result.Reasons, x => x.Contains("ampliato accesso", System.StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Explain_ReturnsMoreRestrictive_WhenRightsAreRemoved()
        {
            var parent = BuildDetail("Modify", "Allow", "S-1-5-21-2", "Gruppo B");
            var child = new FolderDetail();

            var result = _explainer.Explain(child, parent, false, false, false);

            Assert.Equal(FolderStatus.MoreRestrictive, result.Status);
            Assert.Contains(result.Reasons, x => x.Contains("Rimosso accesso", System.StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Explain_ReturnsDenyPresent_WhenExplicitDenyExists()
        {
            var parent = BuildDetail("Read", "Allow", "S-1-5-21-3", "Gruppo C");
            var child = BuildDetail("Modify", "Deny", "S-1-5-21-3", "Gruppo C", false);

            var result = _explainer.Explain(child, parent, false, false, false);

            Assert.Equal(FolderStatus.DenyPresent, result.Status);
            Assert.Contains(result.Reasons, x => x.Contains("Deny", System.StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Explain_ReturnsBrokenInheritance_WhenInheritanceDisabledWithoutDelta()
        {
            var parent = BuildDetail("Read", "Allow", "S-1-5-21-4", "Gruppo D");
            var child = BuildDetail("Read", "Allow", "S-1-5-21-4", "Gruppo D");
            child.IsInheritanceDisabled = true;

            var result = _explainer.Explain(child, parent, false, false, false);

            Assert.Equal(FolderStatus.BrokenInheritance, result.Status);
            Assert.Contains(result.Reasons, x => x.Contains("ereditarietà", System.StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Explain_ReturnsUnknown_WhenReadError()
        {
            var result = _explainer.Explain(new FolderDetail(), new FolderDetail(), false, false, true);

            Assert.Equal(FolderStatus.Unknown, result.Status);
            Assert.Contains("Impossibile leggere", result.Reasons[0]);
        }

        private static FolderDetail BuildDetail(string rights, string allowDeny, string sid, string name, bool isInherited = false)
        {
            var detail = new FolderDetail();
            detail.AllEntries.Add(new AceEntry
            {
                PrincipalSid = sid,
                PrincipalName = name,
                AllowDeny = allowDeny,
                RightsSummary = rights,
                IsInherited = isInherited,
                PermissionLayer = PermissionLayer.Ntfs
            });
            return detail;
        }
    }
}
