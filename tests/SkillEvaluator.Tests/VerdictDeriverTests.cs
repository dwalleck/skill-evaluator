using SkillEvaluator;

namespace SkillEvaluator.Tests;

public sealed class VerdictDeriverTests
{
    private static StaticReport CleanStatic => new(Score: 100, Findings: []);
    private static StaticReport WithBlocker => new(
        Score: 0,
        Findings: [new Finding(Severity.Blocker, "X", "broken")]
    );

    private static RubricResult BuildRubric(int common) =>
        new(
            Scores: new Dictionary<string, DimensionScore>
            {
                ["trigger_clarity"]       = new(common, ""),
                ["scope_coherence"]       = new(common, ""),
                ["instructional_quality"] = new(common, ""),
                ["generality"]            = new(common, ""),
                ["safety_trust"]          = new(common, ""),
            },
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
            Scores = new Dictionary<string, DimensionScore>
            {
                ["trigger_clarity"]       = new(2, "weak"),
                ["scope_coherence"]       = new(5, ""),
                ["instructional_quality"] = new(5, ""),
                ["generality"]            = new(5, ""),
                ["safety_trust"]          = new(5, ""),
            },
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
            Scores = new Dictionary<string, DimensionScore>
            {
                ["trigger_clarity"]       = new(3, ""),
                ["scope_coherence"]       = new(4, ""),
                ["instructional_quality"] = new(4, ""),
                ["generality"]            = new(4, ""),
                ["safety_trust"]          = new(4, ""),
            },
        };
        var v = VerdictDeriver.Derive(CleanStatic, rubric);
        await Assert.That(v.Kind).IsEqualTo(VerdictKind.Revise);
    }
}
