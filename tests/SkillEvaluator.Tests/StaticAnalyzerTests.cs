using SkillEvaluator;

namespace SkillEvaluator.Tests;

public sealed class StaticAnalyzerTests
{
    private static Artifact SkillWithFrontmatter(Dictionary<string, object> fm)
    {
        return new Artifact(
            Kind: ArtifactKind.Skill,
            Name: "x",
            Path: "/tmp/x/SKILL.md",
            Frontmatter: fm,
            Body: "body",
            ReferencedFiles: []
        );
    }

    [Test]
    public async Task Skill_without_name_is_blocker()
    {
        var artifact = SkillWithFrontmatter(new() { ["description"] = "d" });

        var report = StaticAnalyzer.Analyze(artifact);

        await Assert.That(report.HasBlocker).IsTrue();
        await Assert.That(report.Findings).Contains(f => f.Check == "FrontmatterPresent");
    }

    [Test]
    public async Task Instruction_without_applyTo_is_blocker()
    {
        var artifact = new Artifact(
            Kind: ArtifactKind.Instruction,
            Name: "x",
            Path: "/tmp/x.instructions.md",
            Frontmatter: new Dictionary<string, object> { ["description"] = "d" },
            Body: "body",
            ReferencedFiles: []
        );

        var report = StaticAnalyzer.Analyze(artifact);

        await Assert.That(report.HasBlocker).IsTrue();
    }

    [Test]
    public async Task Complete_frontmatter_no_blocker()
    {
        var artifact = SkillWithFrontmatter(new() { ["name"] = "x", ["description"] = "d" });

        var report = StaticAnalyzer.Analyze(artifact);

        await Assert.That(report.HasBlocker).IsFalse();
    }
}
