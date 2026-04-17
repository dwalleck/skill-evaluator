using System.Text.Json;
using YamlDotNet.Serialization;

namespace SkillEvaluator;

public static class ArtifactText
{
    private static readonly ISerializer s_yaml = new SerializerBuilder().Build();

    /// YAML-serializes the frontmatter so list/map values round-trip cleanly
    /// (vs `object.ToString()` emitting `System.Collections.Generic.List`1[...]`).
    public static string Reassemble(this Artifact artifact)
    {
        var yaml = s_yaml.Serialize(artifact.Frontmatter).TrimEnd();
        return $"---\n{yaml}\n---\n{artifact.Body}";
    }

    /// Predicate for "LLM response was malformed in a way that warrants retry."
    /// Shared by AnthropicProvider and KiroProvider retry filters.
    public static bool IsMalformedResponseError(Exception ex) =>
        ex is JsonException or InvalidOperationException or ArgumentOutOfRangeException;
}
