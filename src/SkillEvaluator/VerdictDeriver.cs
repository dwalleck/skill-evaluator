namespace SkillEvaluator;

public static class VerdictDeriver
{
    // Weights must sum to 1.0. Source: design doc
    // (docs/plans/2026-04-16-skill-evaluator-design.md §"Rubric score composition").
    private const double TriggerClarityWeight       = 0.25;
    private const double ScopeCoherenceWeight       = 0.20;
    private const double InstructionalQualityWeight = 0.20;
    private const double GeneralityWeight           = 0.20;
    private const double SafetyTrustWeight          = 0.15;

    // Acceptance thresholds. Source: design doc §"Verdict derivation".
    private const int MinDimensionForAccept = 4;
    private const int RejectIfAnyDimBelow   = 3;   // any dim <= 2 is reject
    private const double MinCompositeForAccept = 3.5;

    public static Verdict Derive(StaticReport staticReport, RubricResult? rubric)
    {
        if (staticReport.HasBlocker)
        {
            return Verdict.Reject(staticReport.Blockers.Select(f => f.Message).ToList());
        }

        if (rubric is null)
        {
            return staticReport.WarningCount == 0
                ? Verdict.Accept(staticReport.Score)
                : Verdict.Revise(staticReport.Score, staticReport.Warnings.Select(f => f.Message).ToList());
        }

        var composite =
            TriggerClarityWeight       * rubric.TriggerClarity.Score
            + ScopeCoherenceWeight       * rubric.ScopeCoherence.Score
            + InstructionalQualityWeight * rubric.InstructionalQuality.Score
            + GeneralityWeight           * rubric.Generality.Score
            + SafetyTrustWeight          * rubric.SafetyTrust.Score;

        var minDim = rubric.MinScore;

        return (minDim, composite) switch
        {
            ( < RejectIfAnyDimBelow, _)                     => Verdict.Reject(["Rubric dimension scored below 3"]),
            ( >= MinDimensionForAccept, >= MinCompositeForAccept) => Verdict.Accept(composite),
            _                                               => Verdict.Revise(composite, rubric.TopConcerns),
        };
    }
}
