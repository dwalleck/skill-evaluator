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

    [Test]
    [Arguments(200, "compact", Severity.Info)]
    [Arguments(1500, "detailed", Severity.Info)]
    [Arguments(3000, "standard", Severity.Warn)]
    [Arguments(6000, "comprehensive", Severity.Blocker)]
    public async Task TokenTier_classifies_by_count(int targetTokens, string expectedTier, Severity expectedSeverity)
    {
        var body = string.Join(" ", Enumerable.Repeat("word", targetTokens));
        var artifact = new Artifact(
            Kind: ArtifactKind.Skill,
            Name: "x",
            Path: "/tmp/x/SKILL.md",
            Frontmatter: new Dictionary<string, object> { ["name"] = "x", ["description"] = "d" },
            Body: body,
            ReferencedFiles: []
        );

        var report = StaticAnalyzer.Analyze(artifact);

        var finding = report.Findings.Single(f => f.Check == "TokenTier");
        await Assert.That(finding.Message).Contains(expectedTier);
        await Assert.That(finding.Severity).IsEqualTo(expectedSeverity);
    }

    [Test]
    public async Task BodyLength_warns_over_150_lines()
    {
        var body = string.Join("\n", Enumerable.Repeat("line", 160));
        var artifact = new Artifact(
            Kind: ArtifactKind.Instruction,
            Name: "x",
            Path: "/tmp/x.instructions.md",
            Frontmatter: new Dictionary<string, object> { ["description"] = "d", ["applyTo"] = "**/*.cs" },
            Body: body,
            ReferencedFiles: []
        );

        var report = StaticAnalyzer.Analyze(artifact);

        await Assert.That(report.Findings).Contains(f => f.Check == "BodyLength" && f.Severity == Severity.Warn);
    }

    [Test]
    public async Task ApplyToGlob_warns_on_overly_broad_pattern()
    {
        var artifact = new Artifact(
            Kind: ArtifactKind.Instruction,
            Name: "x",
            Path: "/tmp/x.instructions.md",
            Frontmatter: new Dictionary<string, object> { ["description"] = "d", ["applyTo"] = "**/*" },
            Body: "body",
            ReferencedFiles: []
        );

        var report = StaticAnalyzer.Analyze(artifact);

        await Assert.That(report.Findings).Contains(f => f.Check == "ApplyToGlobValidity" && f.Severity == Severity.Warn);
    }
}
