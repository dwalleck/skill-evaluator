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
}
