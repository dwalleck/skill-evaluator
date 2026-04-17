using System.ComponentModel;
using System.Diagnostics;

namespace SkillEvaluator.Providers;

public sealed class GhModelsProvider : IProvider
{
    private readonly string _model;

    public GhModelsProvider(string model)
    {
        _model = model;
    }

    public string Name => "gh-models";

    public async Task<RubricResult?> GradeAsync(Artifact artifact, string rubricPrompt, CancellationToken ct)
    {
        var output = await RunAsync(Rubric.SystemPrompt, rubricPrompt, ct);
        try
        {
            return Rubric.ParseResponse(output);
        }
        catch (Exception ex) when (ArtifactText.IsMalformedResponseError(ex))
        {
            var retrySystem = Rubric.SystemPrompt +
                "\nYour previous response was malformed. Respond with valid JSON only.";
            var retry = await RunAsync(retrySystem, rubricPrompt, ct);
            return Rubric.ParseResponse(retry);
        }
    }

    private async Task<string> RunAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        // `gh models run` reads the user prompt from stdin when given '-'.
        // System prompt goes in via --system-prompt.
        var psi = new ProcessStartInfo
        {
            FileName = "gh",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("models");
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--system-prompt");
        psi.ArgumentList.Add(systemPrompt);
        psi.ArgumentList.Add(_model);

        Process? proc;
        try
        {
            proc = Process.Start(psi);
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(
                "gh not found on PATH. Install the gh CLI and the `gh-models` extension.",
                ex
            );
        }

        if (proc is null)
        {
            throw new InvalidOperationException("Failed to start gh.");
        }

        using (proc)
        {
            await proc.StandardInput.WriteAsync(userPrompt.AsMemory(), ct);
            await proc.StandardInput.FlushAsync(ct);
            proc.StandardInput.Close();

            // Read before waiting to avoid pipe-buffer deadlock.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);

            await proc.WaitForExitAsync(ct);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (proc.ExitCode != 0)
            {
                throw new InvalidOperationException($"gh models run exited {proc.ExitCode}: {stderr}");
            }

            return stdout;
        }
    }

    public void Dispose()
    {
    }
}
