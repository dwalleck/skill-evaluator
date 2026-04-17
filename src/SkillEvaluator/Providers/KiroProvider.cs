using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace SkillEvaluator.Providers;

// Drives Kiro via the Agent Client Protocol (ACP) subcommand `kiro-cli acp`,
// a JSON-RPC 2.0 stream over stdio. This bypasses Kiro's TUI wrapper entirely:
// `kiro-cli chat --no-interactive` emits ANSI colors, cursor codes, a `> `
// prompt marker, and a "Credits/Time" footer — none of which are suppressible
// and all of which confuse the JSON parser. ACP returns the model's text
// directly via `session/update` → `agent_message_chunk` notifications, which
// concatenate into clean JSON.
public sealed class KiroProvider : IProvider
{
    public string Name => "kiro";

    public void Dispose()
    {
    }

    public async Task<RubricResult?> GradeAsync(Artifact artifact, string rubricPrompt, CancellationToken ct)
    {
        var fullPrompt = $"{Rubric.SystemPrompt}\n\n{rubricPrompt}";
        try
        {
            return Rubric.ParseResponse(await RunAcpAsync(fullPrompt, ct));
        }
        catch (Exception ex) when (ArtifactText.IsMalformedResponseError(ex))
        {
            var retry = fullPrompt + "\n\n(Your previous response was malformed. Respond with valid JSON only.)";
            return Rubric.ParseResponse(await RunAcpAsync(retry, ct));
        }
    }

    // Encoding.UTF8 writes a BOM preamble on the first WriteLine, which kiro-cli
    // rejects as a JSON-RPC parse error. Use the no-BOM variant for stdin and
    // for stdout reads so line boundaries aren't skewed by an accidental preamble.
    private static readonly UTF8Encoding s_utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static async Task<string> RunAcpAsync(string prompt, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "kiro-cli",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardInputEncoding = s_utf8NoBom,
            StandardOutputEncoding = s_utf8NoBom,
            StandardErrorEncoding = s_utf8NoBom,
        };
        psi.ArgumentList.Add("acp");
        psi.ArgumentList.Add("--trust-all-tools");

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
            var client = new AcpClient(proc, ct);
            try
            {
                await client.InitializeAsync();
                var sessionId = await client.NewSessionAsync();
                return await client.PromptAsync(sessionId, prompt);
            }
            finally
            {
                try
                {
                    proc.StandardInput.Close();
                }
                catch (IOException)
                {
                }
                if (!proc.HasExited)
                {
                    try
                    {
                        proc.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            }
        }
    }

    // Minimal JSON-RPC 2.0 client: id-correlates responses, accumulates
    // session/update agent_message_chunk text, ignores extension notifications.
    // One turn at a time — the rubric is one-shot, so we can clear chunks per
    // prompt and return the whole accumulated text on response.
    private sealed class AcpClient
    {
        private static readonly bool s_debug =
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KIRO_DEBUG"));

        private readonly Process _proc;
        private readonly CancellationToken _ct;
        private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pending = [];
        private readonly StringBuilder _chunks = new();
        private readonly Lock _gate = new();
        private readonly Task _reader;
        private int _nextId;

        public AcpClient(Process proc, CancellationToken ct)
        {
            _proc = proc;
            _ct = ct;
            _reader = Task.Run(ReadLoopAsync, ct);
            // Drain stderr so a full pipe buffer can't block the child. When
            // KIRO_DEBUG is set, echo it through so we can see what kiro-cli logs.
            _ = Task.Run(async () =>
            {
                try
                {
                    string? line;
                    while ((line = await _proc.StandardError.ReadLineAsync(ct)) is not null)
                    {
                        if (s_debug)
                        {
                            await Console.Error.WriteLineAsync($"[kiro.stderr] {line}");
                        }
                    }
                }
                catch (IOException)
                {
                }
                catch (OperationCanceledException)
                {
                }
            }, ct);
        }

        public async Task InitializeAsync()
        {
            await SendRequestAsync("initialize", new
            {
                protocolVersion = 1,
                clientCapabilities = new { },
                clientInfo = new { name = "skill-evaluator", version = "0.1", title = "skill-evaluator" },
            });
        }

        public async Task<string> NewSessionAsync()
        {
            var result = await SendRequestAsync("session/new", new
            {
                cwd = Environment.CurrentDirectory,
                mcpServers = Array.Empty<object>(),
            });
            return result.GetProperty("sessionId").GetString()
                ?? throw new InvalidOperationException("session/new response missing sessionId.");
        }

        public async Task<string> PromptAsync(string sessionId, string text)
        {
            lock (_gate)
            {
                _chunks.Clear();
            }
            await SendRequestAsync("session/prompt", new
            {
                sessionId,
                prompt = new[] { new { type = "text", text } },
            });
            lock (_gate)
            {
                return _chunks.ToString();
            }
        }

        private async Task<JsonElement> SendRequestAsync(string method, object @params)
        {
            int id;
            TaskCompletionSource<JsonElement> tcs;
            lock (_gate)
            {
                id = _nextId++;
                tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending[id] = tcs;
            }

            var json = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id,
                method,
                @params,
            });
            if (s_debug)
            {
                await Console.Error.WriteLineAsync($"[kiro.>>>] {json}");
            }
            await _proc.StandardInput.WriteLineAsync(json.AsMemory(), _ct);
            await _proc.StandardInput.FlushAsync(_ct);

            // Race the response against the reader task completing. If the
            // reader faulted or stdout closed before our response arrived,
            // `_reader` finishes first and surfaces the underlying problem
            // instead of leaving the caller hung on tcs.Task forever.
            var completed = await Task.WhenAny(tcs.Task, _reader).WaitAsync(_ct);
            if (completed == _reader)
            {
                await _reader;
                throw new InvalidOperationException(
                    $"kiro-cli ACP read loop exited before response to '{method}'.");
            }
            return await tcs.Task;
        }

        private async Task ReadLoopAsync()
        {
            Exception? fault = null;
            try
            {
                var reader = _proc.StandardOutput;
                while (!_ct.IsCancellationRequested)
                {
                    string? line;
                    try
                    {
                        line = await reader.ReadLineAsync(_ct);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    if (line is null)
                    {
                        break;
                    }
                    if (s_debug)
                    {
                        await Console.Error.WriteLineAsync($"[kiro.<<<] {line[..Math.Min(line.Length, 200)]}");
                    }

                    JsonDocument doc;
                    try
                    {
                        doc = JsonDocument.Parse(line);
                    }
                    catch (JsonException)
                    {
                        continue;
                    }

                    using (doc)
                    {
                        HandleMessage(doc.RootElement);
                    }
                }
            }
            catch (Exception ex)
            {
                fault = ex;
                if (s_debug)
                {
                    await Console.Error.WriteLineAsync($"[kiro.read-loop faulted] {ex}");
                }
            }
            finally
            {
                // Always unblock any waiting senders, whether we exited cleanly
                // (stdout EOF) or via an unexpected exception — otherwise they
                // hang on tcs.Task forever.
                lock (_gate)
                {
                    foreach (var (_, tcs) in _pending)
                    {
                        tcs.TrySetException(fault ?? new InvalidOperationException(
                            "kiro-cli acp closed stdout unexpectedly."));
                    }
                    _pending.Clear();
                }
            }
        }

        private void HandleMessage(JsonElement root)
        {
            // Response: has both `id` and (`result` or `error`).
            if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number)
            {
                if (root.TryGetProperty("result", out var resultEl))
                {
                    CompleteRequest(idEl.GetInt32(), tcs => tcs.TrySetResult(resultEl.Clone()));
                    return;
                }
                if (root.TryGetProperty("error", out var errorEl))
                {
                    var message = errorEl.TryGetProperty("message", out var m) ? m.GetString() : "unknown";
                    CompleteRequest(idEl.GetInt32(), tcs =>
                        tcs.TrySetException(new InvalidOperationException($"kiro-cli ACP error: {message}")));
                    return;
                }
            }

            // JSON-RPC error with null/missing id: the server couldn't parse
            // our request or hit a generic failure. We can't correlate it to a
            // specific pending entry, so surface it as a reader fault — the
            // `finally` in ReadLoopAsync will fail every outstanding request.
            if (root.TryGetProperty("error", out var topErrorEl))
            {
                var message = topErrorEl.TryGetProperty("message", out var m) ? m.GetString() : "unknown";
                throw new InvalidOperationException($"kiro-cli ACP error (no id): {message}");
            }

            // Notification: has `method` but no numeric `id` (or id is for a
            // server→client request, which with --trust-all-tools shouldn't occur).
            if (!root.TryGetProperty("method", out var methodEl))
            {
                return;
            }
            if (!string.Equals(methodEl.GetString(), "session/update", StringComparison.Ordinal))
            {
                return;
            }
            if (!root.TryGetProperty("params", out var paramsEl)
                || !paramsEl.TryGetProperty("update", out var update)
                || !update.TryGetProperty("sessionUpdate", out var kind)
                || !string.Equals(kind.GetString(), "agent_message_chunk", StringComparison.Ordinal)
                || !update.TryGetProperty("content", out var content)
                || !content.TryGetProperty("text", out var textEl))
            {
                return;
            }
            var chunk = textEl.GetString();
            if (chunk is null)
            {
                return;
            }
            lock (_gate)
            {
                _chunks.Append(chunk);
            }
        }

        private void CompleteRequest(int id, Action<TaskCompletionSource<JsonElement>> complete)
        {
            TaskCompletionSource<JsonElement>? tcs;
            lock (_gate)
            {
                _pending.Remove(id, out tcs);
            }
            if (tcs is not null)
            {
                complete(tcs);
            }
        }
    }
}
