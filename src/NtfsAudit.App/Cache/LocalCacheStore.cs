using System;
using System.IO;

namespace NtfsAudit.App.Cache
{
    public class LocalCacheStore
    {
        public string GetCacheFilePath(string fileName)
        {
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var localPath = Path.Combine(exeDir, fileName);
            if (IsWritableDirectory(exeDir))
            {
                return localPath;
            }

            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NtfsAudit", "Cache");
            Directory.CreateDirectory(appData);
            return Path.Combine(appData, fileName);
        }

        private bool IsWritableDirectory(string directory)
        {
            try
            {
                var testPath = Path.Combine(directory, string.Format(".write_{0}", Guid.NewGuid().ToString("N")));
                File.WriteAllText(testPath, "x");
                File.Delete(testPath);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
