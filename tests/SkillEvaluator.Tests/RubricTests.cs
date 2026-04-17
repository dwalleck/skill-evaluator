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
}
