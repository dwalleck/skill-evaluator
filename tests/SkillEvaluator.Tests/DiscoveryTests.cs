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

    [Test]
    public async Task CRLF_line_endings_in_frontmatter_still_parse()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var skillDir = Path.Combine(tmp, "skills", "crlf");
            Directory.CreateDirectory(skillDir);
            var content = "---\r\nname: crlf\r\ndescription: Windows-authored skill\r\n---\r\n\r\n# Body\r\n";
            File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), content);

            var artifacts = Discovery.DiscoverAll(tmp);

            var skill = artifacts.Single();
            await Assert.That(skill.Frontmatter).ContainsKey("name");
            await Assert.That(skill.Frontmatter["name"]).IsEqualTo("crlf");
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Test]
    public async Task Malformed_YAML_does_not_crash_discovery()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var badSkill = Path.Combine(tmp, "skills", "broken");
            var goodSkill = Path.Combine(tmp, "skills", "fine");
            Directory.CreateDirectory(badSkill);
            Directory.CreateDirectory(goodSkill);

            // Tab-indented YAML + unquoted colon in value → YamlException.
            File.WriteAllText(
                Path.Combine(badSkill, "SKILL.md"),
                "---\n\tname: broken\ndescription: has: a: colon: problem\n---\nbody\n"
            );
            File.WriteAllText(
                Path.Combine(goodSkill, "SKILL.md"),
                "---\nname: fine\ndescription: clean\n---\nbody\n"
            );

            var artifacts = Discovery.DiscoverAll(tmp);

            // Both artifacts are discovered; the malformed one has empty
            // frontmatter so FrontmatterPresent will raise blockers later.
            await Assert.That(artifacts.Count).IsEqualTo(2);
            await Assert.That(artifacts).Contains(a => a.Name == "fine");
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Test]
    public async Task Root_passed_as_skills_dir_directly_does_not_double_discover()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var skillsDir = Path.Combine(tmp, "skills");
            var one = Path.Combine(skillsDir, "one");
            Directory.CreateDirectory(one);
            File.WriteAllText(
                Path.Combine(one, "SKILL.md"),
                "---\nname: one\ndescription: d\n---\nbody\n"
            );

            var asSkillsRoot = Discovery.DiscoverAll(skillsDir);
            var asParentRoot = Discovery.DiscoverAll(tmp);

            await Assert.That(asSkillsRoot.Count).IsEqualTo(1);
            await Assert.That(asParentRoot.Count).IsEqualTo(1);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }
}
