using System.Text.RegularExpressions;
using Microsoft.ML.Tokenizers;

namespace SkillEvaluator;

public static class StaticAnalyzer
{
    private static readonly TiktokenTokenizer s_tokenizer =
        TiktokenTokenizer.CreateForEncoding("cl100k_base");

    // Token-tier thresholds. Source: design doc
    // (docs/plans/2026-04-16-skill-evaluator-design.md §"Static-check catalog").
    private const int CompactMax       = 400;   // < 400 tokens = compact (Info)
    private const int DetailedMax      = 2500;  // [400, 2500] = detailed (Info)
    private const int StandardMax      = 5000;  // (2500, 5000] = standard (Warn)
                                                // > 5000 = comprehensive (Blocker)

    // Ratio thresholds per 1000 words. Source: design doc §"Static-check catalog".
    private const double ImperativeWarnPer1000 = 20.0;
    private const double AllCapsWarnPer1000    = 15.0;

    // Description length bounds (chars). Source: design doc §"Static-check catalog".
    private const int DescriptionMin = 40;
    private const int DescriptionMax = 500;

    private static readonly Regex s_imperativeRx = new(
        @"\b(MUST|NEVER|ALWAYS|REQUIRED)\b",
        RegexOptions.Compiled);

    private static readonly Regex s_allCapsRx = new(
        @"\b[A-Z]{3,}\b",
        RegexOptions.Compiled);

    private static readonly Regex s_markdownLinkRx = new(
        @"\[[^\]]+\]\(([^)]+)\)",
        RegexOptions.Compiled);

    // Technical initialisms that read as all-caps but aren't stylistic shouting.
    // Extended after real-world smoke-testing against dotnet/skills: AOT, CRLF,
    // NET, SDK, etc. showed up as false positives on legitimate technical content.
    private static readonly HashSet<string> s_allCapsAllowlist = new(StringComparer.Ordinal)
    {
        // Protocols / formats
        "HTTP", "HTTPS", "JSON", "YAML", "XML", "HTML", "CSS", "SQL",
        "CSV", "TSV", "UUID", "UTF", "ASCII", "RFC", "REST", "RPC",
        // Surfaces
        "API", "CLI", "URL", "URI", "UI", "GUI", "MCP", "IDE", "OS",
        // Platform / runtime
        "NET", "SDK", "JVM", "CPU", "GPU", "RAM", "OS",
        // Runtime concepts
        "AOT", "JIT", "TLS", "SSL", "GC", "IO", "DI", "JWT",
        // Linguistic / format
        "CRLF", "LF", "BOM", "DTO", "POCO",
        // VCS / ops
        "CI", "CD", "PR", "SSH", "DNS", "VPN",
    };

    // Script security/behavior heuristics (Info-only).
    private static readonly (string Pattern, string Flag)[] s_scriptFlags =
    [
        (@"\beval\s*\(", "eval"),
        (@"\bexec\s*\(", "exec"),
        (@"\bsubprocess\b", "subprocess"),
        (@"shell\s*=\s*True", "shell=True"),
        (@"\bos\.system\s*\(", "os.system"),
        (@"^\s*import\s+(requests|urllib|http\.client|socket)\b", "network-import"),
        (@"^\s*from\s+(requests|urllib|http\.client|socket)\s+import\b", "network-import"),
    ];

    public static StaticReport Analyze(Artifact artifact)
    {
        var findings = new List<Finding>();

        findings.AddRange(CheckFrontmatterPresent(artifact));

        if (artifact.Kind is ArtifactKind.Skill or ArtifactKind.Agent)
        {
            findings.AddRange(CheckTokenTier(artifact));
            findings.AddRange(CheckDescriptionLength(artifact));
        }

        if (artifact.Kind == ArtifactKind.Instruction)
        {
            findings.AddRange(CheckBodyLength(artifact));
            findings.AddRange(CheckApplyToGlob(artifact));
        }

        if (artifact.Kind == ArtifactKind.Skill)
        {
            findings.AddRange(CheckReferencedFilesExist(artifact));
            findings.AddRange(CheckScriptInventory(artifact));
        }

        findings.AddRange(CheckImperativeSmellRatio(artifact));
        findings.AddRange(CheckAllCapsRatio(artifact));
        findings.AddRange(CheckInternalLinksResolve(artifact));

        var warnings = findings.Count(f => f.Severity == Severity.Warn);
        var score = Math.Max(0, 100 - warnings * 5);
        return new StaticReport(Score: score, Findings: findings);
    }

    private static IEnumerable<Finding> CheckFrontmatterPresent(Artifact artifact)
    {
        var required = artifact.Kind switch
        {
            ArtifactKind.Skill       => new[] { "name", "description" },
            ArtifactKind.Agent       => new[] { "name", "description" },
            ArtifactKind.Instruction => new[] { "description", "applyTo" },
            _                        => Array.Empty<string>(),
        };

        foreach (var key in required)
        {
            if (!artifact.Frontmatter.ContainsKey(key))
            {
                yield return new Finding(
                    Severity: Severity.Blocker,
                    Check: "FrontmatterPresent",
                    Message: $"Missing required frontmatter field: {key}"
                );
            }
        }
    }

    private static IEnumerable<Finding> CheckTokenTier(Artifact artifact)
    {
        var fullText = artifact.Reassemble();
        var tokens = s_tokenizer.CountTokens(fullText);

        var (tier, severity) = tokens switch
        {
            < CompactMax  => ("compact", Severity.Info),
            <= DetailedMax => ("detailed", Severity.Info),
            <= StandardMax => ("standard", Severity.Warn),
            _              => ("comprehensive", Severity.Blocker),
        };

        yield return new Finding(
            Severity: severity,
            Check: "TokenTier",
            Message: $"{tokens} tokens ({tier} tier)"
        );
    }

    private static IEnumerable<Finding> CheckBodyLength(Artifact artifact)
    {
        // Count lines without double-counting the trailing empty element when
        // Body ends in '\n'. TrimEnd('\n') means "150 real lines" reports 150.
        var lines = artifact.Body.TrimEnd('\n').Split('\n').Length;
        if (lines > 150)
        {
            yield return new Finding(Severity.Warn, "BodyLength", $"Body is {lines} lines (>150)");
        }
        else if (lines >= 50)
        {
            yield return new Finding(Severity.Info, "BodyLength", $"Body is {lines} lines");
        }
    }

    private static IEnumerable<Finding> CheckApplyToGlob(Artifact artifact)
    {
        if (!artifact.Frontmatter.TryGetValue("applyTo", out var val) || val is not string glob)
        {
            yield break;
        }

        var trimmed = glob.Trim();
        if (trimmed is "**/*" or "**")
        {
            yield return new Finding(Severity.Warn, "ApplyToGlobValidity", $"Overly broad applyTo glob: {glob}");
        }
    }

    private static IEnumerable<Finding> CheckReferencedFilesExist(Artifact artifact)
    {
        foreach (var referenced in artifact.ReferencedFiles)
        {
            if (!File.Exists(referenced))
            {
                yield return new Finding(
                    Severity: Severity.Blocker,
                    Check: "ReferencedFilesExist",
                    Message: $"Referenced file does not exist: {referenced}"
                );
            }
        }
    }

    private static IEnumerable<Finding> CheckDescriptionLength(Artifact artifact)
    {
        if (!artifact.Frontmatter.TryGetValue("description", out var val) || val is not string description)
        {
            yield break;
        }

        var length = description.Length;
        if (length < DescriptionMin)
        {
            yield return new Finding(Severity.Warn, "DescriptionLength", $"Description is {length} chars (<{DescriptionMin})");
        }
        else if (length > DescriptionMax)
        {
            yield return new Finding(Severity.Warn, "DescriptionLength", $"Description is {length} chars (>{DescriptionMax})");
        }
    }

    private static IEnumerable<Finding> CheckImperativeSmellRatio(Artifact artifact)
    {
        var wordCount = CountWords(artifact.Body);
        if (wordCount == 0)
        {
            yield break;
        }

        var matches = s_imperativeRx.Matches(artifact.Body).Count;
        var ratio = matches * 1000.0 / wordCount;
        var severity = ratio > ImperativeWarnPer1000 ? Severity.Warn : Severity.Info;
        yield return new Finding(
            severity,
            "ImperativeSmellRatio",
            $"{ratio:F1} imperatives per 1000 words ({matches} of MUST/NEVER/ALWAYS/REQUIRED in {wordCount} words)"
        );
    }

    private static IEnumerable<Finding> CheckAllCapsRatio(Artifact artifact)
    {
        var wordCount = CountWords(artifact.Body);
        if (wordCount == 0)
        {
            yield break;
        }

        var matches = s_allCapsRx.Matches(artifact.Body)
            .Count(m => !s_allCapsAllowlist.Contains(m.Value));
        var ratio = matches * 1000.0 / wordCount;
        var severity = ratio > AllCapsWarnPer1000 ? Severity.Warn : Severity.Info;
        yield return new Finding(
            severity,
            "AllCapsRatio",
            $"{ratio:F1} all-caps words per 1000 words ({matches} in {wordCount} words, excluding common initialisms)"
        );
    }

    private static IEnumerable<Finding> CheckInternalLinksResolve(Artifact artifact)
    {
        var baseDir = Path.GetDirectoryName(artifact.Path);
        if (baseDir is null)
        {
            yield break;
        }

        foreach (Match m in s_markdownLinkRx.Matches(artifact.Body))
        {
            var target = m.Groups[1].Value.Trim();

            // Skip URLs, anchors, and email/other-scheme links.
            if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                || target.StartsWith('#'))
            {
                continue;
            }

            // Strip fragment (`#section`) from relative paths before existence check.
            var hash = target.IndexOf('#');
            var pathOnly = hash >= 0 ? target[..hash] : target;
            if (pathOnly.Length == 0)
            {
                continue;
            }

            var resolved = Path.Combine(baseDir, pathOnly);
            if (!File.Exists(resolved) && !Directory.Exists(resolved))
            {
                yield return new Finding(
                    Severity.Warn,
                    "InternalLinksResolve",
                    $"Broken relative link: {target}"
                );
            }
        }
    }

    private static IEnumerable<Finding> CheckScriptInventory(Artifact artifact)
    {
        var skillDir = Path.GetDirectoryName(artifact.Path);
        if (skillDir is null)
        {
            yield break;
        }

        var scriptsDir = Path.Combine(skillDir, "scripts");
        if (!Directory.Exists(scriptsDir))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(scriptsDir, "*", SearchOption.AllDirectories))
        {
            string? text = null;
            string? readError = null;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                readError = ex.Message;
            }

            if (text is null)
            {
                yield return new Finding(
                    Severity.Info,
                    "ScriptInventory",
                    $"Could not read {Path.GetRelativePath(skillDir, file)}: {readError}"
                );
                continue;
            }

            var loc = text.TrimEnd('\n').Split('\n').Length;
            var language = Path.GetExtension(file).ToLowerInvariant() switch
            {
                ".py"   => "python",
                ".sh"   => "shell",
                ".js"   => "javascript",
                ".ts"   => "typescript",
                ".rb"   => "ruby",
                ".go"   => "go",
                ".rs"   => "rust",
                ""      => "shell-or-unknown",
                var ext => ext.TrimStart('.'),
            };

            var flags = s_scriptFlags
                .Where(f => Regex.IsMatch(text, f.Pattern, RegexOptions.Multiline))
                .Select(f => f.Flag)
                .Distinct()
                .ToList();

            var flagText = flags.Count == 0 ? "no flags" : string.Join(", ", flags);
            yield return new Finding(
                Severity.Info,
                "ScriptInventory",
                $"{Path.GetRelativePath(skillDir, file)} ({language}, {loc} LOC; {flagText})"
            );
        }
    }

    private static int CountWords(string text) =>
        text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;
}
