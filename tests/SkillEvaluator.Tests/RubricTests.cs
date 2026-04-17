using SkillEvaluator;

namespace SkillEvaluator.Tests;

public sealed class RubricTests
{
    [Test]
    public async Task Prompt_mentions_artifact_kind_and_content()
    {
        var artifact = new Artifact(
            ArtifactKind.Skill, "x", "/tmp/x/SKILL.md",
            new Dictionary<string, object> { ["name"] = "x", ["description"] = "d" },
            "# hello body", []);

        var prompt = Rubric.BuildUserPrompt(artifact);

        await Assert.That(prompt).Contains("skill");
        await Assert.That(prompt).Contains("hello body");
        await Assert.That(prompt).Contains("trigger_clarity");
    }

    [Test]
    public async Task Parses_valid_rubric_json()
    {
        var json = """
            {
              "trigger_clarity":       { "score": 5, "rationale": "specific" },
              "scope_coherence":       { "score": 4, "rationale": "tight" },
              "instructional_quality": { "score": 4, "rationale": "explains" },
              "generality":            { "score": 4, "rationale": "broad" },
              "safety_trust":          { "score": 5, "rationale": "clean" },
              "verdict_hint": "accept",
              "top_concerns": [],
              "strengths": ["good"]
            }
            """;

        var result = Rubric.ParseResponse(json);

        await Assert.That(result.TriggerClarity.Score).IsEqualTo(5);
        await Assert.That(result.VerdictHint).IsEqualTo("accept");
        await Assert.That(result.Strengths).Contains("good");
    }

    [Test]
    public async Task Strips_markdown_fences_before_parsing()
    {
        var wrapped = "```json\n{ \"trigger_clarity\": { \"score\": 3, \"rationale\": \"\" }, \"scope_coherence\": { \"score\": 3, \"rationale\": \"\" }, \"instructional_quality\": { \"score\": 3, \"rationale\": \"\" }, \"generality\": { \"score\": 3, \"rationale\": \"\" }, \"safety_trust\": { \"score\": 3, \"rationale\": \"\" }, \"verdict_hint\": \"revise\", \"top_concerns\": [], \"strengths\": [] }\n```";

        var result = Rubric.ParseResponse(wrapped);

        await Assert.That(result.VerdictHint).IsEqualTo("revise");
    }

    [Test]
    public async Task Missing_dimension_throws_JsonException()
    {
        // safety_trust omitted — used to NRE on dto.safety_trust.score.
        var json = """
            {
              "trigger_clarity":       { "score": 4, "rationale": "" },
              "scope_coherence":       { "score": 4, "rationale": "" },
              "instructional_quality": { "score": 4, "rationale": "" },
              "generality":            { "score": 4, "rationale": "" },
              "verdict_hint": "accept",
              "top_concerns": [],
              "strengths": []
            }
            """;

        await Assert.That(() => Rubric.ParseResponse(json)).Throws<System.Text.Json.JsonException>();
    }

    [Test]
    public async Task Missing_verdict_hint_throws_JsonException()
    {
        var json = """
            {
              "trigger_clarity":       { "score": 4, "rationale": "" },
              "scope_coherence":       { "score": 4, "rationale": "" },
              "instructional_quality": { "score": 4, "rationale": "" },
              "generality":            { "score": 4, "rationale": "" },
              "safety_trust":          { "score": 4, "rationale": "" },
              "top_concerns": [],
              "strengths": []
            }
            """;

        await Assert.That(() => Rubric.ParseResponse(json)).Throws<System.Text.Json.JsonException>();
    }

    [Test]
    public async Task Out_of_range_score_throws()
    {
        var json = """
            {
              "trigger_clarity":       { "score": 7, "rationale": "" },
              "scope_coherence":       { "score": 4, "rationale": "" },
              "instructional_quality": { "score": 4, "rationale": "" },
              "generality":            { "score": 4, "rationale": "" },
              "safety_trust":          { "score": 4, "rationale": "" },
              "verdict_hint": "accept",
              "top_concerns": [],
              "strengths": []
            }
            """;

        await Assert.That(() => Rubric.ParseResponse(json)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Prompt_reassembles_list_frontmatter_as_YAML_not_dotnet_typename()
    {
        var artifact = new Artifact(
            ArtifactKind.Agent, "x", "/tmp/x.agent.md",
            new Dictionary<string, object>
            {
                ["name"] = "x",
                ["description"] = "d",
                ["tags"] = new List<object> { "a", "b" },
            },
            "body", []);

        var prompt = Rubric.BuildUserPrompt(artifact);

        await Assert.That(prompt).DoesNotContain("System.Collections");
        await Assert.That(prompt).Contains("- a");
    }
}
