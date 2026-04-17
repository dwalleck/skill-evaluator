namespace SkillEvaluator.Providers;

public interface IProvider
{
    string Name { get; }
    Task<RubricResult?> GradeAsync(Artifact artifact, string rubricPrompt, CancellationToken ct);
}
