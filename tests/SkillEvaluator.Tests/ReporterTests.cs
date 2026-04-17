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

    [Test]
    public async Task Pipe_in_rationale_is_escaped_to_preserve_table()
    {
        var rubric = new RubricResult(
            TriggerClarity:       new DimensionScore(3, "unclear | ambiguous"),
            ScopeCoherence:       new DimensionScore(4, "ok"),
            InstructionalQuality: new DimensionScore(4, "ok"),
            Generality:           new DimensionScore(4, "ok"),
            SafetyTrust:          new DimensionScore(4, "ok"),
            VerdictHint: "revise",
            TopConcerns: [],
            Strengths: [],
            RawResponse: "{}"
        );
        var artifact = new Artifact(ArtifactKind.Skill, "x", "/tmp/x/SKILL.md",
            new Dictionary<string, object> { ["name"] = "x", ["description"] = "d" }, "body", []);
        var results = new[]
        {
            new ArtifactResult(artifact, new StaticReport(100, []), rubric, Verdict.Revise(3.6, []), ProviderError: null),
        };

        var md = Reporter.BuildMarkdown(results, "anthropic", "claude-sonnet-4-6", TimeSpan.Zero);

        await Assert.That(md).Contains("unclear \\| ambiguous");
        await Assert.That(md).DoesNotContain("unclear | ambiguous");
    }

    [Test]
    public async Task ProviderError_is_surfaced_when_present()
    {
        var artifact = new Artifact(ArtifactKind.Skill, "x", "/tmp/x/SKILL.md",
            new Dictionary<string, object> { ["name"] = "x", ["description"] = "d" }, "body", []);
        var results = new[]
        {
            new ArtifactResult(
                artifact,
                new StaticReport(100, []),
                Rubric: null,
                Verdict.Revise(100, []),
                ProviderError: "Anthropic API 401: invalid x-api-key"
            ),
        };

        var md = Reporter.BuildMarkdown(results, "anthropic", null, TimeSpan.Zero);

        await Assert.That(md).Contains("Provider error");
        await Assert.That(md).Contains("401: invalid x-api-key");
    }
}
