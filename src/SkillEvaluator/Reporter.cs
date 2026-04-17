using System.Text;

namespace SkillEvaluator;

public static class Reporter
{
    public static string BuildMarkdown(
        IReadOnlyList<ArtifactResult> results,
        string provider,
        string? model,
        TimeSpan duration)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Skill Evaluator Report");
        sb.AppendLine();
        sb.AppendLine($"- **Generated**: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine($"- **Provider**: {provider}{(model is null ? "" : $" ({model})")}");
        sb.AppendLine($"- **Artifacts**: {results.Count}");
        sb.AppendLine($"- **Duration**: {duration.TotalSeconds:F0}s");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        var accepts = results.Count(r => r.Verdict.Kind == VerdictKind.Accept);
        var revises = results.Count(r => r.Verdict.Kind == VerdictKind.Revise);
        var rejects = results.Count(r => r.Verdict.Kind == VerdictKind.Reject);
        sb.AppendLine("| Verdict   | Count |");
        sb.AppendLine("|-----------|-------|");
        sb.AppendLine($"| ✅ Accept | {accepts}     |");
        sb.AppendLine($"| 🔧 Revise | {revises}     |");
        sb.AppendLine($"| ❌ Reject | {rejects}     |");
        sb.AppendLine();

        AppendSection(sb, "Rejects", results.Where(r => r.Verdict.Kind == VerdictKind.Reject));
        AppendSection(sb, "Revises", results.Where(r => r.Verdict.Kind == VerdictKind.Revise));
        AppendSection(sb, "Accepts", results.Where(r => r.Verdict.Kind == VerdictKind.Accept));

        return sb.ToString();
    }

    private static void AppendSection(StringBuilder sb, string title, IEnumerable<ArtifactResult> items)
    {
        var list = items.ToList();
        if (list.Count == 0)
        {
            return;
        }

        sb.AppendLine($"## {title}");
        sb.AppendLine();
        foreach (var r in list)
        {
            sb.AppendLine($"### {r.Artifact.Name} ({r.Artifact.Kind.ToString().ToLowerInvariant()})");
            sb.AppendLine();
            sb.AppendLine($"**Path**: `{r.Artifact.Path}`");
            sb.AppendLine();
            if (r.Verdict.Reasons.Count > 0)
            {
                sb.AppendLine("**Reasons**:");
                foreach (var reason in r.Verdict.Reasons)
                {
                    sb.AppendLine($"- {reason}");
                }
                sb.AppendLine();
            }
            if (r.Static.Findings.Count > 0)
            {
                sb.AppendLine("**Static findings**:");
                foreach (var f in r.Static.Findings)
                {
                    var icon = f.Severity switch
                    {
                        Severity.Blocker => "🛑",
                        Severity.Warn    => "⚠",
                        _                => "ℹ",
                    };
                    sb.AppendLine($"- {icon} `{f.Check}`: {f.Message}");
                }
                sb.AppendLine();
            }
            sb.AppendLine("---");
            sb.AppendLine();
        }
    }
}
