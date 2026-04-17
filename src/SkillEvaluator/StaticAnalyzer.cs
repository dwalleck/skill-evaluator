using Microsoft.ML.Tokenizers;

namespace SkillEvaluator;

public static class StaticAnalyzer
{
    private static readonly TiktokenTokenizer s_tokenizer =
        TiktokenTokenizer.CreateForEncoding("cl100k_base");

    public static StaticReport Analyze(Artifact artifact)
    {
        var findings = new List<Finding>();

        findings.AddRange(CheckFrontmatterPresent(artifact));

        if (artifact.Kind is ArtifactKind.Skill or ArtifactKind.Agent)
        {
            findings.AddRange(CheckTokenTier(artifact));
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
        var fullText = ReassembleArtifactText(artifact);
        var tokens = s_tokenizer.CountTokens(fullText);

        var (tier, severity) = tokens switch
        {
            < 400 => ("compact", Severity.Info),
            < 2501 => ("detailed", Severity.Info),
            < 5001 => ("standard", Severity.Warn),
            _ => ("comprehensive", Severity.Blocker),
        };

        yield return new Finding(
            Severity: severity,
            Check: "TokenTier",
            Message: $"{tokens} tokens ({tier} tier)",
            Data: new Dictionary<string, object> { ["tokens"] = tokens, ["tier"] = tier }
        );
    }

    private static string ReassembleArtifactText(Artifact artifact)
    {
        var fm = string.Join("\n", artifact.Frontmatter.Select(kv => $"{kv.Key}: {kv.Value}"));
        return $"---\n{fm}\n---\n{artifact.Body}";
    }
}
