using System.Net.Http.Json;
using System.Text.Json;

namespace SkillEvaluator.Providers;

public sealed class AnthropicProvider : IProvider, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string _model;

    public AnthropicProvider(string model)
        : this(new HttpClient { Timeout = TimeSpan.FromMinutes(2) }, model, ownsHttp: true)
    {
    }

    public AnthropicProvider(HttpClient http, string model)
        : this(http, model, ownsHttp: false)
    {
    }

    private AnthropicProvider(HttpClient http, string model, bool ownsHttp)
    {
        _http = http;
        _model = model;
        _ownsHttp = ownsHttp;
    }

    public string Name => "anthropic";

    public async Task<RubricResult?> GradeAsync(Artifact artifact, string rubricPrompt, CancellationToken ct)
    {
        var raw = await CallOnce(Rubric.SystemPrompt, rubricPrompt, artifact, ct);
        try
        {
            return Rubric.ParseResponse(raw);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            var retrySystem = Rubric.SystemPrompt +
                "\nYour previous response was malformed. Respond with valid JSON only.";
            var retry = await CallOnce(retrySystem, rubricPrompt, artifact, ct);
            return Rubric.ParseResponse(retry);
        }
    }

    private async Task<string> CallOnce(string systemPrompt, string userPrompt, Artifact artifact, CancellationToken ct)
    {
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new InvalidOperationException("ANTHROPIC_API_KEY not set");

        var request = new
        {
            model = _model,
            max_tokens = 1024,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = userPrompt } },
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = JsonContent.Create(request),
        };
        req.Headers.Add("x-api-key", apiKey);
        // Required Anthropic Messages API version header; pinned for stability.
        req.Headers.Add("anthropic-version", "2023-06-01");

        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            // Anthropic returns structured error JSON; include the body so the
            // user sees *why* (bad key, rate limit, overload) instead of just
            // "Response status code does not indicate success".
            throw new HttpRequestException(
                $"Anthropic API {(int)resp.StatusCode} for artifact '{artifact.Name}': {body}"
            );
        }

        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString();
        if (string.IsNullOrEmpty(text))
        {
            throw new InvalidOperationException(
                $"Empty response from Anthropic for artifact '{artifact.Name}'."
            );
        }
        return text;
    }

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }
}
