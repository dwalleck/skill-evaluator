using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace SkillEvaluator;

public static class Discovery
{
    private static readonly IDeserializer s_yaml = new DeserializerBuilder().Build();

    private static readonly TimeSpan s_regexTimeout = TimeSpan.FromSeconds(1);

    // Matches `---` delimited YAML frontmatter. Tolerates CRLF line endings by
    // using `\r?\n`; the final body anchor uses `.*` with RegexOptions.Singleline
    // so body content can span lines.
    private static readonly Regex s_frontmatterRx = new(
        @"^---\s*\r?\n(?<yaml>.*?)\r?\n---\s*\r?\n?(?<body>.*)$",
        RegexOptions.Singleline | RegexOptions.Compiled,
        s_regexTimeout);

    private static readonly Regex s_referenceRx = new(
        @"(?:^|[\s(`])(?<path>(?:scripts|references|assets)/[A-Za-z0-9_./-]+)",
        RegexOptions.Compiled,
        s_regexTimeout);

    public static IReadOnlyList<Artifact> DiscoverAll(string root)
    {
        var artifacts = new List<Artifact>();

        // Treat `root` as the skills root if it's named "skills"; otherwise
        // look for a `skills/` subdirectory. Never both — avoids double discovery.
        var skillsDir = Path.GetFileName(root).Equals("skills", StringComparison.OrdinalIgnoreCase)
            ? root
            : Path.Combine(root, "skills");

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

    private static Artifact ParseSkill(string skillMdPath)
    {
        var raw = SafeRead(skillMdPath);
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

    private static Artifact ParseFlatFile(string path, ArtifactKind kind)
    {
        var raw = SafeRead(path);
        var (frontmatter, body) = SplitFrontmatter(raw);
        var fileName = Path.GetFileName(path);
        var suffix = kind switch
        {
            ArtifactKind.Instruction => ".instructions.md",
            ArtifactKind.Agent       => ".agent.md",
            _                        => string.Empty,
        };
        var name = fileName.EndsWith(suffix, StringComparison.Ordinal)
            ? fileName[..^suffix.Length]
            : Path.GetFileNameWithoutExtension(fileName);

        return new Artifact(
            Kind: kind,
            Name: name,
            Path: path,
            Frontmatter: frontmatter,
            Body: body,
            ReferencedFiles: []
        );
    }

    private static string SafeRead(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Surface as a parseable artifact with no frontmatter; FrontmatterPresent
            // will raise blockers and include the file path in the report.
            return $"---\n# IO_ERROR: {ex.Message}\n---\n";
        }
    }

    private static (IReadOnlyDictionary<string, object> Frontmatter, string Body) SplitFrontmatter(string raw)
    {
        var match = s_frontmatterRx.Match(raw);
        if (!match.Success)
        {
            return (new Dictionary<string, object>(StringComparer.Ordinal), raw);
        }

        var yamlText = match.Groups["yaml"].Value;
        var body = match.Groups["body"].Value;

        Dictionary<string, object>? parsed;
        try
        {
            parsed = s_yaml.Deserialize<Dictionary<string, object>>(yamlText);
        }
        catch (YamlException)
        {
            // Malformed YAML on a single file must not crash the whole discovery
            // pass. FrontmatterPresent will surface blockers because required keys
            // are absent; the body is still accessible.
            return (new Dictionary<string, object>(StringComparer.Ordinal), body);
        }

        return (parsed ?? new Dictionary<string, object>(StringComparer.Ordinal), body);
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
