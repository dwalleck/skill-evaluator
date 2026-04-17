using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace SkillEvaluator.Providers;

public sealed class KiroProvider : IProvider
{
    public string Name => "kiro";

    public void Dispose()
    {
    }

    public async Task<RubricResult?> GradeAsync(Artifact artifact, string rubricPrompt, CancellationToken ct)
    {
        var fullPrompt = $"{Rubric.SystemPrompt}\n\n{rubricPrompt}";
        var output = await RunKiroAsync(fullPrompt, ct);
        try
        {
            return Rubric.ParseResponse(output);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentOutOfRangeException)
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
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("chat");
        psi.ArgumentList.Add("--no-interactive");

        Process? proc;
        try
        {
            proc = Process.Start(psi);
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(
                "kiro-cli not found on PATH. Install it or run with --provider none.",
                ex
            );
        }

        if (proc is null)
        {
            throw new InvalidOperationException("Failed to start kiro-cli.");
        }

        using (proc)
        {
            // Write prompt via stdin so we don't blow argv limits (ARG_MAX is
            // 128 KiB on Linux, 32 KiB per arg on Windows) and so the prompt
            // doesn't show up in `ps`.
            await proc.StandardInput.WriteAsync(prompt.AsMemory(), ct);
            await proc.StandardInput.FlushAsync(ct);
            proc.StandardInput.Close();

            // Read stdout and stderr *before* awaiting exit. If we waited
            // first, the child could block writing to a full pipe buffer
            // (4-64 KiB on Linux) and WaitForExitAsync would never return.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);

            await proc.WaitForExitAsync(ct);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (proc.ExitCode != 0)
            {
                throw new InvalidOperationException($"kiro-cli exited {proc.ExitCode}: {stderr}");
            }

            return stdout;
        }
    }
}
