using SkillEvaluator;

namespace SkillEvaluator.Tests;

public sealed class DiscoveryTests
{
    private static string FixturesDir => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    [Test]
    public async Task Discovers_skill_from_SKILL_md()
    {
        var skillsRoot = Path.Combine(FixturesDir, "skills");
        var artifacts = Discovery.DiscoverAll(skillsRoot);

        var skill = artifacts.Single(a => a.Kind == ArtifactKind.Skill);
        await Assert.That(skill.Name).IsEqualTo("demo-skill");
        await Assert.That(skill.Frontmatter["description"]).IsEqualTo("A demo skill used for discovery tests.");
        await Assert.That(skill.Body).Contains("# Demo skill");
        await Assert.That(skill.ReferencedFiles).Contains(item => item.EndsWith("scripts/run.py", StringComparison.Ordinal));
    }
}
