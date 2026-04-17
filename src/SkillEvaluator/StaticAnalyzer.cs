using Microsoft.ML.Tokenizers;

namespace SkillEvaluator;

public static class StaticAnalyzer
{
    private static readonly TiktokenTokenizer s_tokenizer =
        TiktokenTokenizer.CreateForEncoding("cl100k_base");

    // Token-tier thresholds. Source: design doc
    // (docs/plans/2026-04-16-skill-evaluator-design.md §"Static-check catalog").
    private const int CompactMax       = 400;   // < 400 tokens = compact (Info)
    private const int DetailedMax      = 2500;  // [400, 2500] = detailed (Info)
    private const int StandardMax      = 5000;  // (2500, 5000] = standard (Warn)
                                                // > 5000 = comprehensive (Blocker)

    public static StaticReport Analyze(Artifact artifact)
    {
        var findings = new List<Finding>();

        findings.AddRange(CheckFrontmatterPresent(artifact));

        if (artifact.Kind is ArtifactKind.Skill or ArtifactKind.Agent)
        {
            findings.AddRange(CheckTokenTier(artifact));
        }

        if (artifact.Kind == ArtifactKind.Instruction)
        {
            findings.AddRange(CheckBodyLength(artifact));
            findings.AddRange(CheckApplyToGlob(artifact));
        }

        if (artifact.Kind == ArtifactKind.Skill)
        {
            findings.AddRange(CheckReferencedFilesExist(artifact));
        }

        var warnings = findings.Count(f => f.Severity == Severity.Warn);
        var score = Math.Max(0, 100 - warnings * 5);
        return new StaticReport(Score: score, Findings: findings);
    }

    private static IEnumerable<Finding> CheckFrontmatterPresent(Artifact artifact)
    {
        var required = artifact.Kind switch
        {
            ArtifactKind.Skill       => new[] { "name", "description" },
            ArtifactKind.Agent       => new[] { "name", "description" },
            ArtifactKind.Instruction => new[] { "description", "applyTo" },
            _                        => Array.Empty<string>(),
        };

        foreach (var key in required)
        {
            if (!artifact.Frontmatter.ContainsKey(key))
            {
                yield return new Finding(
                    Severity: Severity.Blocker,
                    Check: "FrontmatterPresent",
                    Message: $"Missing required frontmatter field: {key}"
                );
            }
        }
    }

    private static IEnumerable<Finding> CheckTokenTier(Artifact artifact)
    {
        var fullText = artifact.Reassemble();
        var tokens = s_tokenizer.CountTokens(fullText);

        var (tier, severity) = tokens switch
        {
            < CompactMax  => ("compact", Severity.Info),
            <= DetailedMax => ("detailed", Severity.Info),
            <= StandardMax => ("standard", Severity.Warn),
            _              => ("comprehensive", Severity.Blocker),
        };

        yield return new Finding(
            Severity: severity,
            Check: "TokenTier",
            Message: $"{tokens} tokens ({tier} tier)"
        );
    }

    private static IEnumerable<Finding> CheckBodyLength(Artifact artifact)
    {
        // Count lines without double-counting the trailing empty element when
        // Body ends in '\n'. TrimEnd('\n') means "150 real lines" reports 150.
        var lines = artifact.Body.TrimEnd('\n').Split('\n').Length;
        if (lines > 150)
        {
            yield return new Finding(Severity.Warn, "BodyLength", $"Body is {lines} lines (>150)");
        }
        else if (lines >= 50)
        {
            yield return new Finding(Severity.Info, "BodyLength", $"Body is {lines} lines");
        }
    }

    private static IEnumerable<Finding> CheckApplyToGlob(Artifact artifact)
    {
        if (!artifact.Frontmatter.TryGetValue("applyTo", out var val) || val is not string glob)
        {
            yield break;
        }

        var trimmed = glob.Trim();
        if (trimmed is "**/*" or "**")
        {
            yield return new Finding(Severity.Warn, "ApplyToGlobValidity", $"Overly broad applyTo glob: {glob}");
        }
    }

    private static IEnumerable<Finding> CheckReferencedFilesExist(Artifact artifact)
    {
        foreach (var referenced in artifact.ReferencedFiles)
        {
            if (!File.Exists(referenced))
            {
                yield return new Finding(
                    Severity: Severity.Blocker,
                    Check: "ReferencedFilesExist",
                    Message: $"Referenced file does not exist: {referenced}"
                );
            }
        }
    }
}
