namespace SkillEvaluator.Providers;

public interface IProvider : IDisposable
{
    string Name { get; }
    Task<RubricResult?> GradeAsync(Artifact artifact, string rubricPrompt, CancellationToken ct);
}
