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

    [Test]
    public async Task Discovers_instruction_files()
    {
        var artifacts = Discovery.DiscoverAll(FixturesDir);

        var inst = artifacts.Single(a => a.Kind == ArtifactKind.Instruction);
        await Assert.That(inst.Name).IsEqualTo("demo");
        await Assert.That(inst.Frontmatter["applyTo"]).IsEqualTo("**/*.cs");
    }

    [Test]
    public async Task Discovers_agent_files()
    {
        var artifacts = Discovery.DiscoverAll(FixturesDir);

        var agent = artifacts.Single(a => a.Kind == ArtifactKind.Agent);
        await Assert.That(agent.Frontmatter["name"]).IsEqualTo("Demo Agent");
    }
}
