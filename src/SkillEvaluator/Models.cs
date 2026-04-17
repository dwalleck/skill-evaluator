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
    string Message
);

public sealed record DimensionScore
{
    public int Score { get; }
    public string Rationale { get; }

    public DimensionScore(int score, string rationale)
    {
        if (score is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(score), score, "Dimension score must be 1-5.");
        }
        Score = score;
        Rationale = rationale;
    }
}

public sealed record RubricResult(
    DimensionScore TriggerClarity,
    DimensionScore ScopeCoherence,
    DimensionScore InstructionalQuality,
    DimensionScore Generality,
    DimensionScore SafetyTrust,
    string VerdictHint,
    IReadOnlyList<string> TopConcerns,
    IReadOnlyList<string> Strengths,
    string RawResponse
)
{
    public int MinScore =>
        Math.Min(
            Math.Min(Math.Min(TriggerClarity.Score, ScopeCoherence.Score),
                     Math.Min(InstructionalQuality.Score, Generality.Score)),
            SafetyTrust.Score
        );
}

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

public sealed record Verdict
{
    public VerdictKind Kind { get; }
    public double Score { get; }
    public IReadOnlyList<string> Reasons { get; }

    private Verdict(VerdictKind kind, double score, IReadOnlyList<string> reasons)
    {
        Kind = kind;
        Score = score;
        Reasons = reasons;
    }

    public static Verdict Accept(double score) =>
        new(VerdictKind.Accept, score, []);

    public static Verdict Revise(double score, IReadOnlyList<string> concerns) =>
        new(VerdictKind.Revise, score, concerns);

    public static Verdict Reject(IReadOnlyList<string> reasons) =>
        new(VerdictKind.Reject, 0, reasons);
}

public sealed record ArtifactResult(
    Artifact Artifact,
    StaticReport Static,
    RubricResult? Rubric,
    Verdict Verdict,
    string? ProviderError
);
