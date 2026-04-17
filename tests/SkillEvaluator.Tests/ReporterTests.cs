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
    public async Task At_a_glance_table_includes_each_artifact()
    {
        var skill = new Artifact(ArtifactKind.Skill, "a", "/tmp/a/SKILL.md",
            new Dictionary<string, object> { ["name"] = "a", ["description"] = "d" }, "body", []);
        var agent = new Artifact(ArtifactKind.Agent, "b", "/tmp/b.agent.md",
            new Dictionary<string, object> { ["name"] = "b", ["description"] = "d" }, "body", []);
        var results = new[]
        {
            new ArtifactResult(skill, new StaticReport(100, []), null, Verdict.Accept(100), null),
            new ArtifactResult(agent, new StaticReport(90, [new Finding(Severity.Warn, "W", "warned thing")]),
                null, Verdict.Revise(90, []), null),
        };

        var md = Reporter.BuildMarkdown(results, "none", null, TimeSpan.Zero);

        await Assert.That(md).Contains("## At a glance");
        await Assert.That(md).Contains("| a | skill |");
        await Assert.That(md).Contains("| b | agent |");
        await Assert.That(md).Contains("warned thing");
    }

    [Test]
    public async Task Json_report_has_schema_version_and_summary_counts()
    {
        var artifact = new Artifact(ArtifactKind.Skill, "x", "/tmp/x/SKILL.md",
            new Dictionary<string, object> { ["name"] = "x", ["description"] = "d" }, "body", []);
        var results = new[]
        {
            new ArtifactResult(artifact, new StaticReport(100, []), null, Verdict.Accept(100), ProviderError: null),
        };

        var json = Reporter.BuildJson(results, "none", null, TimeSpan.FromSeconds(3));

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        await Assert.That(root.GetProperty("schema_version").GetInt32()).IsEqualTo(1);
        await Assert.That(root.GetProperty("summary").GetProperty("by_verdict").GetProperty("accept").GetInt32()).IsEqualTo(1);
        await Assert.That(root.GetProperty("artifacts")[0].GetProperty("verdict").GetProperty("kind").GetString()).IsEqualTo("accept");
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
