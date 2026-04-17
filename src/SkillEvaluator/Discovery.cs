using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace SkillEvaluator;

public static class Discovery
{
    private static readonly IDeserializer s_yaml = new DeserializerBuilder().Build();
    private static readonly Regex s_frontmatterRx = new(
        @"^---\s*\n(?<yaml>.*?)\n---\s*\n(?<body>.*)$",
        RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex s_referenceRx = new(
        @"(?:^|[\s(`])(?<path>(?:scripts|references|assets)/[A-Za-z0-9_./-]+)",
        RegexOptions.Compiled);

    public static IReadOnlyList<Artifact> DiscoverAll(string root)
    {
        var artifacts = new List<Artifact>();

        var skillsDir = Path.Combine(root, "skills");
        if (Directory.Exists(skillsDir))
        {
            foreach (var dir in Directory.EnumerateDirectories(skillsDir))
            {
                var skillMd = Path.Combine(dir, "SKILL.md");
                if (File.Exists(skillMd))
                {
                    artifacts.Add(ParseSkill(skillMd));
                }
            }
        }

        if (Path.GetFileName(root).Equals("skills", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var skillMd = Path.Combine(dir, "SKILL.md");
                if (File.Exists(skillMd))
                {
                    artifacts.Add(ParseSkill(skillMd));
                }
            }
        }

        var instructionsDir = Path.Combine(root, "instructions");
        if (Directory.Exists(instructionsDir))
        {
            foreach (var file in Directory.EnumerateFiles(instructionsDir, "*.instructions.md"))
            {
                artifacts.Add(ParseFlatFile(file, ArtifactKind.Instruction));
            }
        }

        var agentsDir = Path.Combine(root, "agents");
        if (Directory.Exists(agentsDir))
        {
            foreach (var file in Directory.EnumerateFiles(agentsDir, "*.agent.md"))
            {
                artifacts.Add(ParseFlatFile(file, ArtifactKind.Agent));
            }
        }

        return artifacts;
    }

    private static Artifact ParseFlatFile(string path, ArtifactKind kind)
    {
        var raw = File.ReadAllText(path);
        var (frontmatter, body) = SplitFrontmatter(raw);
        var fileName = Path.GetFileName(path);
        var name = kind switch
        {
            ArtifactKind.Instruction => fileName.Replace(".instructions.md", ""),
            ArtifactKind.Agent       => fileName.Replace(".agent.md", ""),
            _                        => fileName,
        };

        return new Artifact(
            Kind: kind,
            Name: name,
            Path: path,
            Frontmatter: frontmatter,
            Body: body,
            ReferencedFiles: []
        );
    }

    private static Artifact ParseSkill(string skillMdPath)
    {
        var raw = File.ReadAllText(skillMdPath);
        var (frontmatter, body) = SplitFrontmatter(raw);
        var skillDir = Path.GetDirectoryName(skillMdPath)!;
        var referenced = FindReferencedFiles(body, skillDir);
        var name = frontmatter.TryGetValue("name", out var n) && n is string s
            ? s
            : Path.GetFileName(skillDir);

        return new Artifact(
            Kind: ArtifactKind.Skill,
            Name: name,
            Path: skillMdPath,
            Frontmatter: frontmatter,
            Body: body,
            ReferencedFiles: referenced
        );
    }

    private static (IReadOnlyDictionary<string, object> Frontmatter, string Body) SplitFrontmatter(string raw)
    {
        var match = s_frontmatterRx.Match(raw);
        if (!match.Success)
        {
            return (new Dictionary<string, object>(), raw);
        }

        var yamlText = match.Groups["yaml"].Value;
        var body = match.Groups["body"].Value;
        var parsed = s_yaml.Deserialize<Dictionary<string, object>>(yamlText) ?? new Dictionary<string, object>();
        return (parsed, body);
    }

    private static IReadOnlyList<string> FindReferencedFiles(string body, string skillDir)
    {
        var refs = new List<string>();
        foreach (Match m in s_referenceRx.Matches(body))
        {
            var relPath = m.Groups["path"].Value.TrimEnd('.', ',', ')', '`');
            refs.Add(Path.Combine(skillDir, relPath));
        }
        return refs;
    }
}
