namespace SkillEvaluator.Providers;

public sealed class StaticOnlyProvider : IProvider
{
    public string Name => "none";

    public Task<RubricResult?> GradeAsync(Artifact artifact, string rubricPrompt, CancellationToken ct)
    {
        return Task.FromResult<RubricResult?>(null);
    }
}
