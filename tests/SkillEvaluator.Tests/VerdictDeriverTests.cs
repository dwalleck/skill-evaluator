using SkillEvaluator;

namespace SkillEvaluator.Tests;

public sealed class VerdictDeriverTests
{
    private static StaticReport CleanStatic => new(Score: 100, Findings: []);
    private static StaticReport WithBlocker => new(
        Score: 0,
        Findings: [new Finding(Severity.Blocker, CheckKind.FrontmatterPresent, "broken")]
    );

    private static RubricResult BuildRubric(int common) =>
        new(
            TriggerClarity:       new DimensionScore(common, ""),
            ScopeCoherence:       new DimensionScore(common, ""),
            InstructionalQuality: new DimensionScore(common, ""),
            Generality:           new DimensionScore(common, ""),
            SafetyTrust:          new DimensionScore(common, ""),
            VerdictHint: "accept",
            TopConcerns: [],
            Strengths: [],
            RawResponse: "{}"
        );

    [Test]
    public async Task Blocker_rejects_regardless_of_rubric()
    {
        var v = VerdictDeriver.Derive(WithBlocker, BuildRubric(5));
        await Assert.That(v.Kind).IsEqualTo(VerdictKind.Reject);
    }

    [Test]
    public async Task Static_only_clean_accepts()
    {
        var v = VerdictDeriver.Derive(CleanStatic, rubric: null);
        await Assert.That(v.Kind).IsEqualTo(VerdictKind.Accept);
    }

    [Test]
    public async Task Rubric_dim_of_2_rejects()
    {
        var rubric = BuildRubric(5) with
        {
            TriggerClarity = new DimensionScore(2, "weak"),
        };
        var v = VerdictDeriver.Derive(CleanStatic, rubric);
        await Assert.That(v.Kind).IsEqualTo(VerdictKind.Reject);
    }

    [Test]
    public async Task High_composite_with_all_min_4_accepts()
    {
        var v = VerdictDeriver.Derive(CleanStatic, BuildRubric(4));
        await Assert.That(v.Kind).IsEqualTo(VerdictKind.Accept);
    }

    [Test]
    public async Task Mixed_scores_revise()
    {
        var rubric = BuildRubric(4) with
        {
            TriggerClarity = new DimensionScore(3, ""),
        };
        var v = VerdictDeriver.Derive(CleanStatic, rubric);
        await Assert.That(v.Kind).IsEqualTo(VerdictKind.Revise);
    }
}
