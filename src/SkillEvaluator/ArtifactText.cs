using System.Net;
using System.Net.Http;
using System.Text.Json;
using YamlDotNet.Serialization;

namespace SkillEvaluator;

public static class ArtifactText
{
    private static readonly ISerializer s_yaml = new SerializerBuilder().Build();

    // YAML-serialize the frontmatter so list/map values round-trip cleanly —
    // `object.ToString()` would emit `System.Collections.Generic.List`1[...]`.
    public static string Reassemble(this Artifact artifact)
    {
        var yaml = s_yaml.Serialize(artifact.Frontmatter).TrimEnd();
        return $"---\n{yaml}\n---\n{artifact.Body}";
    }

    // Predicate for "LLM response was malformed in a way that warrants retry."
    // Used by all four LLM providers' retry filters.
    public static bool IsMalformedResponseError(Exception ex) =>
        ex is JsonException or InvalidOperationException or ArgumentOutOfRangeException;

    // Predicate for "HTTP call failed transiently; one retry is worth trying."
    // Covers 408 Request Timeout, 429 Too Many Requests, and any 5xx.
    public static bool IsTransientHttpError(Exception ex)
    {
        if (ex is not HttpRequestException httpEx || httpEx.StatusCode is not { } status)
        {
            return false;
        }
        return status == HttpStatusCode.RequestTimeout
            || status == HttpStatusCode.TooManyRequests
            || (int)status >= 500;
    }
}
