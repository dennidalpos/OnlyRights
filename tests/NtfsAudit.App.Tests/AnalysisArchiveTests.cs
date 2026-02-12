using System;
using System.IO;
using System.IO.Compression;
using NtfsAudit.App.Services;
using Xunit;

namespace NtfsAudit.App.Tests
{
    public class AnalysisArchiveTests
    {
        [Fact]
        public void Import_ThrowsWhenArchivePathMissing()
        {
            var service = new AnalysisArchive();

            Assert.Throws<FileNotFoundException>(() => service.Import(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".ntaudit")));
        }

        [Fact]
        public void Import_ThrowsForArchiveWithoutDataEntry()
        {
            var service = new AnalysisArchive();
            var archivePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".ntaudit");

            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            {
                archive.CreateEntry("meta.json");
            }

            try
            {
                Assert.Throws<InvalidDataException>(() => service.Import(archivePath));
            }
            finally
            {
                if (File.Exists(archivePath))
                {
                    File.Delete(archivePath);
                }
            }
        }
    }
}
