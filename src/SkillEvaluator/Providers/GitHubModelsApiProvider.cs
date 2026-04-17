using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SkillEvaluator.Providers;

public sealed class GitHubModelsApiProvider : IProvider, IDisposable
{
    private const string Endpoint = "https://models.github.ai/inference/chat/completions";

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string _model;

    public GitHubModelsApiProvider(string model)
        : this(new HttpClient { Timeout = TimeSpan.FromMinutes(2) }, model, ownsHttp: true)
    {
    }

    public GitHubModelsApiProvider(HttpClient http, string model)
        : this(http, model, ownsHttp: false)
    {
    }

    private GitHubModelsApiProvider(HttpClient http, string model, bool ownsHttp)
    {
        _http = http;
        _model = model;
        _ownsHttp = ownsHttp;
    }

    public string Name => "github-api";

    public async Task<RubricResult?> GradeAsync(Artifact artifact, string rubricPrompt, CancellationToken ct)
    {
        string raw;
        try
        {
            raw = await CallOnce(Rubric.SystemPrompt, rubricPrompt, artifact, ct);
        }
        catch (Exception ex) when (ArtifactText.IsTransientHttpError(ex))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
            raw = await CallOnce(Rubric.SystemPrompt, rubricPrompt, artifact, ct);
        }

        try
        {
            return Rubric.ParseResponse(raw);
        }
        catch (Exception ex) when (ArtifactText.IsMalformedResponseError(ex))
        {
            var retrySystem = Rubric.SystemPrompt +
                "\nYour previous response was malformed. Respond with valid JSON only.";
            var retry = await CallOnce(retrySystem, rubricPrompt, artifact, ct);
            return Rubric.ParseResponse(retry);
        }
    }

    private async Task<string> CallOnce(string systemPrompt, string userPrompt, Artifact artifact, CancellationToken ct)
    {
        // GITHUB_TOKEN is the canonical env var in Actions; GH_TOKEN is the
        // gh-CLI convention. Accept either.
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN")
            ?? Environment.GetEnvironmentVariable("GH_TOKEN")
            ?? throw new InvalidOperationException(
                "GITHUB_TOKEN (or GH_TOKEN) not set. Required scope: models:read."
            );

        var request = new
        {
            model = _model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userPrompt },
            },
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(request),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            // StatusCode is load-bearing: IsTransientHttpError reads it to retry 5xx/429/408.
            throw new HttpRequestException(
                $"GitHub Models API {(int)resp.StatusCode} for artifact '{artifact.Name}': {body}",
                inner: null,
                statusCode: resp.StatusCode
            );
        }

        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.GetProperty("choices")[0]
            .GetProperty("message").GetProperty("content").GetString();
        if (string.IsNullOrEmpty(text))
        {
            throw new InvalidOperationException(
                $"Empty response from GitHub Models for artifact '{artifact.Name}'."
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
