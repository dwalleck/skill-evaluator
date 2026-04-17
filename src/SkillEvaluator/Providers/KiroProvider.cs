using System.Diagnostics;

namespace SkillEvaluator.Providers;

public sealed class KiroProvider : IProvider
{
    public string Name => "kiro";

    public async Task<RubricResult?> GradeAsync(Artifact artifact, string rubricPrompt, CancellationToken ct)
    {
        var fullPrompt = $"{Rubric.SystemPrompt}\n\n{rubricPrompt}";
        var output = await RunKiroAsync(fullPrompt, ct);
        try
        {
            return Rubric.ParseResponse(output);
        }
        catch
        {
            var retryPrompt = fullPrompt + "\n\n(Your previous response was malformed. Respond with valid JSON only.)";
            var retryOutput = await RunKiroAsync(retryPrompt, ct);
            return Rubric.ParseResponse(retryOutput);
        }
    }

    private static async Task<string> RunKiroAsync(string prompt, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "kiro-cli",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("chat");
        psi.ArgumentList.Add("--no-interactive");
        psi.ArgumentList.Add(prompt);

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start kiro-cli");
        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0)
        {
            var err = await proc.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException($"kiro-cli exited {proc.ExitCode}: {err}");
        }
        return await proc.StandardOutput.ReadToEndAsync(ct);
    }
}
