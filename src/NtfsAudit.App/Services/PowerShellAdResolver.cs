using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json.Linq;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    public class PowerShellAdResolver : IAdResolver
    {
        private readonly string _powershellPath;
        private readonly bool _moduleAvailable;

        public PowerShellAdResolver(string powershellPath)
        {
            _powershellPath = powershellPath;
            _moduleAvailable = CheckModule();
        }

        public bool IsAvailable { get { return _moduleAvailable; } }

        public ResolvedPrincipal ResolvePrincipal(string sid)
        {
            if (!_moduleAvailable) return null;
            var script = string.Format("Import-Module ActiveDirectory -ErrorAction SilentlyContinue; $o = Get-ADObject -Filter \"objectSid -eq '{0}'\" -Properties objectSid,samAccountName,objectClass; if ($o) {{ $sid = $null; if ($o.objectSid) {{ if ($o.objectSid -is [byte[]]) {{ $sid = (New-Object System.Security.Principal.SecurityIdentifier ($o.objectSid,0)).Value }} else {{ $sid = $o.objectSid.ToString() }} }}; $enabled = $null; if ($sid) {{ if ($o.objectClass -contains 'user') {{ $enabled = (Get-ADUser -Identity $sid -Properties Enabled).Enabled }} elseif ($o.objectClass -contains 'computer') {{ $enabled = (Get-ADComputer -Identity $sid -Properties Enabled).Enabled }} }}; [pscustomobject]@{{ Sid=$sid; Name=$o.sAMAccountName; Class=$o.objectClass; Enabled=$enabled }} | ConvertTo-Json -Compress }}", EscapePowerShellSingleQuotedString(sid));
            var output = Run(script);
            if (string.IsNullOrWhiteSpace(output)) return null;
            var obj = JObject.Parse(output);
            var cls = obj["Class"] == null ? string.Empty : obj["Class"].ToString();
            var isGroup = cls.IndexOf("group", StringComparison.OrdinalIgnoreCase) >= 0;
            var enabledToken = obj["Enabled"];
            var isDisabled = enabledToken != null && enabledToken.Type == JTokenType.Boolean && !enabledToken.Value<bool>();
            return new ResolvedPrincipal
            {
                Sid = obj["Sid"] == null ? null : obj["Sid"].ToString(),
                Name = obj["Name"] == null ? null : obj["Name"].ToString(),
                IsGroup = isGroup,
                IsDisabled = isDisabled
            };
        }

        public List<ResolvedPrincipal> GetGroupMembers(string groupSid)
        {
            var result = new List<ResolvedPrincipal>();
            if (!_moduleAvailable) return result;
            var script = string.Format("Import-Module ActiveDirectory -ErrorAction SilentlyContinue; $m = Get-ADGroupMember -Identity '{0}' -Recursive:$false | Select-Object SID,objectSid,samAccountName,objectClass; if ($m) {{ $m | ForEach-Object {{ $sidSource = $null; if ($_.SID) {{ $sidSource = $_.SID }} elseif ($_.objectSid) {{ $sidSource = $_.objectSid }}; $sid = $null; if ($sidSource) {{ if ($sidSource -is [byte[]]) {{ $sid = (New-Object System.Security.Principal.SecurityIdentifier ($sidSource,0)).Value }} else {{ $sid = $sidSource.ToString() }} }}; $enabled = $null; if ($sid) {{ if ($_.objectClass -contains 'user') {{ $enabled = (Get-ADUser -Identity $sid -Properties Enabled).Enabled }} elseif ($_.objectClass -contains 'computer') {{ $enabled = (Get-ADComputer -Identity $sid -Properties Enabled).Enabled }} }}; [pscustomobject]@{{ Sid=$sid; Name=$_.sAMAccountName; Class=$_.objectClass; Enabled=$enabled }} }} | ConvertTo-Json -Compress }}", EscapePowerShellSingleQuotedString(groupSid));
            var output = Run(script);
            if (string.IsNullOrWhiteSpace(output)) return result;
            if (output.TrimStart().StartsWith("["))
            {
                var arr = JArray.Parse(output);
                foreach (var item in arr)
                {
                    result.Add(Parse(item));
                }
                return result;
            }

            var obj = JObject.Parse(output);
            result.Add(Parse(obj));
            return result;
        }

        public List<ResolvedPrincipal> GetUserGroups(string userSid)
        {
            var result = new List<ResolvedPrincipal>();
            if (!_moduleAvailable) return result;
            var script = string.Format("Import-Module ActiveDirectory -ErrorAction SilentlyContinue; $m = Get-ADPrincipalGroupMembership -Identity '{0}' | Select-Object SID,objectSid,samAccountName,objectClass; if ($m) {{ $m | ForEach-Object {{ $sidSource = $null; if ($_.SID) {{ $sidSource = $_.SID }} elseif ($_.objectSid) {{ $sidSource = $_.objectSid }}; $sid = $null; if ($sidSource) {{ if ($sidSource -is [byte[]]) {{ $sid = (New-Object System.Security.Principal.SecurityIdentifier ($sidSource,0)).Value }} else {{ $sid = $sidSource.ToString() }} }}; $enabled = $null; if ($sid) {{ if ($_.objectClass -contains 'user') {{ $enabled = (Get-ADUser -Identity $sid -Properties Enabled).Enabled }} elseif ($_.objectClass -contains 'computer') {{ $enabled = (Get-ADComputer -Identity $sid -Properties Enabled).Enabled }} }}; [pscustomobject]@{{ Sid=$sid; Name=$_.sAMAccountName; Class=$_.objectClass; Enabled=$enabled }} }} | ConvertTo-Json -Compress }}", EscapePowerShellSingleQuotedString(userSid));
            var output = Run(script);
            if (string.IsNullOrWhiteSpace(output)) return result;
            if (output.TrimStart().StartsWith("["))
            {
                var arr = JArray.Parse(output);
                foreach (var item in arr)
                {
                    result.Add(Parse(item));
                }
                return result;
            }

            var obj = JObject.Parse(output);
            result.Add(Parse(obj));
            return result;
        }

        private ResolvedPrincipal Parse(JToken token)
        {
            var cls = token["Class"] == null ? string.Empty : token["Class"].ToString();
            var isGroup = cls.IndexOf("group", StringComparison.OrdinalIgnoreCase) >= 0;
            var enabledToken = token["Enabled"];
            var isDisabled = enabledToken != null && enabledToken.Type == JTokenType.Boolean && !enabledToken.Value<bool>();
            return new ResolvedPrincipal
            {
                Sid = token["Sid"] == null ? null : token["Sid"].ToString(),
                Name = token["Name"] == null ? null : token["Name"].ToString(),
                IsGroup = isGroup,
                IsDisabled = isDisabled
            };
        }

        private bool CheckModule()
        {
            var script = "Get-Module -ListAvailable ActiveDirectory | Select-Object -First 1 | ConvertTo-Json -Compress";
            var output = Run(script);
            return !string.IsNullOrWhiteSpace(output);
        }

        private string Run(string script)
        {
            if (!File.Exists(_powershellPath)) return null;
            var info = new ProcessStartInfo
            {
                FileName = _powershellPath,
                Arguments = string.Format("-NoProfile -NonInteractive -Command \"{0}\"", script),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var process = Process.Start(info))
            {
                if (process == null) return null;
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(15000))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                    }
                }
                if (string.IsNullOrWhiteSpace(output) && !string.IsNullOrWhiteSpace(error))
                {
                    return null;
                }
                return output.Trim();
            }
        }

        private static string EscapePowerShellSingleQuotedString(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("'", "''");
        }
    }
}
