using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SkillEvaluator;

public static class Reporter
{
    private const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string BuildJson(
        IReadOnlyList<ArtifactResult> results,
        string provider,
        string? model,
        TimeSpan duration)
    {
        var summary = new JsonSummary(
            ByVerdict: new JsonVerdictCounts(
                Accept: results.Count(r => r.Verdict.Kind == VerdictKind.Accept),
                Revise: results.Count(r => r.Verdict.Kind == VerdictKind.Revise),
                Reject: results.Count(r => r.Verdict.Kind == VerdictKind.Reject)
            ),
            Total: results.Count
        );

        var artifacts = results
            .OrderBy(r => r.Artifact.Name)
            .Select(ToJsonArtifact)
            .ToList();

        var report = new JsonReport(
            SchemaVersion: SchemaVersion,
            GeneratedUtc: DateTime.UtcNow,
            Provider: provider,
            Model: model,
            DurationSeconds: duration.TotalSeconds,
            Summary: summary,
            Artifacts: artifacts
        );

        return JsonSerializer.Serialize(report, s_jsonOptions);
    }

    private static JsonArtifact ToJsonArtifact(ArtifactResult r) => new(
        Name: r.Artifact.Name,
        Kind: r.Artifact.Kind.ToString().ToLowerInvariant(),
        Path: r.Artifact.Path,
        Verdict: new JsonVerdict(
            Kind: r.Verdict.Kind.ToString().ToLowerInvariant(),
            Score: Math.Round(r.Verdict.Score, 2),
            Reasons: r.Verdict.Reasons
        ),
        Static: new JsonStatic(
            Score: r.Static.Score,
            Findings: r.Static.Findings
                .Select(f => new JsonFinding(f.Severity.ToString().ToLowerInvariant(), f.Check, f.Message))
                .ToList()
        ),
        Rubric: r.Rubric is null ? null : new JsonRubric(
            TriggerClarity:       ToJsonDim(r.Rubric.TriggerClarity),
            ScopeCoherence:       ToJsonDim(r.Rubric.ScopeCoherence),
            InstructionalQuality: ToJsonDim(r.Rubric.InstructionalQuality),
            Generality:           ToJsonDim(r.Rubric.Generality),
            SafetyTrust:          ToJsonDim(r.Rubric.SafetyTrust),
            VerdictHint: r.Rubric.VerdictHint,
            TopConcerns: r.Rubric.TopConcerns,
            Strengths: r.Rubric.Strengths
        ),
        ProviderError: r.ProviderError
    );

    private static JsonDim ToJsonDim(DimensionScore dim) => new(dim.Score, dim.Rationale);

    private sealed record JsonReport(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("generated_utc")] DateTime GeneratedUtc,
        [property: JsonPropertyName("provider")] string Provider,
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("duration_seconds")] double DurationSeconds,
        [property: JsonPropertyName("summary")] JsonSummary Summary,
        [property: JsonPropertyName("artifacts")] IReadOnlyList<JsonArtifact> Artifacts
    );

    private sealed record JsonSummary(
        [property: JsonPropertyName("by_verdict")] JsonVerdictCounts ByVerdict,
        [property: JsonPropertyName("total")] int Total
    );

    private sealed record JsonVerdictCounts(
        [property: JsonPropertyName("accept")] int Accept,
        [property: JsonPropertyName("revise")] int Revise,
        [property: JsonPropertyName("reject")] int Reject
    );

    private sealed record JsonArtifact(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("verdict")] JsonVerdict Verdict,
        [property: JsonPropertyName("static")] JsonStatic Static,
        [property: JsonPropertyName("rubric")] JsonRubric? Rubric,
        [property: JsonPropertyName("provider_error")] string? ProviderError
    );

    private sealed record JsonVerdict(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("score")] double Score,
        [property: JsonPropertyName("reasons")] IReadOnlyList<string> Reasons
    );

    private sealed record JsonStatic(
        [property: JsonPropertyName("score")] int Score,
        [property: JsonPropertyName("findings")] IReadOnlyList<JsonFinding> Findings
    );

    private sealed record JsonFinding(
        [property: JsonPropertyName("severity")] string Severity,
        [property: JsonPropertyName("check")] string Check,
        [property: JsonPropertyName("message")] string Message
    );

    private sealed record JsonRubric(
        [property: JsonPropertyName("trigger_clarity")] JsonDim TriggerClarity,
        [property: JsonPropertyName("scope_coherence")] JsonDim ScopeCoherence,
        [property: JsonPropertyName("instructional_quality")] JsonDim InstructionalQuality,
        [property: JsonPropertyName("generality")] JsonDim Generality,
        [property: JsonPropertyName("safety_trust")] JsonDim SafetyTrust,
        [property: JsonPropertyName("verdict_hint")] string VerdictHint,
        [property: JsonPropertyName("top_concerns")] IReadOnlyList<string> TopConcerns,
        [property: JsonPropertyName("strengths")] IReadOnlyList<string> Strengths
    );

    private sealed record JsonDim(
        [property: JsonPropertyName("score")] int Score,
        [property: JsonPropertyName("rationale")] string Rationale
    );
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
