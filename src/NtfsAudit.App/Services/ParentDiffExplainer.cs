using System;
using System.Collections.Generic;
using System.Linq;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    public class ParentDiffExplainer
    {
        private const int MaxReasons = 3;

        public FolderExplanation Explain(
            FolderDetail child,
            FolderDetail parent,
            bool useEffectiveLayer,
            bool isRoot,
            bool hasReadError)
        {
            var explanation = new FolderExplanation();
            if (hasReadError)
            {
                explanation.Status = FolderStatus.Unknown;
                explanation.Summary = "Errore lettura";
                explanation.Reasons.Add("Impossibile leggere i permessi.");
                return explanation;
            }

            if (isRoot)
            {
                explanation.Status = FolderStatus.Same;
                explanation.Summary = "Uguale al padre";
                explanation.Reasons.Add("Cartella radice: non esiste una cartella padre da confrontare.");
                return explanation;
            }

            var childEntries = SelectLayerEntries(child, useEffectiveLayer);
            var parentEntries = SelectLayerEntries(parent, useEffectiveLayer);

            var hasDeny = childEntries.Any(e => !e.IsInherited && string.Equals(e.AllowDeny, "Deny", StringComparison.OrdinalIgnoreCase));
            var childScores = BuildPrincipalScores(childEntries);
            var parentScores = BuildPrincipalScores(parentEntries);

            var reasons = new List<string>();
            var hasIncrease = false;
            var hasDecrease = false;

            foreach (var sid in childScores.Keys.Union(parentScores.Keys, StringComparer.OrdinalIgnoreCase))
            {
                var childScore = childScores.TryGetValue(sid, out var cs) ? cs : 0;
                var parentScore = parentScores.TryGetValue(sid, out var ps) ? ps : 0;
                if (childScore > parentScore)
                {
                    hasIncrease = true;
                    if (reasons.Count < MaxReasons)
                    {
                        var name = ResolvePrincipalName(childEntries, sid) ?? sid;
                        reasons.Add(string.Format("Aggiunto o ampliato accesso {0} per {1}.", ScoreLabel(childScore), name));
                    }
                }
                else if (childScore < parentScore)
                {
                    hasDecrease = true;
                    if (reasons.Count < MaxReasons)
                    {
                        var name = ResolvePrincipalName(parentEntries, sid) ?? sid;
                        if (childScore == 0)
                        {
                            reasons.Add(string.Format("Rimosso accesso per {0}.", name));
                        }
                        else
                        {
                            reasons.Add(string.Format("Ridotto accesso per {0} (ora {1}).", name, ScoreLabel(childScore)));
                        }
                    }
                }
            }

            var inheritanceBroken = child != null && child.IsInheritanceDisabled;
            if (inheritanceBroken && reasons.Count < MaxReasons)
            {
                reasons.Add("Disattivata ereditarietà: i permessi non seguono la cartella superiore.");
            }
            if (hasDeny && reasons.Count < MaxReasons)
            {
                reasons.Add("Presente regola di blocco (Deny).");
            }

            if (hasDeny)
            {
                explanation.Status = FolderStatus.DenyPresent;
                explanation.Summary = "Presente blocco (Deny)";
            }
            else if (hasIncrease)
            {
                explanation.Status = FolderStatus.MorePermissive;
                explanation.Summary = "Più aperta del padre";
            }
            else if (hasDecrease)
            {
                explanation.Status = FolderStatus.MoreRestrictive;
                explanation.Summary = "Più restrittiva del padre";
            }
            else if (inheritanceBroken)
            {
                explanation.Status = FolderStatus.BrokenInheritance;
                explanation.Summary = "Ereditarietà disattivata";
            }
            else
            {
                explanation.Status = FolderStatus.Same;
                explanation.Summary = "Uguale al padre";
                if (reasons.Count == 0)
                {
                    reasons.Add("Nessuna differenza effettiva rispetto alla cartella padre.");
                }
            }

            explanation.Reasons.AddRange(reasons.Take(MaxReasons));
            return explanation;
        }

        private static List<AceEntry> SelectLayerEntries(FolderDetail detail, bool useEffectiveLayer)
        {
            if (detail == null)
            {
                return new List<AceEntry>();
            }

            if (useEffectiveLayer && detail.EffectiveEntries != null && detail.EffectiveEntries.Count > 0)
            {
                return detail.EffectiveEntries;
            }

            return detail.AllEntries == null
                ? new List<AceEntry>()
                : detail.AllEntries.Where(e => e.PermissionLayer == PermissionLayer.Ntfs).ToList();
        }

        private static Dictionary<string, int> BuildPrincipalScores(List<AceEntry> entries)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (entries == null)
            {
                return map;
            }

            foreach (var group in entries
                .Where(e => !string.IsNullOrWhiteSpace(e.PrincipalSid))
                .GroupBy(e => e.PrincipalSid, StringComparer.OrdinalIgnoreCase))
            {
                var allowScore = group
                    .Where(e => string.Equals(e.AllowDeny, "Allow", StringComparison.OrdinalIgnoreCase))
                    .Select(e => ToScore(e))
                    .DefaultIfEmpty(0)
                    .Max();

                var hasExplicitDeny = group.Any(e => !e.IsInherited && string.Equals(e.AllowDeny, "Deny", StringComparison.OrdinalIgnoreCase));
                map[group.Key] = hasExplicitDeny ? 0 : allowScore;
            }

            return map;
        }

        private static int ToScore(AceEntry entry)
        {
            var summary = !string.IsNullOrWhiteSpace(entry.EffectiveRightsSummary)
                ? entry.EffectiveRightsSummary
                : entry.RightsSummary;
            if (string.IsNullOrWhiteSpace(summary)) return 0;
            if (summary.IndexOf("FullControl", StringComparison.OrdinalIgnoreCase) >= 0) return 3;
            if (summary.IndexOf("Modify", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            if (summary.IndexOf("Read", StringComparison.OrdinalIgnoreCase) >= 0
                || summary.IndexOf("List", StringComparison.OrdinalIgnoreCase) >= 0
                || summary.IndexOf("ReadAndExecute", StringComparison.OrdinalIgnoreCase) >= 0) return 1;
            return 0;
        }

        private static string ScoreLabel(int score)
        {
            switch (score)
            {
                case 3: return "Full";
                case 2: return "Modify";
                case 1: return "Read";
                default: return "None";
            }
        }

        private static string ResolvePrincipalName(IEnumerable<AceEntry> entries, string sid)
        {
            return entries
                .Where(e => string.Equals(e.PrincipalSid, sid, StringComparison.OrdinalIgnoreCase))
                .Select(e => e.PrincipalName)
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
        }
    }
}
