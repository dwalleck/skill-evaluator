using SkillEvaluator;

namespace SkillEvaluator.Tests;

public sealed class ModelsTests
{
    [Test]
    public async Task Artifact_exposes_kind_name_and_frontmatter()
    {
        var artifact = new Artifact(
            Kind: ArtifactKind.Skill,
            Name: "demo",
            Path: "/tmp/demo/SKILL.md",
            Frontmatter: new Dictionary<string, object> { ["description"] = "a thing" },
            Body: "# body",
            ReferencedFiles: []
        );

        await Assert.That(artifact.Kind).IsEqualTo(ArtifactKind.Skill);
        await Assert.That(artifact.Name).IsEqualTo("demo");
        await Assert.That(artifact.Frontmatter["description"]).IsEqualTo("a thing");
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(6)]
    [Arguments(100)]
    public async Task DimensionScore_rejects_out_of_range(int score)
    {
        await Assert.That(() => new DimensionScore(score, "r")).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments(1)]
    [Arguments(3)]
    [Arguments(5)]
    public async Task DimensionScore_accepts_1_through_5(int score)
    {
        var dim = new DimensionScore(score, "r");
        await Assert.That(dim.Score).IsEqualTo(score);
    }

    [Test]
    public async Task Verdict_Accept_has_zero_reasons()
    {
        var v = Verdict.Accept(95.5);
        await Assert.That(v.Kind).IsEqualTo(VerdictKind.Accept);
        await Assert.That(v.Score).IsEqualTo(95.5);
        await Assert.That(v.Reasons.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Verdict_Reject_has_zero_score()
    {
        var v = Verdict.Reject(["bad"]);
        await Assert.That(v.Kind).IsEqualTo(VerdictKind.Reject);
        await Assert.That(v.Score).IsEqualTo(0);
        await Assert.That(v.Reasons).Contains("bad");
    }
}
