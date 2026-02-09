using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using NtfsAudit.App.Models;
using NtfsAudit.App.Services;

namespace NtfsAudit.App.Export
{
    public class HtmlExporter
    {
        public void Export(
            ScanResult result,
            string rootPath,
            string selectedFolderPath,
            bool colorizeRights,
            string aclFilter,
            bool filterEveryone,
            bool filterAuthenticatedUsers,
            bool filterDenyOnly,
            bool filterInheritanceDisabled,
            IEnumerable<string> expandedPaths,
            IEnumerable<ErrorEntry> errors,
            string outputPath)
        {
            if (result == null) throw new ArgumentNullException("result");
            if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("Percorso output non valido.");

            var ioOutputPath = PathResolver.ToExtendedPath(outputPath);
            var outputDir = Path.GetDirectoryName(ioOutputPath);
            if (!string.IsNullOrWhiteSpace(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            var treeMap = result.TreeMap ?? new Dictionary<string, List<string>>();
            var root = ResolveRootPath(rootPath, treeMap);
            var selectedPath = string.IsNullOrWhiteSpace(selectedFolderPath) ? root : selectedFolderPath;
            var details = result.Details ?? new Dictionary<string, FolderDetail>();
            var expanded = expandedPaths == null ? new List<string>() : expandedPaths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var errorPayload = errors == null ? new List<ErrorEntry>() : errors.ToList();

            var jsonSettings = new JsonSerializerSettings
            {
                StringEscapeHandling = StringEscapeHandling.EscapeHtml,
                NullValueHandling = NullValueHandling.Include
            };

            var serializer = JsonSerializer.Create(jsonSettings);

            using (var writer = new StreamWriter(ioOutputPath, false, Encoding.UTF8))
            {
                var builder = new HtmlLineWriter(writer, serializer);
                builder.AppendLine("<!DOCTYPE html>");
                builder.AppendLine("<html lang=\"it\">");
                builder.AppendLine("<head>");
                builder.AppendLine("  <meta charset=\"utf-8\" />");
                builder.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
                builder.AppendLine("  <title>NTFS Audit - Export HTML</title>");
                builder.AppendLine("  <style>");
                builder.AppendLine("    body { font-family: 'Segoe UI', Arial, sans-serif; margin: 0; color: #212121; }");
                builder.AppendLine("    header { padding: 16px; border-bottom: 1px solid #ddd; background: #fafafa; }");
                builder.AppendLine("    header h1 { margin: 0 0 8px 0; font-size: 20px; }");
                builder.AppendLine("    .meta { display: flex; flex-wrap: wrap; gap: 12px; font-size: 13px; }");
                builder.AppendLine("    .meta span { background: #e3f2fd; padding: 4px 8px; border-radius: 4px; }");
                builder.AppendLine("    .layout { display: flex; height: calc(100vh - 92px); }");
                builder.AppendLine("    .sidebar { width: 320px; border-right: 1px solid #ddd; padding: 12px; overflow: auto; }");
                builder.AppendLine("    .content { flex: 1; padding: 12px; overflow: auto; }");
                builder.AppendLine("    .tree-actions { display: flex; gap: 8px; margin-bottom: 8px; }");
                builder.AppendLine("    button { background: #1565c0; color: white; border: none; border-radius: 4px; padding: 6px 12px; cursor: pointer; }");
                builder.AppendLine("    button.secondary { background: #616161; }");
                builder.AppendLine("    button:disabled { background: #b0bec5; cursor: not-allowed; }");
                builder.AppendLine("    ul.tree { list-style: none; padding-left: 16px; }");
                builder.AppendLine("    .tree-item { margin: 2px 0; }");
                builder.AppendLine("    .tree-label { cursor: pointer; padding: 2px 6px; border-radius: 4px; display: inline-flex; align-items: center; gap: 6px; }");
                builder.AppendLine("    .tree-label.selected { background: #bbdefb; color: #0d47a1; font-weight: 600; }");
                builder.AppendLine("    .tree-toggle { width: 16px; height: 16px; display: inline-flex; align-items: center; justify-content: center; border-radius: 2px; background: #e0e0e0; font-size: 10px; }");
                builder.AppendLine("    .tree-children.collapsed { display: none; }");
                builder.AppendLine("    .tree-marker { width: 8px; height: 8px; display: inline-block; }");
                builder.AppendLine("    .marker-inheritance { background: #c62828; border-radius: 2px; }");
                builder.AppendLine("    .marker-explicit { background: #ffa000; border-radius: 50%; }");
                builder.AppendLine("    .badge { display: inline-flex; align-items: center; justify-content: center; padding: 1px 4px; border-radius: 2px; font-size: 10px; color: #fff; }");
                builder.AppendLine("    .badge-protected { background: #c62828; }");
                builder.AppendLine("    .badge-added { background: #2e7d32; }");
                builder.AppendLine("    .badge-removed { background: #f57c00; }");
                builder.AppendLine("    .badge-deny { background: #6a1b9a; }");
                builder.AppendLine("    .badge-baseline-added { background: #455a64; }");
                builder.AppendLine("    .badge-baseline-removed { background: #78909c; }");
                builder.AppendLine("    .badge-ntfs { background: #00897b; }");
                builder.AppendLine("    .badge-share { background: #5e35b1; }");
                builder.AppendLine("    .legend { display: grid; grid-auto-flow: row; grid-auto-rows: max-content; gap: 6px; font-size: 12px; margin-bottom: 8px; align-items: start; }");
                builder.AppendLine("    .legend-title { font-weight: 600; }");
                builder.AppendLine("    .legend-item { display: inline-flex; align-items: center; gap: 6px; white-space: nowrap; }");
                builder.AppendLine("    .toolbar { display: flex; flex-wrap: wrap; gap: 16px; align-items: center; margin-bottom: 12px; }");
                builder.AppendLine("    .tabs { display: flex; gap: 8px; margin-bottom: 12px; }");
                builder.AppendLine("    .tab-button { background: #e0e0e0; color: #212121; }");
                builder.AppendLine("    .tab-button.active { background: #1565c0; color: white; }");
                builder.AppendLine("    .tab-panel { display: none; }");
                builder.AppendLine("    .tab-panel.active { display: block; }");
                builder.AppendLine("    table { width: 100%; border-collapse: collapse; font-size: 12px; }");
                builder.AppendLine("    th, td { border: 1px solid #ddd; padding: 6px; text-align: left; }");
                builder.AppendLine("    th { background: #f5f5f5; position: sticky; top: 0; z-index: 1; }");
                builder.AppendLine("    tr.row-disabled { text-decoration: line-through; color: #9e9e9e; }");
                builder.AppendLine("    tr.rights-read { background: #e3f2fd; }");
                builder.AppendLine("    tr.rights-list { background: #e8f5e9; }");
                builder.AppendLine("    tr.rights-readexecute { background: #e0f7fa; }");
                builder.AppendLine("    tr.rights-write { background: #fff9c4; }");
                builder.AppendLine("    tr.rights-modify { background: #ffe0b2; }");
                builder.AppendLine("    tr.rights-full { background: #ffcdd2; }");
                builder.AppendLine("    .filter-input { padding: 6px; width: 280px; }");
                builder.AppendLine("  </style>");
                builder.AppendLine("</head>");
                builder.AppendLine("<body>");
                builder.AppendLine("  <header>");
                builder.AppendLine("    <h1>NTFS Audit - Esportazione HTML</h1>");
                builder.AppendLine(string.Format("    <div class=\"meta\"><span>Root: {0}</span><span>Selezione: <strong id=\"selectedPath\"></strong></span></div>", WebUtility.HtmlEncode(root)));
                builder.AppendLine("  </header>");
                builder.AppendLine("  <div class=\"layout\">");
                builder.AppendLine("    <aside class=\"sidebar\">");
                builder.AppendLine("      <div class=\"tree-actions\">");
                builder.AppendLine("        <button id=\"expandAll\">Espandi</button>");
                builder.AppendLine("        <button id=\"collapseAll\" class=\"secondary\">Comprimi</button>");
                builder.AppendLine("      </div>");
                builder.AppendLine("      <div class=\"legend\">");
                builder.AppendLine("        <div class=\"legend-title\">Legenda:</div>");
                builder.AppendLine("        <div class=\"legend-item\"><span class=\"badge badge-protected\">P</span>Protetto (ereditarietà disabilitata)</div>");
                builder.AppendLine("        <div class=\"legend-item\"><span class=\"badge badge-added\">+N</span>ACE esplicite aggiunte</div>");
                builder.AppendLine("        <div class=\"legend-item\"><span class=\"badge badge-removed\">-N</span>ACE rimosse rispetto al padre</div>");
                builder.AppendLine("        <div class=\"legend-item\"><span class=\"badge badge-deny\">D</span>Deny espliciti</div>");
                builder.AppendLine("        <div class=\"legend-item\"><span class=\"badge badge-baseline-added\">B+N</span>Aggiunte vs baseline</div>");
                builder.AppendLine("        <div class=\"legend-item\"><span class=\"badge badge-baseline-removed\">B-N</span>Rimozioni vs baseline</div>");
                builder.AppendLine("        <div class=\"legend-item\"><span class=\"badge badge-ntfs\">N</span>ACE NTFS esplicite</div>");
                builder.AppendLine("        <div class=\"legend-item\"><span class=\"badge badge-share\">S</span>Permessi Share presenti</div>");
                builder.AppendLine("      </div>");
                builder.AppendLine("      <div id=\"treeContainer\"></div>");
                builder.AppendLine("    </aside>");
                builder.AppendLine("    <main class=\"content\">");
                builder.AppendLine("      <div class=\"toolbar\">");
                builder.AppendLine("        <label><input type=\"checkbox\" id=\"colorizeToggle\" /> Colora per diritto</label>");
                builder.AppendLine("        <input id=\"aclFilter\" class=\"filter-input\" type=\"text\" placeholder=\"Filtra ACL (utente, SID, allow/deny, diritti)...\" />");
                builder.AppendLine("        <label><input type=\"checkbox\" id=\"filterEveryone\" /> Everyone</label>");
                builder.AppendLine("        <label><input type=\"checkbox\" id=\"filterAuthUsers\" /> Authenticated Users</label>");
                builder.AppendLine("        <label><input type=\"checkbox\" id=\"filterDeny\" /> Solo Deny</label>");
                builder.AppendLine("        <label><input type=\"checkbox\" id=\"filterInheritance\" /> Ereditarietà disabilitata</label>");
                builder.AppendLine("      </div>");
                builder.AppendLine("      <div class=\"tabs\">");
                builder.AppendLine("        <button class=\"tab-button active\" data-tab=\"groups\">Permessi Gruppi</button>");
                builder.AppendLine("        <button class=\"tab-button\" data-tab=\"users\">Permessi Utenti</button>");
                builder.AppendLine("        <button class=\"tab-button\" data-tab=\"acl\">Dettaglio ACL</button>");
                builder.AppendLine("        <button class=\"tab-button\" data-tab=\"share\">Share</button>");
                builder.AppendLine("        <button class=\"tab-button\" data-tab=\"effective\">Effective Access</button>");
                builder.AppendLine("        <button class=\"tab-button\" data-tab=\"errors\">Errori</button>");
                builder.AppendLine("      </div>");
                builder.AppendLine("      <section id=\"tab-groups\" class=\"tab-panel active\">");
                builder.AppendLine("        <table id=\"groupsTable\"></table>");
                builder.AppendLine("      </section>");
                builder.AppendLine("      <section id=\"tab-users\" class=\"tab-panel\">");
                builder.AppendLine("        <table id=\"usersTable\"></table>");
                builder.AppendLine("      </section>");
                builder.AppendLine("      <section id=\"tab-acl\" class=\"tab-panel\">");
                builder.AppendLine("        <table id=\"aclTable\"></table>");
                builder.AppendLine("      </section>");
                builder.AppendLine("      <section id=\"tab-share\" class=\"tab-panel\">");
                builder.AppendLine("        <table id=\"shareTable\"></table>");
                builder.AppendLine("      </section>");
                builder.AppendLine("      <section id=\"tab-effective\" class=\"tab-panel\">");
                builder.AppendLine("        <table id=\"effectiveTable\"></table>");
                builder.AppendLine("      </section>");
                builder.AppendLine("      <section id=\"tab-errors\" class=\"tab-panel\">");
                builder.AppendLine("        <table id=\"errorsTable\"></table>");
                builder.AppendLine("      </section>");
                builder.AppendLine("    </main>");
                builder.AppendLine("  </div>");
                builder.AppendLine("  <script>");
                builder.AppendJsonLine("    const treeMap = ", treeMap, ";");
                builder.AppendDetailsLine("    const detailsData = ", details, ";");
                builder.AppendJsonLine("    const expandedPaths = new Set(", expanded, ");");
                builder.AppendJsonLine("    const errorsData = ", errorPayload, ";");
                builder.AppendJsonLine("    const rootPath = ", root, ";");
                builder.AppendJsonLine("    let selectedPath = ", selectedPath, ";");
                builder.AppendLine(string.Format("    let colorizeRights = {0};", colorizeRights.ToString().ToLowerInvariant()));
                builder.AppendJsonLine("    const initialAclFilter = ", aclFilter ?? string.Empty, ";");
                builder.AppendLine(string.Format("    let filterEveryone = {0};", filterEveryone.ToString().ToLowerInvariant()));
                builder.AppendLine(string.Format("    let filterAuthenticatedUsers = {0};", filterAuthenticatedUsers.ToString().ToLowerInvariant()));
                builder.AppendLine(string.Format("    let filterDenyOnly = {0};", filterDenyOnly.ToString().ToLowerInvariant()));
                builder.AppendLine(string.Format("    let filterInheritanceDisabled = {0};", filterInheritanceDisabled.ToString().ToLowerInvariant()));
                builder.AppendLine("\n    const groupColumns = [");
                builder.AppendLine("      { key: 'ResourceType', label: 'Risorsa' },");
                builder.AppendLine("      { key: 'PrincipalName', label: 'Gruppo' },");
                builder.AppendLine("      { key: 'PrincipalSid', label: 'SID' },");
                builder.AppendLine("      { key: 'AllowDeny', label: 'Allow/Deny' },");
                builder.AppendLine("      { key: 'RightsSummary', label: 'Diritti' },");
                builder.AppendLine("      { key: 'EffectiveRightsSummary', label: 'Effective Access' },");
                builder.AppendLine("      { key: 'RiskLevel', label: 'Rischio' },");
                builder.AppendLine("      { key: 'Owner', label: 'Owner' },");
                builder.AppendLine("      { key: 'AuditSummary', label: 'SACL/Audit' },");
                builder.AppendLine("      { key: 'Depth', label: 'Depth' },");
                builder.AppendLine("      { key: 'IsDisabled', label: 'Disabilitato', type: 'bool' },");
                builder.AppendLine("      { key: 'HasFullControl', label: 'Full', type: 'bool' },");
                builder.AppendLine("      { key: 'HasModify', label: 'Modify', type: 'bool' },");
                builder.AppendLine("      { key: 'HasReadAndExecute', label: 'R&E', type: 'bool' },");
                builder.AppendLine("      { key: 'HasList', label: 'List', type: 'bool' },");
                builder.AppendLine("      { key: 'HasRead', label: 'Read', type: 'bool' },");
                builder.AppendLine("      { key: 'HasWrite', label: 'Write', type: 'bool' },");
                builder.AppendLine("      { key: 'IsInherited', label: 'Inherited', type: 'bool' },");
                builder.AppendLine("      { key: 'InheritanceFlags', label: 'Inheritance' },");
                builder.AppendLine("      { key: 'PropagationFlags', label: 'Propagation' }");
                builder.AppendLine("    ];");
                builder.AppendLine("    const userColumns = [");
                builder.AppendLine("      { key: 'ResourceType', label: 'Risorsa' },");
                builder.AppendLine("      { key: 'PrincipalName', label: 'Utente' },");
                builder.AppendLine("      { key: 'PrincipalSid', label: 'SID' },");
                builder.AppendLine("      { key: 'AllowDeny', label: 'Allow/Deny' },");
                builder.AppendLine("      { key: 'RightsSummary', label: 'Diritti' },");
                builder.AppendLine("      { key: 'EffectiveRightsSummary', label: 'Effective Access' },");
                builder.AppendLine("      { key: 'RiskLevel', label: 'Rischio' },");
                builder.AppendLine("      { key: 'Owner', label: 'Owner' },");
                builder.AppendLine("      { key: 'AuditSummary', label: 'SACL/Audit' },");
                builder.AppendLine("      { key: 'Depth', label: 'Depth' },");
                builder.AppendLine("      { key: 'IsDisabled', label: 'Disabilitato', type: 'bool' },");
                builder.AppendLine("      { key: 'HasFullControl', label: 'Full', type: 'bool' },");
                builder.AppendLine("      { key: 'HasModify', label: 'Modify', type: 'bool' },");
                builder.AppendLine("      { key: 'HasReadAndExecute', label: 'R&E', type: 'bool' },");
                builder.AppendLine("      { key: 'HasList', label: 'List', type: 'bool' },");
                builder.AppendLine("      { key: 'HasRead', label: 'Read', type: 'bool' },");
                builder.AppendLine("      { key: 'HasWrite', label: 'Write', type: 'bool' },");
                builder.AppendLine("      { key: 'IsInherited', label: 'Inherited', type: 'bool' },");
                builder.AppendLine("      { key: 'InheritanceFlags', label: 'Inheritance' },");
                builder.AppendLine("      { key: 'PropagationFlags', label: 'Propagation' }");
                builder.AppendLine("    ];");
                builder.AppendLine("    const aclColumns = [");
                builder.AppendLine("      { key: 'ResourceType', label: 'Risorsa' },");
                builder.AppendLine("      { key: 'PrincipalName', label: 'Principal' },");
                builder.AppendLine("      { key: 'PrincipalSid', label: 'SID' },");
                builder.AppendLine("      { key: 'PrincipalType', label: 'Tipo' },");
                builder.AppendLine("      { key: 'AllowDeny', label: 'Allow/Deny' },");
                builder.AppendLine("      { key: 'RightsSummary', label: 'Diritti' },");
                builder.AppendLine("      { key: 'EffectiveRightsSummary', label: 'Effective Access' },");
                builder.AppendLine("      { key: 'RiskLevel', label: 'Rischio' },");
                builder.AppendLine("      { key: 'Owner', label: 'Owner' },");
                builder.AppendLine("      { key: 'AuditSummary', label: 'SACL/Audit' },");
                builder.AppendLine("      { key: 'Depth', label: 'Depth' },");
                builder.AppendLine("      { key: 'IsDisabled', label: 'Disabilitato', type: 'bool' },");
                builder.AppendLine("      { key: 'HasFullControl', label: 'Full', type: 'bool' },");
                builder.AppendLine("      { key: 'HasModify', label: 'Modify', type: 'bool' },");
                builder.AppendLine("      { key: 'HasReadAndExecute', label: 'R&E', type: 'bool' },");
                builder.AppendLine("      { key: 'HasList', label: 'List', type: 'bool' },");
                builder.AppendLine("      { key: 'HasRead', label: 'Read', type: 'bool' },");
                builder.AppendLine("      { key: 'HasWrite', label: 'Write', type: 'bool' },");
                builder.AppendLine("      { key: 'IsInherited', label: 'Inherited', type: 'bool' },");
                builder.AppendLine("      { key: 'InheritanceFlags', label: 'Inheritance' },");
                builder.AppendLine("      { key: 'PropagationFlags', label: 'Propagation' }");
                builder.AppendLine("    ];");
                builder.AppendLine("    const shareColumns = [");
                builder.AppendLine("      { key: 'ShareServer', label: 'Server' },");
                builder.AppendLine("      { key: 'ShareName', label: 'Share' },");
                builder.AppendLine("      { key: 'PrincipalName', label: 'Principal' },");
                builder.AppendLine("      { key: 'PrincipalSid', label: 'SID' },");
                builder.AppendLine("      { key: 'AllowDeny', label: 'Allow/Deny' },");
                builder.AppendLine("      { key: 'RightsSummary', label: 'Diritti' },");
                builder.AppendLine("      { key: 'Depth', label: 'Depth' },");
                builder.AppendLine("      { key: 'Owner', label: 'Owner' },");
                builder.AppendLine("      { key: 'AuditSummary', label: 'SACL/Audit' }");
                builder.AppendLine("    ];");
                builder.AppendLine("    const effectiveColumns = [");
                builder.AppendLine("      { key: 'PrincipalName', label: 'Principal' },");
                builder.AppendLine("      { key: 'PrincipalSid', label: 'SID' },");
                builder.AppendLine("      { key: 'EffectiveRightsSummary', label: 'Effective' },");
                builder.AppendLine("      { key: 'NtfsRightsMask', label: 'NTFS Mask' },");
                builder.AppendLine("      { key: 'ShareRightsMask', label: 'Share Mask' },");
                builder.AppendLine("      { key: 'Depth', label: 'Depth' }");
                builder.AppendLine("    ];");
                builder.AppendLine("    const errorColumns = [");
                builder.AppendLine("      { key: 'Path', label: 'Path' },");
                builder.AppendLine("      { key: 'ErrorType', label: 'Tipo' },");
                builder.AppendLine("      { key: 'Message', label: 'Messaggio' }");
                builder.AppendLine("    ];");

                builder.AppendLine("    const treeContainer = document.getElementById('treeContainer');");
                builder.AppendLine("    const selectedPathElement = document.getElementById('selectedPath');");
                builder.AppendLine("    const colorizeToggle = document.getElementById('colorizeToggle');");
                builder.AppendLine("    const aclFilterInput = document.getElementById('aclFilter');");
                builder.AppendLine("    const filterEveryoneToggle = document.getElementById('filterEveryone');");
                builder.AppendLine("    const filterAuthUsersToggle = document.getElementById('filterAuthUsers');");
                builder.AppendLine("    const filterDenyToggle = document.getElementById('filterDeny');");
                builder.AppendLine("    const filterInheritanceToggle = document.getElementById('filterInheritance');");

                builder.AppendLine("    function getChildren(path) {");
                builder.AppendLine("      return treeMap[path] || [];" );
                builder.AppendLine("    }");

                builder.AppendLine("    function buildTree(path) {");
                builder.AppendLine("      const children = getChildren(path);");
                builder.AppendLine("      const listItem = document.createElement('li');");
                builder.AppendLine("      listItem.className = 'tree-item';");
                builder.AppendLine("      const label = document.createElement('span');");
                builder.AppendLine("      label.className = 'tree-label';");
                builder.AppendLine("      const toggle = document.createElement('span');");
                builder.AppendLine("      toggle.className = 'tree-toggle';");
                builder.AppendLine("      toggle.textContent = children.length ? (expandedPaths.has(path) ? '-' : '+') : '';" );
                builder.AppendLine("      label.appendChild(toggle);");
                builder.AppendLine("      const name = document.createElement('span');");
                builder.AppendLine("      name.textContent = displayName(path);");
                builder.AppendLine("      label.appendChild(name);");
                builder.AppendLine("      const flags = detailsData[path] || {};");
                builder.AppendLine("      if (flags.IsProtected) {");
                builder.AppendLine("        const badge = document.createElement('span');");
                builder.AppendLine("        badge.className = 'badge badge-protected';");
                builder.AppendLine("        badge.textContent = 'P';");
                builder.AppendLine("        label.appendChild(badge);");
                builder.AppendLine("      }");
                builder.AppendLine("      if ((flags.ExplicitAddedCount || 0) > 0) {");
                builder.AppendLine("        const badge = document.createElement('span');");
                builder.AppendLine("        badge.className = 'badge badge-added';");
                builder.AppendLine("        badge.textContent = `+${flags.ExplicitAddedCount}`;");
                builder.AppendLine("        label.appendChild(badge);");
                builder.AppendLine("      }");
                builder.AppendLine("      if ((flags.ExplicitRemovedCount || 0) > 0) {");
                builder.AppendLine("        const badge = document.createElement('span');");
                builder.AppendLine("        badge.className = 'badge badge-removed';");
                builder.AppendLine("        badge.textContent = `-${flags.ExplicitRemovedCount}`;");
                builder.AppendLine("        label.appendChild(badge);");
                builder.AppendLine("      }");
                builder.AppendLine("      if ((flags.DenyExplicitCount || 0) > 0) {");
                builder.AppendLine("        const badge = document.createElement('span');");
                builder.AppendLine("        badge.className = 'badge badge-deny';");
                builder.AppendLine("        badge.textContent = 'D';");
                builder.AppendLine("        label.appendChild(badge);");
                builder.AppendLine("      }");
                builder.AppendLine("      if ((flags.BaselineAddedCount || 0) > 0) {");
                builder.AppendLine("        const badge = document.createElement('span');");
                builder.AppendLine("        badge.className = 'badge badge-baseline-added';");
                builder.AppendLine("        badge.textContent = `B+${flags.BaselineAddedCount}`;");
                builder.AppendLine("        label.appendChild(badge);");
                builder.AppendLine("      }");
                builder.AppendLine("      if ((flags.BaselineRemovedCount || 0) > 0) {");
                builder.AppendLine("        const badge = document.createElement('span');");
                builder.AppendLine("        badge.className = 'badge badge-baseline-removed';");
                builder.AppendLine("        badge.textContent = `B-${flags.BaselineRemovedCount}`;");
                builder.AppendLine("        label.appendChild(badge);");
                builder.AppendLine("      }");
                builder.AppendLine("      if (flags.HasExplicitNtfs) {");
                builder.AppendLine("        const badge = document.createElement('span');");
                builder.AppendLine("        badge.className = 'badge badge-ntfs';");
                builder.AppendLine("        badge.textContent = 'N';");
                builder.AppendLine("        label.appendChild(badge);");
                builder.AppendLine("      }");
                builder.AppendLine("      if (flags.HasExplicitShare) {");
                builder.AppendLine("        const badge = document.createElement('span');");
                builder.AppendLine("        badge.className = 'badge badge-share';");
                builder.AppendLine("        badge.textContent = 'S';");
                builder.AppendLine("        label.appendChild(badge);");
                builder.AppendLine("      }");
                builder.AppendLine("      label.addEventListener('click', (event) => {" );
                builder.AppendLine("        event.stopPropagation();" );
                builder.AppendLine("        selectPath(path);" );
                builder.AppendLine("      });");
                builder.AppendLine("      listItem.appendChild(label);");

                builder.AppendLine("      if (children.length) {");
                builder.AppendLine("        const childList = document.createElement('ul');");
                builder.AppendLine("        childList.className = 'tree tree-children';" );
                builder.AppendLine("        const shouldExpand = expandedPaths.has(path) || path === rootPath || isAncestorOfSelected(path);" );
                builder.AppendLine("        if (!shouldExpand) { childList.classList.add('collapsed'); }" );
                builder.AppendLine("        if (shouldExpand) { toggle.textContent = '-'; }" );
                builder.AppendLine("        toggle.addEventListener('click', (event) => {");
                builder.AppendLine("          event.stopPropagation();" );
                builder.AppendLine("          const collapsed = childList.classList.toggle('collapsed');" );
                builder.AppendLine("          toggle.textContent = collapsed ? '+' : '-';" );
                builder.AppendLine("        });");
                builder.AppendLine("        children.forEach(child => { childList.appendChild(buildTree(child)); });" );
                builder.AppendLine("        listItem.appendChild(childList);");
                builder.AppendLine("      }");

                builder.AppendLine("      if (path === selectedPath) { label.classList.add('selected'); }");
                builder.AppendLine("      return listItem;");
                builder.AppendLine("    }");

                builder.AppendLine("    function isAncestorOfSelected(path) {");
                builder.AppendLine("      if (!selectedPath || !path) return false;" );
                builder.AppendLine("      if (path === selectedPath) return true;" );
                builder.AppendLine("      const children = getChildren(path);" );
                builder.AppendLine("      return children.some(child => isAncestorOfSelected(child));" );
                builder.AppendLine("    }");

                builder.AppendLine("    function displayName(path) {");
                builder.AppendLine("      if (!path) return '';");
                builder.AppendLine("      const trimmed = path.replace(/[\\\\/]+$/, '');");
                builder.AppendLine("      const parts = trimmed.split(/\\\\|\\//).filter(Boolean);");
                builder.AppendLine("      return parts.length ? parts[parts.length - 1] : path;");
                builder.AppendLine("    }");

                builder.AppendLine("    function renderTree() {");
                builder.AppendLine("      treeContainer.innerHTML = '';" );
                builder.AppendLine("      const list = document.createElement('ul');" );
                builder.AppendLine("      list.className = 'tree';" );
                builder.AppendLine("      if (rootPath) { list.appendChild(buildTree(rootPath)); }" );
                builder.AppendLine("      treeContainer.appendChild(list);");
                builder.AppendLine("    }");

                builder.AppendLine("    function selectPath(path) {");
                builder.AppendLine("      selectedPath = path;" );
                builder.AppendLine("      selectedPathElement.textContent = path || '';" );
                builder.AppendLine("      renderTables();" );
                builder.AppendLine("      renderTree();" );
                builder.AppendLine("    }");

                builder.AppendLine("    function getRowClass(entry) {");
                builder.AppendLine("      const classes = [];" );
                builder.AppendLine("      if (entry.IsDisabled) classes.push('row-disabled');" );
                builder.AppendLine("      if (!colorizeRights) return classes.join(' ');" );
                builder.AppendLine("      if (entry.HasFullControl) classes.push('rights-full');" );
                builder.AppendLine("      else if (entry.HasModify) classes.push('rights-modify');" );
                builder.AppendLine("      else if (entry.HasWrite) classes.push('rights-write');" );
                builder.AppendLine("      else if (entry.HasReadAndExecute) classes.push('rights-readexecute');" );
                builder.AppendLine("      else if (entry.HasList) classes.push('rights-list');" );
                builder.AppendLine("      else if (entry.HasRead) classes.push('rights-read');" );
                builder.AppendLine("      return classes.join(' ');" );
                builder.AppendLine("    }");

                builder.AppendLine("    function formatValue(value, type) {");
                builder.AppendLine("      if (type === 'bool') return value ? 'True' : 'False';" );
                builder.AppendLine("      if (value === null || value === undefined) return '';" );
                builder.AppendLine("      return value;" );
                builder.AppendLine("    }");

                builder.AppendLine("    function matchesAclFilter(entry, filter) {");
                builder.AppendLine("      if (!filter) return true;");
                builder.AppendLine("      const term = filter.toLowerCase().trim();");
                builder.AppendLine("      return (entry.PrincipalName || '').toLowerCase().includes(term) ||");
                builder.AppendLine("        (entry.PrincipalSid || '').toLowerCase().includes(term) ||");
                builder.AppendLine("        String(entry.PermissionLayer || '').toLowerCase().includes(term) ||");
                builder.AppendLine("        (entry.AllowDeny || '').toLowerCase().includes(term) ||");
                builder.AppendLine("        (entry.RightsSummary || '').toLowerCase().includes(term) ||");
                builder.AppendLine("        (entry.EffectiveRightsSummary || '').toLowerCase().includes(term) ||");
                builder.AppendLine("        (entry.AuditSummary || '').toLowerCase().includes(term) ||");
                builder.AppendLine("        (entry.ResourceType || '').toLowerCase().includes(term) ||");
                builder.AppendLine("        (entry.TargetPath || '').toLowerCase().includes(term) ||");
                builder.AppendLine("        (entry.Owner || '').toLowerCase().includes(term) ||");
                builder.AppendLine("        (entry.ShareName || '').toLowerCase().includes(term) ||");
                builder.AppendLine("        (entry.ShareServer || '').toLowerCase().includes(term) ||");
                builder.AppendLine("        (entry.RiskLevel || '').toLowerCase().includes(term) ||");
                builder.AppendLine("        (entry.Source || '').toLowerCase().includes(term) ||");
                builder.AppendLine("        matchesMemberFilter(entry.MemberNames, term);");
                builder.AppendLine("    }");

                builder.AppendLine("    function matchesMemberFilter(members, term) {");
                builder.AppendLine("      if (!members || !members.length) return false;");
                builder.AppendLine("      return members.some(member => (member || '').toLowerCase().includes(term));");
                builder.AppendLine("    }");

                builder.AppendLine("    function isEveryone(entry) {");
                builder.AppendLine("      const sid = (entry.PrincipalSid || '').toLowerCase();");
                builder.AppendLine("      const name = (entry.PrincipalName || '').toLowerCase();");
                builder.AppendLine("      return sid === 's-1-1-0' || name.includes('everyone') || name.includes('tutti');");
                builder.AppendLine("    }");

                builder.AppendLine("    function isAuthenticatedUsers(entry) {");
                builder.AppendLine("      const sid = (entry.PrincipalSid || '').toLowerCase();");
                builder.AppendLine("      const name = (entry.PrincipalName || '').toLowerCase();");
                builder.AppendLine("      return sid === 's-1-5-11' || name.includes('authenticated users') || name.includes('utenti autenticati');");
                builder.AppendLine("    }");

                builder.AppendLine("    function passesToggleFilters(entry) {");
                builder.AppendLine("      if (filterDenyOnly && (entry.AllowDeny || '').toLowerCase() !== 'deny') {");
                builder.AppendLine("        return false;");
                builder.AppendLine("      }");
                builder.AppendLine("      if (filterInheritanceDisabled && !entry.IsInheritanceDisabled) {");
                builder.AppendLine("        return false;");
                builder.AppendLine("      }");
                builder.AppendLine("      if (filterEveryone || filterAuthenticatedUsers) {");
                builder.AppendLine("        const match = (filterEveryone && isEveryone(entry)) || (filterAuthenticatedUsers && isAuthenticatedUsers(entry));");
                builder.AppendLine("        if (!match) return false;");
                builder.AppendLine("      }");
                builder.AppendLine("      return true;");
                builder.AppendLine("    }");

                builder.AppendLine("    function renderTable(tableId, entries, columns, useRightsClass) {");
                builder.AppendLine("      const table = document.getElementById(tableId);" );
                builder.AppendLine("      table.innerHTML = '';" );
                builder.AppendLine("      const thead = document.createElement('thead');" );
                builder.AppendLine("      const headerRow = document.createElement('tr');" );
                builder.AppendLine("      columns.forEach(col => {" );
                builder.AppendLine("        const th = document.createElement('th');" );
                builder.AppendLine("        th.textContent = col.label;" );
                builder.AppendLine("        headerRow.appendChild(th);" );
                builder.AppendLine("      });" );
                builder.AppendLine("      thead.appendChild(headerRow);" );
                builder.AppendLine("      table.appendChild(thead);" );
                builder.AppendLine("      const tbody = document.createElement('tbody');" );
                builder.AppendLine("      entries.forEach(entry => {" );
                builder.AppendLine("        const row = document.createElement('tr');" );
                builder.AppendLine("        if (useRightsClass) { row.className = getRowClass(entry); }" );
                builder.AppendLine("        columns.forEach(col => {" );
                builder.AppendLine("          const td = document.createElement('td');" );
                builder.AppendLine("          td.textContent = formatValue(entry[col.key], col.type);" );
                builder.AppendLine("          row.appendChild(td);" );
                builder.AppendLine("        });" );
                builder.AppendLine("        tbody.appendChild(row);" );
                builder.AppendLine("      });" );
                builder.AppendLine("      table.appendChild(tbody);" );
                builder.AppendLine("    }");

                builder.AppendLine("    function renderTables() {");
                builder.AppendLine("      const detail = detailsData[selectedPath] || { GroupEntries: [], UserEntries: [], AllEntries: [], ShareEntries: [], EffectiveEntries: [] };" );
                builder.AppendLine("      const aclFilterValue = aclFilterInput.value || '';");
                builder.AppendLine("      const groupEntries = (detail.GroupEntries || []).filter(entry => passesToggleFilters(entry) && matchesAclFilter(entry, aclFilterValue));");
                builder.AppendLine("      const userEntries = (detail.UserEntries || []).filter(entry => passesToggleFilters(entry) && matchesAclFilter(entry, aclFilterValue));");
                builder.AppendLine("      const aclEntries = (detail.AllEntries || []).filter(entry => passesToggleFilters(entry) && matchesAclFilter(entry, aclFilterValue));");
                builder.AppendLine("      const shareEntries = (detail.ShareEntries || []).filter(entry => passesToggleFilters(entry) && matchesAclFilter(entry, aclFilterValue));");
                builder.AppendLine("      const effectiveEntries = (detail.EffectiveEntries || []).filter(entry => passesToggleFilters(entry) && matchesAclFilter(entry, aclFilterValue));");
                builder.AppendLine("      renderTable('groupsTable', groupEntries, groupColumns, true);" );
                builder.AppendLine("      renderTable('usersTable', userEntries, userColumns, true);" );
                builder.AppendLine("      renderTable('aclTable', aclEntries, aclColumns, true);" );
                builder.AppendLine("      renderTable('shareTable', shareEntries, shareColumns, true);" );
                builder.AppendLine("      renderTable('effectiveTable', effectiveEntries, effectiveColumns, true);" );
                builder.AppendLine("      renderErrors();" );
                builder.AppendLine("    }");

                builder.AppendLine("    function renderErrors() {");
                builder.AppendLine("      renderTable('errorsTable', errorsData, errorColumns, false);" );
                builder.AppendLine("    }");

                builder.AppendLine("    function wireTabs() {");
                builder.AppendLine("      document.querySelectorAll('.tab-button').forEach(button => {");
                builder.AppendLine("        button.addEventListener('click', () => {" );
                builder.AppendLine("          document.querySelectorAll('.tab-button').forEach(btn => btn.classList.remove('active'));");
                builder.AppendLine("          document.querySelectorAll('.tab-panel').forEach(panel => panel.classList.remove('active'));");
                builder.AppendLine("          button.classList.add('active');" );
                builder.AppendLine("          const tab = button.getAttribute('data-tab');" );
                builder.AppendLine("          const panel = document.getElementById(`tab-${tab}`);" );
                builder.AppendLine("          if (panel) panel.classList.add('active');" );
                builder.AppendLine("        });" );
                builder.AppendLine("      });" );
                builder.AppendLine("    }");

                builder.AppendLine("    document.getElementById('expandAll').addEventListener('click', () => {");
                builder.AppendLine("      function expandAll(path) {");
                builder.AppendLine("        expandedPaths.add(path);" );
                builder.AppendLine("        (treeMap[path] || []).forEach(child => expandAll(child));" );
                builder.AppendLine("      }");
                builder.AppendLine("      if (rootPath) expandAll(rootPath);" );
                builder.AppendLine("      renderTree();" );
                builder.AppendLine("    });");

                builder.AppendLine("    document.getElementById('collapseAll').addEventListener('click', () => {");
                builder.AppendLine("      expandedPaths.clear();" );
                builder.AppendLine("      renderTree();" );
                builder.AppendLine("    });");

                builder.AppendLine("    aclFilterInput.addEventListener('input', () => {");
                builder.AppendLine("      renderTables();");
                builder.AppendLine("    });");

                builder.AppendLine("    filterEveryoneToggle.addEventListener('change', () => {");
                builder.AppendLine("      filterEveryone = filterEveryoneToggle.checked;");
                builder.AppendLine("      renderTables();");
                builder.AppendLine("    });");
                builder.AppendLine("    filterAuthUsersToggle.addEventListener('change', () => {");
                builder.AppendLine("      filterAuthenticatedUsers = filterAuthUsersToggle.checked;");
                builder.AppendLine("      renderTables();");
                builder.AppendLine("    });");
                builder.AppendLine("    filterDenyToggle.addEventListener('change', () => {");
                builder.AppendLine("      filterDenyOnly = filterDenyToggle.checked;");
                builder.AppendLine("      renderTables();");
                builder.AppendLine("    });");
                builder.AppendLine("    filterInheritanceToggle.addEventListener('change', () => {");
                builder.AppendLine("      filterInheritanceDisabled = filterInheritanceToggle.checked;");
                builder.AppendLine("      renderTables();");
                builder.AppendLine("    });");

                builder.AppendLine("    colorizeToggle.addEventListener('change', () => {");
                builder.AppendLine("      colorizeRights = colorizeToggle.checked;" );
                builder.AppendLine("      renderTables();" );
                builder.AppendLine("    });");

                builder.AppendLine("    colorizeToggle.checked = colorizeRights;");
                builder.AppendLine("    aclFilterInput.value = initialAclFilter;");
                builder.AppendLine("    filterEveryoneToggle.checked = filterEveryone;");
                builder.AppendLine("    filterAuthUsersToggle.checked = filterAuthenticatedUsers;");
                builder.AppendLine("    filterDenyToggle.checked = filterDenyOnly;");
                builder.AppendLine("    filterInheritanceToggle.checked = filterInheritanceDisabled;");
                builder.AppendLine("    selectedPathElement.textContent = selectedPath || '';" );
                builder.AppendLine("    wireTabs();" );
                builder.AppendLine("    renderTree();" );
                builder.AppendLine("    renderTables();" );
                builder.AppendLine("  </script>");
                builder.AppendLine("</body>");
                builder.AppendLine("</html>");
            }
        }

        private sealed class HtmlLineWriter
        {
            private readonly TextWriter _writer;
            private readonly JsonSerializer _serializer;

            public HtmlLineWriter(TextWriter writer, JsonSerializer serializer)
            {
                _writer = writer ?? throw new ArgumentNullException("writer");
                _serializer = serializer ?? throw new ArgumentNullException("serializer");
            }

            public void AppendLine(string value)
            {
                _writer.WriteLine(value);
            }

            public void AppendJsonLine(string prefix, object value, string suffix)
            {
                _writer.Write(prefix);
                _serializer.Serialize(_writer, value);
                _writer.WriteLine(suffix);
            }

            public void AppendDetailsLine(string prefix, Dictionary<string, FolderDetail> details, string suffix)
            {
                _writer.Write(prefix);
                using (var jsonWriter = new JsonTextWriter(_writer) { CloseOutput = false })
                {
                    jsonWriter.WriteStartObject();
                    if (details != null)
                    {
                        foreach (var entry in details)
                        {
                            jsonWriter.WritePropertyName(entry.Key);
                            WriteDetail(jsonWriter, entry.Value);
                        }
                    }
                    jsonWriter.WriteEndObject();
                    jsonWriter.Flush();
                }
                _writer.WriteLine(suffix);
            }

            private void WriteDetail(JsonWriter writer, FolderDetail detail)
            {
                var summary = detail == null ? null : detail.DiffSummary;
                var baselineSummary = detail == null ? null : detail.BaselineSummary;
                var added = summary == null ? 0 : summary.Added.Count(key => !key.IsInherited);
                var removed = summary == null ? 0 : summary.Removed.Count;
                var deny = summary == null ? 0 : summary.DenyExplicitCount;
                var baselineAdded = baselineSummary == null ? 0 : baselineSummary.Added.Count;
                var baselineRemoved = baselineSummary == null ? 0 : baselineSummary.Removed.Count;

                writer.WriteStartObject();
                WriteProperty(writer, "GroupEntries", detail == null ? Array.Empty<AceEntry>() : (detail.GroupEntries ?? Array.Empty<AceEntry>()));
                WriteProperty(writer, "UserEntries", detail == null ? Array.Empty<AceEntry>() : (detail.UserEntries ?? Array.Empty<AceEntry>()));
                WriteProperty(writer, "AllEntries", detail == null ? Array.Empty<AceEntry>() : (detail.AllEntries ?? Array.Empty<AceEntry>()));
                WriteProperty(writer, "ShareEntries", detail == null ? Array.Empty<AceEntry>() : (detail.ShareEntries ?? Array.Empty<AceEntry>()));
                WriteProperty(writer, "EffectiveEntries", detail == null ? Array.Empty<AceEntry>() : (detail.EffectiveEntries ?? Array.Empty<AceEntry>()));
                writer.WritePropertyName("HasExplicitPermissions");
                writer.WriteValue(detail != null && detail.HasExplicitPermissions);
                writer.WritePropertyName("HasExplicitNtfs");
                writer.WriteValue(detail != null && detail.HasExplicitNtfs);
                writer.WritePropertyName("HasExplicitShare");
                writer.WriteValue(detail != null && detail.HasExplicitShare);
                writer.WritePropertyName("IsInheritanceDisabled");
                writer.WriteValue(detail != null && detail.IsInheritanceDisabled);
                writer.WritePropertyName("IsProtected");
                writer.WriteValue(summary != null ? summary.IsProtected : detail != null && detail.IsInheritanceDisabled);
                writer.WritePropertyName("ExplicitAddedCount");
                writer.WriteValue(added);
                writer.WritePropertyName("ExplicitRemovedCount");
                writer.WriteValue(removed);
                writer.WritePropertyName("DenyExplicitCount");
                writer.WriteValue(deny);
                writer.WritePropertyName("BaselineAddedCount");
                writer.WriteValue(baselineAdded);
                writer.WritePropertyName("BaselineRemovedCount");
                writer.WriteValue(baselineRemoved);
                writer.WriteEndObject();
            }

            private void WriteProperty(JsonWriter writer, string name, object value)
            {
                writer.WritePropertyName(name);
                _serializer.Serialize(writer, value);
            }
        }

        private static string ResolveRootPath(string rootPath, Dictionary<string, List<string>> treeMap)
        {
            if (!string.IsNullOrWhiteSpace(rootPath) && treeMap.ContainsKey(rootPath)) return rootPath;
            if (treeMap.Count == 0) return rootPath ?? string.Empty;
            return treeMap.Keys.First();
        }

    }
}
