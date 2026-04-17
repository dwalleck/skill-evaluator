namespace SkillEvaluator;

public static class VerdictDeriver
{
    private static readonly IReadOnlyDictionary<string, double> s_weights = new Dictionary<string, double>
    {
        ["trigger_clarity"]       = 0.25,
        ["scope_coherence"]       = 0.20,
        ["instructional_quality"] = 0.20,
        ["generality"]            = 0.20,
        ["safety_trust"]          = 0.15,
    };

    public static Verdict Derive(StaticReport staticReport, RubricResult? rubric)
    {
        if (staticReport.HasBlocker)
        {
            var reasons = staticReport.Blockers.Select(f => f.Message).ToList();
            return new Verdict(VerdictKind.Reject, Score: 0, Reasons: reasons);
        }

        if (rubric is null)
        {
            return staticReport.WarningCount == 0
                ? new Verdict(VerdictKind.Accept, Score: staticReport.Score, Reasons: [])
                : new Verdict(
                    VerdictKind.Revise,
                    Score: staticReport.Score,
                    Reasons: staticReport.Warnings.Select(f => f.Message).ToList());
        }

        var minDim = rubric.Scores.Values.Min(s => s.Score);
        var composite = rubric.Scores.Sum(kv => s_weights[kv.Key] * kv.Value.Score);

        return (minDim, composite) switch
        {
            (<= 2, _) => new Verdict(VerdictKind.Reject, composite, ["Rubric dimension scored <= 2"]),
            (_, >= 3.5) when minDim >= 4 => new Verdict(VerdictKind.Accept, composite, []),
            _ => new Verdict(VerdictKind.Revise, composite, rubric.TopConcerns),
        };
    }
}
