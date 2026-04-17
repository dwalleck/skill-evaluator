using SkillEvaluator;

namespace SkillEvaluator.Tests;

public sealed class ReporterTests
{
    [Test]
    public async Task Markdown_report_includes_verdict_table()
    {
        var artifact = new Artifact(ArtifactKind.Skill, "x", "/tmp/x/SKILL.md",
            new Dictionary<string, object> { ["name"] = "x", ["description"] = "d" }, "body", []);
        var staticReport = new StaticReport(100, []);
        var verdict = Verdict.Accept(100);
        var results = new[] { new ArtifactResult(artifact, staticReport, null, verdict, ProviderError: null) };

        var md = Reporter.BuildMarkdown(results, provider: "none", model: null, duration: TimeSpan.FromSeconds(1));

        await Assert.That(md).Contains("# Skill Evaluator Report");
        await Assert.That(md).Contains("| ✅ Accept | 1");
    }
}
