using Spectre.Console.Cli;

namespace SkillEvaluator;

public sealed class Program
{
    public static int Main(string[] args)
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationName("skill-evaluator");
            config.AddCommand<EvaluateCommand>("evaluate")
                .WithDescription("Evaluate Copilot-format artifacts (skills, instructions, agents).");
        });
        return app.Run(args);
    }
}
