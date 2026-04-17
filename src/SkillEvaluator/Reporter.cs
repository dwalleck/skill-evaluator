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

        sb.AppendLine("## Appendix: Rubric prompt");
        sb.AppendLine();
        sb.AppendLine("<details>");
        sb.AppendLine("<summary>Click to expand</summary>");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine(Rubric.SystemPrompt);
        sb.AppendLine();
        sb.AppendLine("(User prompt template — see Rubric.cs — substitutes {kind} and {content} per artifact.)");
        sb.AppendLine("```");
        sb.AppendLine("</details>");

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
            if (r.ProviderError is { } err)
            {
                sb.AppendLine("**Provider error** (rubric skipped):");
                sb.AppendLine();
                sb.AppendLine("```");
                sb.AppendLine(err);
                sb.AppendLine("```");
                sb.AppendLine();
            }
            if (r.Rubric is { } rubric)
            {
                sb.AppendLine("**Rubric scores**:");
                sb.AppendLine();
                sb.AppendLine("| Dimension             | Score | Rationale |");
                sb.AppendLine("|-----------------------|-------|-----------|");
                AppendDimension(sb, "trigger_clarity",       rubric.TriggerClarity);
                AppendDimension(sb, "scope_coherence",       rubric.ScopeCoherence);
                AppendDimension(sb, "instructional_quality", rubric.InstructionalQuality);
                AppendDimension(sb, "generality",            rubric.Generality);
                AppendDimension(sb, "safety_trust",          rubric.SafetyTrust);
                sb.AppendLine();

                if (rubric.TopConcerns.Count > 0)
                {
                    sb.AppendLine("**Top concerns**:");
                    foreach (var c in rubric.TopConcerns)
                    {
                        sb.AppendLine($"- {c}");
                    }
                    sb.AppendLine();
                }
                if (rubric.Strengths.Count > 0)
                {
                    sb.AppendLine("**Strengths**:");
                    foreach (var s in rubric.Strengths)
                    {
                        sb.AppendLine($"- {s}");
                    }
                    sb.AppendLine();
                }
            }
            sb.AppendLine("---");
            sb.AppendLine();
        }
    }

    private static void AppendDimension(StringBuilder sb, string key, DimensionScore dim)
    {
        // Escape pipes in rationales so they don't break the markdown table row.
        var safeRationale = dim.Rationale.Replace("|", "\\|");
        sb.AppendLine($"| {key,-21} | {dim.Score}     | {safeRationale} |");
    }
}
