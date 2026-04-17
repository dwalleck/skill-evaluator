using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
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
            AnsiConsole.MarkupLine(CultureInfo.InvariantCulture, "[red]No artifacts found under {0}[/]", settings.Path);
            return 1;
        }

        using var provider = BuildProvider(settings);

        var sw = Stopwatch.StartNew();
        var results = new ConcurrentBag<ArtifactResult>();
        var wasCancelled = false;

        try
        {
            await Parallel.ForEachAsync(
                artifacts,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = settings.Parallel,
                    CancellationToken = cancellationToken,
                },
                async (artifact, ct) =>
                {
                    results.Add(await GradeArtifact(provider, artifact, ct));
                }
            );
        }
        catch (OperationCanceledException)
        {
            wasCancelled = true;
        }

        try
        {
            Directory.CreateDirectory(settings.OutDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AnsiConsole.MarkupLine(CultureInfo.InvariantCulture, "[red]Failed to create output directory {0}: {1}[/]", settings.OutDir, ex.Message);
            return 2;
        }

        var ordered = results.OrderBy(r => r.Artifact.Name, StringComparer.Ordinal).ToList();
        // Each report is written independently so a failure on one doesn't
        // destroy the other — a costly LLM run should never be discarded
        // because disk ran out partway through a second write.
        await TryWriteReport(
            Path.Combine(settings.OutDir, "report.md"),
            () => Reporter.BuildMarkdown(ordered, settings.Provider, settings.Model, sw.Elapsed)
        );
        await TryWriteReport(
            Path.Combine(settings.OutDir, "report.json"),
            () => Reporter.BuildJson(ordered, settings.Provider, settings.Model, sw.Elapsed)
        );

        var completed = results.Count;
        var total = artifacts.Count;
        var prefix = wasCancelled ? "Cancelled after " : "Wrote ";
        Console.WriteLine($"{prefix}{completed}/{total} verdicts to {settings.OutDir}/ in {sw.Elapsed.TotalSeconds:F1}s");
        // 130 = 128 + SIGINT, conventional shell exit code for Ctrl-C.
        return wasCancelled ? 130 : 0;
    }

    private static async Task TryWriteReport(string path, Func<string> build)
    {
        string content;
        try
        {
            content = build();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine(CultureInfo.InvariantCulture, "[red]Failed to build {0}: {1}[/]", path, ex.Message);
            return;
        }

        try
        {
            // CancellationToken.None so Ctrl-C doesn't discard the partial
            // report we just built from results already in hand.
            await File.WriteAllTextAsync(path, content, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AnsiConsole.MarkupLine(CultureInfo.InvariantCulture, "[red]Failed to write {0}: {1}[/]", path, ex.Message);
        }
    }

    private static async Task<ArtifactResult> GradeArtifact(
        Providers.IProvider provider,
        Artifact artifact,
        CancellationToken ct
    )
    {
        var staticReport = StaticAnalyzer.Analyze(artifact);
        RubricResult? rubric = null;
        string? providerError = null;

        if (!staticReport.HasBlocker)
        {
            try
            {
                rubric = await provider.GradeAsync(artifact, Rubric.BuildUserPrompt(artifact), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Per-artifact failure: capture so the report still contains
                // the static findings and other artifacts aren't lost.
                // OperationCanceledException propagates so Parallel.ForEachAsync tears down.
                providerError = ex.Message;
            }
        }

        var verdict = VerdictDeriver.Derive(staticReport, rubric);
        return new ArtifactResult(artifact, staticReport, rubric, verdict, providerError);
    }

    private static Providers.IProvider BuildProvider(Settings settings) => settings.Provider switch
    {
        "none"       => new Providers.StaticOnlyProvider(),
        "anthropic"  => new Providers.AnthropicProvider(settings.Model ?? "claude-sonnet-4-6"),
        "kiro"       => new Providers.KiroProvider(),
        "gh-models"  => new Providers.GhModelsProvider(settings.Model ?? "gpt-4o"),
        "github-api" => new Providers.GitHubModelsApiProvider(settings.Model ?? "gpt-4o"),
        _            => throw new NotSupportedException($"Unknown provider: {settings.Provider}"),
    };
}
