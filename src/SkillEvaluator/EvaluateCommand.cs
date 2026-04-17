using System.ComponentModel;
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
    }

    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        await Task.CompletedTask;
        Console.WriteLine($"path={settings.Path} provider={settings.Provider} out={settings.OutDir}");
        return 0;
    }
}
