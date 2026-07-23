using NtfsAudit.Core.Logging;
using NtfsAudit.Core.Export;
using NtfsAudit.Core.Cache;
using NtfsAudit.Core.Models;
using NtfsAudit.Core.Services;
namespace NtfsAudit.App.Services
{
    public sealed class SelectionOption<T>
    {
        public SelectionOption(T value, string displayName)
        {
            Value = value;
            DisplayName = displayName;
        }

        public T Value { get; private set; }
        public string DisplayName { get; private set; }
    }
}
