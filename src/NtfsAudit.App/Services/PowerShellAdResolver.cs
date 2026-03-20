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
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    public class PowerShellAdResolver : IAdResolver
    {
        private readonly string _powershellPath;
        private readonly bool _moduleAvailable;
        private readonly ScanCredential _credential;
        private readonly Func<string, string> _scriptRunner;
        private readonly Func<string, bool> _fileExists;

        public PowerShellAdResolver(string powershellPath)
            : this(powershellPath, null)
        {
        }

        public PowerShellAdResolver(string powershellPath, ScanCredential credential)
            : this(powershellPath, credential, null, null)
        {
        }

        internal PowerShellAdResolver(string powershellPath, ScanCredential credential, Func<string, string> scriptRunner, Func<string, bool> fileExists)
        {
            _powershellPath = powershellPath;
            _credential = credential;
            _scriptRunner = scriptRunner;
            _fileExists = fileExists ?? File.Exists;
            _moduleAvailable = CheckModule();
        }

        public bool IsAvailable { get { return _moduleAvailable; } }

        public ResolvedPrincipal ResolvePrincipal(string sid)
        {
            if (!_moduleAvailable) return null;
            var script = string.Format(
                "{0}Import-Module ActiveDirectory -ErrorAction SilentlyContinue; $o = Get-ADObject -Filter \"objectSid -eq '{1}'\" -Properties objectSid,samAccountName,objectClass {2}; if ($o) {{ $sid = $null; if ($o.objectSid) {{ if ($o.objectSid -is [byte[]]) {{ $sid = (New-Object System.Security.Principal.SecurityIdentifier ($o.objectSid,0)).Value }} else {{ $sid = $o.objectSid.ToString() }} }}; $enabled = $null; if ($sid) {{ if ($o.objectClass -contains 'user') {{ $enabled = (Get-ADUser -Identity $sid -Properties Enabled {2}).Enabled }} elseif ($o.objectClass -contains 'computer') {{ $enabled = (Get-ADComputer -Identity $sid -Properties Enabled {2}).Enabled }} }}; [pscustomobject]@{{ Sid=$sid; Name=$o.sAMAccountName; Class=$o.objectClass; Enabled=$enabled }} | ConvertTo-Json -Compress }}",
                BuildCredentialBootstrap(),
                sid,
                BuildCredentialParameter());
            var output = Run(script);
            if (!TryParseJsonToken(output, out var token) || token.Type != JTokenType.Object) return null;
            var obj = (JObject)token;
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
            var credentialParameter = BuildCredentialParameter();
            var script = string.Format(
                "{0}Import-Module ActiveDirectory -ErrorAction SilentlyContinue; $m = Get-ADGroupMember -Identity '{1}' -Recursive:$false {2} | Select-Object SID,objectSid,samAccountName,objectClass; if ($m) {{ $m | ForEach-Object {{ $sidSource = $null; if ($_.SID) {{ $sidSource = $_.SID }} elseif ($_.objectSid) {{ $sidSource = $_.objectSid }}; $sid = $null; if ($sidSource) {{ if ($sidSource -is [byte[]]) {{ $sid = (New-Object System.Security.Principal.SecurityIdentifier ($sidSource,0)).Value }} else {{ $sid = $sidSource.ToString() }} }}; $enabled = $null; if ($sid) {{ if ($_.objectClass -contains 'user') {{ $enabled = (Get-ADUser -Identity $sid -Properties Enabled {2}).Enabled }} elseif ($_.objectClass -contains 'computer') {{ $enabled = (Get-ADComputer -Identity $sid -Properties Enabled {2}).Enabled }} }}; [pscustomobject]@{{ Sid=$sid; Name=$_.sAMAccountName; Class=$_.objectClass; Enabled=$enabled }} }} | ConvertTo-Json -Compress }}",
                BuildCredentialBootstrap(),
                groupSid,
                credentialParameter);
            var output = Run(script);
            if (!TryParseJsonToken(output, out var token)) return result;
            if (token.Type == JTokenType.Array)
            {
                foreach (var item in (JArray)token)
                {
                    result.Add(Parse(item));
                }
                return result;
            }

            if (token.Type == JTokenType.Object)
            {
                result.Add(Parse(token));
            }
            return result;
        }

        public List<ResolvedPrincipal> GetUserGroups(string userSid)
        {
            var result = new List<ResolvedPrincipal>();
            if (!_moduleAvailable) return result;
            var credentialParameter = BuildCredentialParameter();
            var script = string.Format(
                "{0}Import-Module ActiveDirectory -ErrorAction SilentlyContinue; $m = Get-ADPrincipalGroupMembership -Identity '{1}' {2} | Select-Object SID,objectSid,samAccountName,objectClass; if ($m) {{ $m | ForEach-Object {{ $sidSource = $null; if ($_.SID) {{ $sidSource = $_.SID }} elseif ($_.objectSid) {{ $sidSource = $_.objectSid }}; $sid = $null; if ($sidSource) {{ if ($sidSource -is [byte[]]) {{ $sid = (New-Object System.Security.Principal.SecurityIdentifier ($sidSource,0)).Value }} else {{ $sid = $sidSource.ToString() }} }}; $enabled = $null; if ($sid) {{ if ($_.objectClass -contains 'user') {{ $enabled = (Get-ADUser -Identity $sid -Properties Enabled {2}).Enabled }} elseif ($_.objectClass -contains 'computer') {{ $enabled = (Get-ADComputer -Identity $sid -Properties Enabled {2}).Enabled }} }}; [pscustomobject]@{{ Sid=$sid; Name=$_.sAMAccountName; Class=$_.objectClass; Enabled=$enabled }} }} | ConvertTo-Json -Compress }}",
                BuildCredentialBootstrap(),
                userSid,
                credentialParameter);
            var output = Run(script);
            if (!TryParseJsonToken(output, out var token)) return result;
            if (token.Type == JTokenType.Array)
            {
                foreach (var item in (JArray)token)
                {
                    result.Add(Parse(item));
                }
                return result;
            }

            if (token.Type == JTokenType.Object)
            {
                result.Add(Parse(token));
            }
            return result;
        }

        private bool TryParseJsonToken(string output, out JToken token)
        {
            token = null;
            if (string.IsNullOrWhiteSpace(output))
            {
                return false;
            }

            var trimmed = output.Trim();
            if (!(trimmed.StartsWith("{") || trimmed.StartsWith("[")))
            {
                return false;
            }

            try
            {
                token = JToken.Parse(trimmed);
                return true;
            }
            catch
            {
                return false;
            }
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
            if (_scriptRunner != null)
            {
                return _scriptRunner(script);
            }

            if (!_fileExists(_powershellPath)) return null;
            var info = new ProcessStartInfo
            {
                FileName = _powershellPath,
                Arguments = string.Format("-NoProfile -NonInteractive -EncodedCommand {0}", EncodeScript(script)),
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
                if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(output))
                {
                    return null;
                }
                if (string.IsNullOrWhiteSpace(output) && !string.IsNullOrWhiteSpace(error))
                {
                    return null;
                }
                return output.Trim();
            }
        }

        private string BuildCredentialBootstrap()
        {
            if (_credential == null || !_credential.IsConfigured)
            {
                return string.Empty;
            }

            var escapedUserName = EscapePowerShellString(_credential.UserName);
            var escapedPassword = EscapePowerShellString(_credential.Password);
            return string.Format(
                "$securePassword = ConvertTo-SecureString '{0}' -AsPlainText -Force; $credential = New-Object System.Management.Automation.PSCredential('{1}', $securePassword); ",
                escapedPassword,
                escapedUserName);
        }

        private string BuildCredentialParameter()
        {
            return _credential != null && _credential.IsConfigured ? "-Credential $credential" : string.Empty;
        }

        private static string EncodeScript(string script)
        {
            var bytes = Encoding.Unicode.GetBytes(script ?? string.Empty);
            return Convert.ToBase64String(bytes);
        }

        private static string EscapePowerShellString(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("'", "''");
        }
    }
}
