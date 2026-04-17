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
            new ArtifactResult(agent, new StaticReport(90, [new Finding(Severity.Warn, CheckKind.AllCapsRatio, "warned thing")]),
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
    public async Task TopConcern_prioritizes_blocker_over_rubric_concern()
    {
        var artifact = new Artifact(ArtifactKind.Skill, "x", "/tmp/x/SKILL.md",
            new Dictionary<string, object> { ["name"] = "x", ["description"] = "d" }, "body", []);
        var rubric = new RubricResult(
            TriggerClarity:       new DimensionScore(4, ""),
            ScopeCoherence:       new DimensionScore(4, ""),
            InstructionalQuality: new DimensionScore(4, ""),
            Generality:           new DimensionScore(4, ""),
            SafetyTrust:          new DimensionScore(4, ""),
            VerdictHint: "accept",
            TopConcerns: ["rubric concern"],
            Strengths: [],
            RawResponse: "{}"
        );
        var staticReport = new StaticReport(
            0,
            [new Finding(Severity.Blocker, CheckKind.FrontmatterPresent, "static blocker"),
             new Finding(Severity.Warn, CheckKind.AllCapsRatio, "static warn")]
        );
        var results = new[]
        {
            new ArtifactResult(artifact, staticReport, rubric, Verdict.Reject(["static blocker"]), null),
        };

        var md = Reporter.BuildMarkdown(results, "anthropic", null, TimeSpan.Zero);

        // At-a-glance row should show the blocker, not the rubric concern.
        var atGlanceStart = md.IndexOf("## At a glance", StringComparison.Ordinal);
        var atGlanceEnd = md.IndexOf("## Rejects", StringComparison.Ordinal);
        var atGlance = md[atGlanceStart..atGlanceEnd];
        await Assert.That(atGlance).Contains("static blocker");
        await Assert.That(atGlance).DoesNotContain("rubric concern");
    }

    [Test]
    public async Task TopConcern_falls_back_to_rubric_when_no_blocker()
    {
        var artifact = new Artifact(ArtifactKind.Skill, "x", "/tmp/x/SKILL.md",
            new Dictionary<string, object> { ["name"] = "x", ["description"] = "d" }, "body", []);
        var rubric = new RubricResult(
            TriggerClarity:       new DimensionScore(3, ""),
            ScopeCoherence:       new DimensionScore(4, ""),
            InstructionalQuality: new DimensionScore(4, ""),
            Generality:           new DimensionScore(4, ""),
            SafetyTrust:          new DimensionScore(4, ""),
            VerdictHint: "revise",
            TopConcerns: ["rubric concern"],
            Strengths: [],
            RawResponse: "{}"
        );
        var staticReport = new StaticReport(
            90,
            [new Finding(Severity.Warn, CheckKind.AllCapsRatio, "static warn")]
        );
        var results = new[]
        {
            new ArtifactResult(artifact, staticReport, rubric, Verdict.Revise(3.5, ["rubric concern"]), null),
        };

        var md = Reporter.BuildMarkdown(results, "anthropic", null, TimeSpan.Zero);

        var atGlanceStart = md.IndexOf("## At a glance", StringComparison.Ordinal);
        var atGlanceEnd = md.IndexOf("## Revises", StringComparison.Ordinal);
        var atGlance = md[atGlanceStart..atGlanceEnd];
        await Assert.That(atGlance).Contains("rubric concern");
        await Assert.That(atGlance).DoesNotContain("static warn");
    }

    [Test]
    public async Task At_a_glance_is_skipped_on_empty_results()
    {
        var md = Reporter.BuildMarkdown([], "none", null, TimeSpan.Zero);

        await Assert.That(md).DoesNotContain("## At a glance");
        await Assert.That(md).Contains("# Skill Evaluator Report");
    }

    [Test]
    public async Task Json_omits_rubric_and_provider_error_when_null()
    {
        var artifact = new Artifact(ArtifactKind.Skill, "x", "/tmp/x/SKILL.md",
            new Dictionary<string, object> { ["name"] = "x", ["description"] = "d" }, "body", []);
        var results = new[]
        {
            new ArtifactResult(artifact, new StaticReport(100, []), null, Verdict.Accept(100), null),
        };

        var json = Reporter.BuildJson(results, "none", null, TimeSpan.Zero);

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var artifactEl = doc.RootElement.GetProperty("artifacts")[0];
        await Assert.That(artifactEl.TryGetProperty("rubric", out _)).IsFalse();
        await Assert.That(artifactEl.TryGetProperty("provider_error", out _)).IsFalse();
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
