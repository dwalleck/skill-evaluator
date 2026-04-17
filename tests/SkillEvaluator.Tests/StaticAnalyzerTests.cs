using SkillEvaluator;

namespace SkillEvaluator.Tests;

public sealed class StaticAnalyzerTests
{
    private static Artifact SkillWithFrontmatter(Dictionary<string, object> fm)
    {
        return new Artifact(
            Kind: ArtifactKind.Skill,
            Name: "x",
            Path: "/tmp/x/SKILL.md",
            Frontmatter: fm,
            Body: "body",
            ReferencedFiles: []
        );
    }

    [Test]
    public async Task Skill_without_name_is_blocker()
    {
        var artifact = SkillWithFrontmatter(new() { ["description"] = "d" });

        var report = StaticAnalyzer.Analyze(artifact);

        await Assert.That(report.HasBlocker).IsTrue();
        await Assert.That(report.Findings).Contains(f => f.Check == CheckKind.FrontmatterPresent);
    }

    [Test]
    public async Task Instruction_without_applyTo_is_blocker()
    {
        var artifact = new Artifact(
            Kind: ArtifactKind.Instruction,
            Name: "x",
            Path: "/tmp/x.instructions.md",
            Frontmatter: new Dictionary<string, object> { ["description"] = "d" },
            Body: "body",
            ReferencedFiles: []
        );

        var report = StaticAnalyzer.Analyze(artifact);

        await Assert.That(report.HasBlocker).IsTrue();
    }

    [Test]
    public async Task Complete_frontmatter_no_blocker()
    {
        var artifact = SkillWithFrontmatter(new() { ["name"] = "x", ["description"] = "d" });

        var report = StaticAnalyzer.Analyze(artifact);

        await Assert.That(report.HasBlocker).IsFalse();
    }

    [Test]
    [Arguments(200, "compact", Severity.Info)]
    [Arguments(1500, "detailed", Severity.Info)]
    [Arguments(3000, "standard", Severity.Warn)]
    [Arguments(6000, "comprehensive", Severity.Blocker)]
    public async Task TokenTier_classifies_by_count(int targetTokens, string expectedTier, Severity expectedSeverity)
    {
        var body = string.Join(" ", Enumerable.Repeat("word", targetTokens));
        var artifact = new Artifact(
            Kind: ArtifactKind.Skill,
            Name: "x",
            Path: "/tmp/x/SKILL.md",
            Frontmatter: new Dictionary<string, object> { ["name"] = "x", ["description"] = "d" },
            Body: body,
            ReferencedFiles: []
        );

        var report = StaticAnalyzer.Analyze(artifact);

        var finding = report.Findings.Single(f => f.Check == CheckKind.TokenTier);
        await Assert.That(finding.Message).Contains(expectedTier);
        await Assert.That(finding.Severity).IsEqualTo(expectedSeverity);
    }

    [Test]
    public async Task BodyLength_warns_over_150_lines()
    {
        var body = string.Join("\n", Enumerable.Repeat("line", 160));
        var artifact = new Artifact(
            Kind: ArtifactKind.Instruction,
            Name: "x",
            Path: "/tmp/x.instructions.md",
            Frontmatter: new Dictionary<string, object> { ["description"] = "d", ["applyTo"] = "**/*.cs" },
            Body: body,
            ReferencedFiles: []
        );

        var report = StaticAnalyzer.Analyze(artifact);

        await Assert.That(report.Findings).Contains(f => f.Check == CheckKind.BodyLength && f.Severity == Severity.Warn);
    }

    [Test]
    public async Task ApplyToGlob_warns_on_overly_broad_pattern()
    {
        var artifact = new Artifact(
            Kind: ArtifactKind.Instruction,
            Name: "x",
            Path: "/tmp/x.instructions.md",
            Frontmatter: new Dictionary<string, object> { ["description"] = "d", ["applyTo"] = "**/*" },
            Body: "body",
            ReferencedFiles: []
        );

        var report = StaticAnalyzer.Analyze(artifact);

        await Assert.That(report.Findings).Contains(f => f.Check == CheckKind.ApplyToGlobValidity && f.Severity == Severity.Warn);
    }

    [Test]
    public async Task BodyLength_ignores_trailing_newline()
    {
        // 150 real lines + trailing '\n' would previously report 151 and warn.
        var body = string.Join("\n", Enumerable.Repeat("line", 150)) + "\n";
        var artifact = new Artifact(
            Kind: ArtifactKind.Instruction,
            Name: "x",
            Path: "/tmp/x.instructions.md",
            Frontmatter: new Dictionary<string, object> { ["description"] = "d", ["applyTo"] = "**/*.cs" },
            Body: body,
            ReferencedFiles: []
        );

        var report = StaticAnalyzer.Analyze(artifact);

        await Assert.That(report.Findings).DoesNotContain(f => f.Check == CheckKind.BodyLength && f.Severity == Severity.Warn);
    }

    [Test]
    public async Task ReferencedFilesExist_blocks_on_missing_file()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var skillMd = Path.Combine(tmp, "SKILL.md");
            File.WriteAllText(skillMd, "---\nname: x\ndescription: d\n---\nUses `scripts/missing.py`");
            var artifact = new Artifact(
                Kind: ArtifactKind.Skill,
                Name: "x",
                Path: skillMd,
                Frontmatter: new Dictionary<string, object> { ["name"] = "x", ["description"] = "d" },
                Body: "Uses `scripts/missing.py`",
                ReferencedFiles: [Path.Combine(tmp, "scripts/missing.py")]
            );

            var report = StaticAnalyzer.Analyze(artifact);

            await Assert.That(report.HasBlocker).IsTrue();
            await Assert.That(report.Findings).Contains(f => f.Check == CheckKind.ReferencedFilesExist);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Test]
    public async Task ImperativeSmellRatio_warns_when_dense()
    {
        // 4 imperatives in ~10 words = 400/1000 = well above the 20/1000 threshold.
        var body = "You MUST do this. You NEVER do that. You ALWAYS do this. It is REQUIRED now.";
        var artifact = SkillWithBody(body);

        var report = StaticAnalyzer.Analyze(artifact);

        await Assert.That(report.Findings).Contains(f => f.Check == CheckKind.ImperativeSmellRatio && f.Severity == Severity.Warn);
    }

    [Test]
    public async Task ImperativeSmellRatio_info_when_sparse()
    {
        var body = string.Join(" ", Enumerable.Repeat("word", 500)) + " MUST";
        var artifact = SkillWithBody(body);

        var report = StaticAnalyzer.Analyze(artifact);

        var finding = report.Findings.Single(f => f.Check == CheckKind.ImperativeSmellRatio);
        await Assert.That(finding.Severity).IsEqualTo(Severity.Info);
    }

    [Test]
    public async Task AllCapsRatio_excludes_common_initialisms()
    {
        var body = "Use the HTTP API to fetch JSON from the URL via the CLI.";
        var artifact = SkillWithBody(body);

        var report = StaticAnalyzer.Analyze(artifact);

        var finding = report.Findings.Single(f => f.Check == CheckKind.AllCapsRatio);
        await Assert.That(finding.Severity).IsEqualTo(Severity.Info);
        await Assert.That(finding.Message).Contains("0.0 all-caps");
    }

    [Test]
    public async Task AllCapsRatio_warns_when_shouting()
    {
        var body = "STOP DOING THAT NOW OR ELSE THE WORLD WILL END BADLY OK";
        var artifact = SkillWithBody(body);

        var report = StaticAnalyzer.Analyze(artifact);

        await Assert.That(report.Findings).Contains(f => f.Check == CheckKind.AllCapsRatio && f.Severity == Severity.Warn);
    }

    [Test]
    [Arguments(20)]   // too short
    [Arguments(600)]  // too long
    public async Task DescriptionLength_warns_outside_bounds(int length)
    {
        var desc = new string('x', length);
        var artifact = new Artifact(
            ArtifactKind.Skill, "x", "/tmp/x/SKILL.md",
            new Dictionary<string, object> { ["name"] = "x", ["description"] = desc },
            "body", []);

        var report = StaticAnalyzer.Analyze(artifact);

        await Assert.That(report.Findings).Contains(f => f.Check == CheckKind.DescriptionLength && f.Severity == Severity.Warn);
    }

    [Test]
    public async Task DescriptionLength_silent_when_in_range()
    {
        var desc = new string('x', 120);
        var artifact = new Artifact(
            ArtifactKind.Skill, "x", "/tmp/x/SKILL.md",
            new Dictionary<string, object> { ["name"] = "x", ["description"] = desc },
            "body", []);

        var report = StaticAnalyzer.Analyze(artifact);

        await Assert.That(report.Findings).DoesNotContain(f => f.Check == CheckKind.DescriptionLength);
    }

    [Test]
    public async Task InternalLinksResolve_warns_on_broken_link()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var skillMd = Path.Combine(tmp, "SKILL.md");
            File.WriteAllText(skillMd, "body");
            var artifact = new Artifact(
                ArtifactKind.Skill, "x", skillMd,
                new Dictionary<string, object> { ["name"] = "x", ["description"] = "d" },
                "See [docs](references/missing.md) and [also this](https://ok.example.com).",
                []);

            var report = StaticAnalyzer.Analyze(artifact);

            var findings = report.Findings.Where(f => f.Check == CheckKind.InternalLinksResolve).ToList();
            await Assert.That(findings.Count).IsEqualTo(1);
            await Assert.That(findings[0].Message).Contains("references/missing.md");
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Test]
    public async Task InternalLinksResolve_silent_for_existing_files()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(tmp, "references"));
            File.WriteAllText(Path.Combine(tmp, "references", "existing.md"), "content");
            var skillMd = Path.Combine(tmp, "SKILL.md");
            File.WriteAllText(skillMd, "body");
            var artifact = new Artifact(
                ArtifactKind.Skill, "x", skillMd,
                new Dictionary<string, object> { ["name"] = "x", ["description"] = "d" },
                "See [docs](references/existing.md).",
                []);

            var report = StaticAnalyzer.Analyze(artifact);

            await Assert.That(report.Findings).DoesNotContain(f => f.Check == CheckKind.InternalLinksResolve);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Test]
    public async Task ScriptInventory_reports_flags()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var scriptsDir = Path.Combine(tmp, "scripts");
            Directory.CreateDirectory(scriptsDir);
            File.WriteAllText(Path.Combine(scriptsDir, "run.py"), "import subprocess\nsubprocess.run(['ls'], shell=True)\n");
            var skillMd = Path.Combine(tmp, "SKILL.md");
            File.WriteAllText(skillMd, "body");
            var artifact = new Artifact(
                ArtifactKind.Skill, "x", skillMd,
                new Dictionary<string, object> { ["name"] = "x", ["description"] = "d" },
                "body",
                [Path.Combine(scriptsDir, "run.py")]);

            var report = StaticAnalyzer.Analyze(artifact);

            var finding = report.Findings.Single(f => f.Check == CheckKind.ScriptInventory);
            await Assert.That(finding.Severity).IsEqualTo(Severity.Info);
            await Assert.That(finding.Message).Contains("python");
            await Assert.That(finding.Message).Contains("subprocess");
            await Assert.That(finding.Message).Contains("shell=True");
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    private static Artifact SkillWithBody(string body) => new(
        ArtifactKind.Skill, "x", "/tmp/x/SKILL.md",
        new Dictionary<string, object> { ["name"] = "x", ["description"] = "demo" + new string('x', 50) },
        body, []);

    [Test]
    [Arguments("AOT")]
    [Arguments("NET")]
    [Arguments("CRLF")]
    [Arguments("SDK")]
    [Arguments("JIT")]
    [Arguments("RFC")]
    [Arguments("MCP")]
    public async Task AllCapsRatio_allowlist_protects_technical_initialism(string token)
    {
        // Regression guard on the smoke-test fix: if anyone prunes the
        // allowlist, a 20-word body with just this token and "normal" prose
        // should still be Info, not Warn.
        var body = $"Some prose about {token} and how it works in our system here. " +
                   string.Join(" ", Enumerable.Repeat("word", 15));
        var artifact = SkillWithBody(body);

        var report = StaticAnalyzer.Analyze(artifact);

        var finding = report.Findings.Single(f => f.Check == CheckKind.AllCapsRatio);
        await Assert.That(finding.Severity).IsEqualTo(Severity.Info);
    }

    [Test]
    public async Task ImperativeSmellRatio_ignores_fenced_code_blocks()
    {
        // 20 imperatives inside a fenced code block plus clean prose must
        // not trigger the Warn. If we counted them, ratio would be ~167/1000.
        var codeBlock = "```csharp\n" +
            string.Join("\n", Enumerable.Repeat("MUST do work here", 20)) +
            "\n```";
        var prose = string.Join(" ", Enumerable.Repeat("word", 100));
        var body = $"{codeBlock}\n\n{prose}";
        var artifact = SkillWithBody(body);

        var report = StaticAnalyzer.Analyze(artifact);

        var finding = report.Findings.Single(f => f.Check == CheckKind.ImperativeSmellRatio);
        await Assert.That(finding.Severity).IsEqualTo(Severity.Info);
    }

    [Test]
    public async Task AllCapsRatio_ignores_fenced_code_blocks()
    {
        var codeBlock = "```shell\nFOO=BAR BAZ=QUX DOIT HARDER NOW\n```";
        var prose = string.Join(" ", Enumerable.Repeat("word", 100));
        var body = $"{codeBlock}\n\n{prose}";
        var artifact = SkillWithBody(body);

        var report = StaticAnalyzer.Analyze(artifact);

        var finding = report.Findings.Single(f => f.Check == CheckKind.AllCapsRatio);
        await Assert.That(finding.Severity).IsEqualTo(Severity.Info);
    }

    [Test]
    public async Task InternalLinksResolve_skips_anchor_and_mailto()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var skillMd = Path.Combine(tmp, "SKILL.md");
            File.WriteAllText(skillMd, "body");
            var artifact = new Artifact(
                ArtifactKind.Skill, "x", skillMd,
                new Dictionary<string, object> { ["name"] = "x", ["description"] = "d" },
                "See [here](#section) or [email](mailto:test@example.com) or [web](https://example.com).",
                []);

            var report = StaticAnalyzer.Analyze(artifact);

            await Assert.That(report.Findings).DoesNotContain(f => f.Check == CheckKind.InternalLinksResolve);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Test]
    public async Task InternalLinksResolve_handles_title_syntax()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(tmp, "references"));
            File.WriteAllText(Path.Combine(tmp, "references", "existing.md"), "content");
            var skillMd = Path.Combine(tmp, "SKILL.md");
            File.WriteAllText(skillMd, "body");
            var artifact = new Artifact(
                ArtifactKind.Skill, "x", skillMd,
                new Dictionary<string, object> { ["name"] = "x", ["description"] = "d" },
                "See [docs](references/existing.md \"The Reference\").",
                []);

            var report = StaticAnalyzer.Analyze(artifact);

            await Assert.That(report.Findings).DoesNotContain(f => f.Check == CheckKind.InternalLinksResolve);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Test]
    public async Task InternalLinksResolve_url_decodes_percent_encoding()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(tmp, "references"));
            File.WriteAllText(Path.Combine(tmp, "references", "my file.md"), "content");
            var skillMd = Path.Combine(tmp, "SKILL.md");
            File.WriteAllText(skillMd, "body");
            var artifact = new Artifact(
                ArtifactKind.Skill, "x", skillMd,
                new Dictionary<string, object> { ["name"] = "x", ["description"] = "d" },
                "See [docs](references/my%20file.md).",
                []);

            var report = StaticAnalyzer.Analyze(artifact);

            await Assert.That(report.Findings).DoesNotContain(f => f.Check == CheckKind.InternalLinksResolve);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Test]
    [Arguments("run.sh", "shell")]
    [Arguments("build.ts", "typescript")]
    [Arguments("util.rb", "ruby")]
    [Arguments("Dockerfile", "shell-or-unknown")]
    public async Task ScriptInventory_detects_language_by_extension(string filename, string expectedLang)
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var scriptsDir = Path.Combine(tmp, "scripts");
            Directory.CreateDirectory(scriptsDir);
            File.WriteAllText(Path.Combine(scriptsDir, filename), "content\n");
            var skillMd = Path.Combine(tmp, "SKILL.md");
            File.WriteAllText(skillMd, "body");
            var artifact = new Artifact(
                ArtifactKind.Skill, "x", skillMd,
                new Dictionary<string, object> { ["name"] = "x", ["description"] = "d" },
                "body",
                []);

            var report = StaticAnalyzer.Analyze(artifact);

            var finding = report.Findings.Single(f => f.Check == CheckKind.ScriptInventory);
            await Assert.That(finding.Message).Contains(expectedLang);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Test]
    public async Task ScriptInventory_skips_binary_files()
    {
        var tmp = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var scriptsDir = Path.Combine(tmp, "scripts");
            Directory.CreateDirectory(scriptsDir);
            File.WriteAllBytes(Path.Combine(scriptsDir, "icon.png"), [0x89, 0x50, 0x4E, 0x47]);
            var skillMd = Path.Combine(tmp, "SKILL.md");
            File.WriteAllText(skillMd, "body");
            var artifact = new Artifact(
                ArtifactKind.Skill, "x", skillMd,
                new Dictionary<string, object> { ["name"] = "x", ["description"] = "d" },
                "body",
                []);

            var report = StaticAnalyzer.Analyze(artifact);

            var finding = report.Findings.Single(f => f.Check == CheckKind.ScriptInventory);
            await Assert.That(finding.Message).Contains("binary, skipped");
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }
}
