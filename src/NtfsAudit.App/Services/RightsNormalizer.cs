using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;

namespace NtfsAudit.App.Services
{
    public static class RightsNormalizer
    {
        public static string Normalize(FileSystemRights rights)
        {
            var set = new HashSet<string>();
            if ((rights & FileSystemRights.FullControl) == FileSystemRights.FullControl)
            {
                set.Add("FullControl");
            }
            else
            {
                if ((rights & FileSystemRights.Modify) == FileSystemRights.Modify) set.Add("Modify");
                if ((rights & FileSystemRights.ReadAndExecute) == FileSystemRights.ReadAndExecute) set.Add("ReadAndExecute");
                if ((rights & FileSystemRights.ListDirectory) == FileSystemRights.ListDirectory) set.Add("List");
                if ((rights & FileSystemRights.Read) == FileSystemRights.Read) set.Add("Read");
                if ((rights & FileSystemRights.Write) == FileSystemRights.Write) set.Add("Write");
            }

            if (set.Count == 0)
            {
                return "Custom";
            }

            return string.Join("|", set.OrderBy(x => x));
        }

        public static string CombineRights(IEnumerable<string> rights)
        {
            var set = new HashSet<string>(rights);
            return string.Join("|", set.OrderBy(x => x));
        }

        public static int Rank(string rightsSummary)
        {
            if (rightsSummary.Contains("FullControl")) return 6;
            if (rightsSummary.Contains("Modify")) return 5;
            if (rightsSummary.Contains("ReadAndExecute")) return 4;
            if (rightsSummary.Contains("List")) return 3;
            if (rightsSummary.Contains("Read")) return 2;
            if (rightsSummary.Contains("Write")) return 1;
            return 0;
        }
    }
}
