namespace SkillEvaluator;

public static class StaticAnalyzer
{
    public static StaticReport Analyze(Artifact artifact)
    {
        var findings = new List<Finding>();

        findings.AddRange(CheckFrontmatterPresent(artifact));

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
}
