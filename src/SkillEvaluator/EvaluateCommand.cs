using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SkillEvaluator;

public sealed class EvaluateCommand : AsyncCommand<EvaluateCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<path>")]
        [Description("Root directory containing artifacts (or a single artifact path).")]
        public required string Path { get; init; }

        [CommandOption("--provider <name>")]
        [Description("Provider: anthropic, kiro, gh-models, github-api, none.")]
        public string Provider { get; init; } = "none";

        [CommandOption("--out <dir>")]
        [Description("Output directory for the report.")]
        public string OutDir { get; init; } = "./report";

        [CommandOption("--parallel <n>")]
        [Description("Max concurrent rubric calls.")]
        public int Parallel { get; init; } = 8;

        [CommandOption("--model <name>")]
        [Description("Provider-specific model name.")]
        public string? Model { get; init; }
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        var artifacts = Discovery.DiscoverAll(settings.Path);
        if (artifacts.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No artifacts found under {0}[/]", settings.Path);
            return 1;
        }

        Providers.IProvider provider = settings.Provider switch
        {
            "none"      => new Providers.StaticOnlyProvider(),
            "anthropic" => new Providers.AnthropicProvider(new HttpClient(), settings.Model ?? "claude-sonnet-4-6"),
            "kiro"      => new Providers.KiroProvider(),
            _           => throw new NotSupportedException($"Provider '{settings.Provider}' not yet wired up."),
        };

        var sw = Stopwatch.StartNew();
        var results = new ConcurrentBag<ArtifactResult>();

        await Parallel.ForEachAsync(
            artifacts,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = settings.Parallel,
                CancellationToken = cancellationToken,
            },
            async (artifact, ct) =>
            {
                var staticReport = StaticAnalyzer.Analyze(artifact);
                RubricResult? rubric = null;
                if (!staticReport.HasBlocker)
                {
                    rubric = await provider.GradeAsync(artifact, Rubric.BuildUserPrompt(artifact), ct);
                }
                var verdict = VerdictDeriver.Derive(staticReport, rubric);
                results.Add(new ArtifactResult(artifact, staticReport, rubric, verdict));
            }
        );

        Directory.CreateDirectory(settings.OutDir);
        var md = Reporter.BuildMarkdown(
            results.OrderBy(r => r.Artifact.Name).ToList(),
            provider: settings.Provider,
            model: settings.Model,
            duration: sw.Elapsed
        );
        await File.WriteAllTextAsync(Path.Combine(settings.OutDir, "report.md"), md, cancellationToken);

        Console.WriteLine($"Wrote {results.Count} verdicts to {settings.OutDir}/report.md in {sw.Elapsed.TotalSeconds:F1}s");
        return 0;
    }
}
