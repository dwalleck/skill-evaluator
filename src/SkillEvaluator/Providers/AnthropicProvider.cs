using System.Net.Http.Json;
using System.Text.Json;

namespace SkillEvaluator.Providers;

public sealed class AnthropicProvider : IProvider
{
    private readonly HttpClient _http;
    private readonly string _model;

    public AnthropicProvider(HttpClient http, string model)
    {
        _http = http;
        _model = model;
    }

    public string Name => "anthropic";

    public async Task<RubricResult?> GradeAsync(Artifact artifact, string rubricPrompt, CancellationToken ct)
    {
        var request = new
        {
            model = _model,
            max_tokens = 1024,
            system = Rubric.SystemPrompt,
            messages = new[]
            {
                new { role = "user", content = rubricPrompt },
            },
        };

        var raw = await CallOnce(request, ct);
        try
        {
            return Rubric.ParseResponse(raw);
        }
        catch (JsonException)
        {
            var retry = await CallOnce(new
            {
                model = _model,
                max_tokens = 1024,
                system = Rubric.SystemPrompt + "\nYour previous response was malformed. Respond with valid JSON only.",
                messages = new[] { new { role = "user", content = rubricPrompt } },
            }, ct);
            return Rubric.ParseResponse(retry);
        }
    }

    private async Task<string> CallOnce(object request, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = JsonContent.Create(request),
        };
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new InvalidOperationException("ANTHROPIC_API_KEY not set");
        req.Headers.Add("x-api-key", apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");

        var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString()
            ?? throw new InvalidOperationException("Empty response");
        return text;
    }
}
