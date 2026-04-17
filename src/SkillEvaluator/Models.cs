namespace SkillEvaluator;

public enum ArtifactKind
{
    Skill,
    Instruction,
    Agent,
}

public enum Severity
{
    Info = 0,
    Warn = 1,
    Blocker = 2,
}

public sealed record Artifact(
    ArtifactKind Kind,
    string Name,
    string Path,
    IReadOnlyDictionary<string, object> Frontmatter,
    string Body,
    IReadOnlyList<string> ReferencedFiles
);

public sealed record Finding(
    Severity Severity,
    string Check,
    string Message,
    IReadOnlyDictionary<string, object>? Data = null
);

public sealed record DimensionScore(int Score, string Rationale);

public sealed record RubricResult(
    IReadOnlyDictionary<string, DimensionScore> Scores,
    string VerdictHint,
    IReadOnlyList<string> TopConcerns,
    IReadOnlyList<string> Strengths,
    string RawResponse
);

public sealed record StaticReport(
    int Score,
    IReadOnlyList<Finding> Findings
)
{
    public bool HasBlocker => Findings.Any(f => f.Severity == Severity.Blocker);
    public int WarningCount => Findings.Count(f => f.Severity == Severity.Warn);
    public IEnumerable<Finding> Blockers => Findings.Where(f => f.Severity == Severity.Blocker);
    public IEnumerable<Finding> Warnings => Findings.Where(f => f.Severity == Severity.Warn);
}

public enum VerdictKind
{
    Accept,
    Revise,
    Reject,
}

public sealed record Verdict(
    VerdictKind Kind,
    double Score,
    IReadOnlyList<string> Reasons
);

public sealed record ArtifactResult(
    Artifact Artifact,
    StaticReport Static,
    RubricResult? Rubric,
    Verdict Verdict
);
