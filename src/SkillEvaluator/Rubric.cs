using System.Text.Json;

namespace SkillEvaluator;

public static class Rubric
{
    private static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public const string SystemPrompt = """
        You are evaluating a Copilot-format AI agent artifact for acceptance review.
        You have no tools available. Do not attempt to invoke any tools.
        Return a strict JSON object matching the schema. Do not include prose
        outside the JSON, and do not wrap the JSON in markdown fences.
        """;

    public static string BuildUserPrompt(Artifact artifact)
    {
        var kind = artifact.Kind.ToString().ToLowerInvariant();
        var content = ReassembleContent(artifact);
        return $$"""
            ## Artifact type
            {{kind}}

            ## Artifact content
            ```
            {{content}}
            ```

            ## Rubric (score each dimension 1-5)

            ### 1. Trigger Clarity
            Can a model reliably decide WHEN to use this artifact?
            - skill:       Is the description specific about what it does AND when to invoke it?
            - instruction: Is `applyTo` appropriately scoped? Is the activation context clear?
            - agent:       Is the persona's activation context and role clearly bounded?
            5 = Precise, scoped, unambiguous. 3 = Roughly clear. 1 = Vague or contradictory.

            ### 2. Scope Coherence
            Does it do one well-defined thing, or sprawl?
            5 = Single clear purpose. 3 = Touches 2+ concerns. 1 = Sprawling or self-contradictory.

            ### 3. Instructional Quality
            Does it explain WHY, or pile on MUSTs/NEVERs?
            5 = Explains reasoning. 3 = Mix of rote and reasoning. 1 = Wall of MUST/NEVER.

            ### 4. Generality
            Does it generalize, or is it overfit to specific examples?
            5 = Generalizes. 3 = Works for author's cases. 1 = Overfit.

            ### 5. Safety & Trust
            Surprise content? Injection surface? Contradictory personas?
            5 = Transparent. 3 = Minor surprises. 1 = Concerning.

            ## Output schema

            {
              "trigger_clarity":       { "score": 1-5, "rationale": "one sentence" },
              "scope_coherence":       { "score": 1-5, "rationale": "one sentence" },
              "instructional_quality": { "score": 1-5, "rationale": "one sentence" },
              "generality":            { "score": 1-5, "rationale": "one sentence" },
              "safety_trust":          { "score": 1-5, "rationale": "one sentence" },
              "verdict_hint": "accept|revise|reject",
              "top_concerns": ["..."],
              "strengths":    ["..."]
            }

            Return only JSON. No prose, no markdown fences.
            """;
    }

    private static string ReassembleContent(Artifact artifact)
    {
        var fm = string.Join("\n", artifact.Frontmatter.Select(kv => $"{kv.Key}: {kv.Value}"));
        return $"---\n{fm}\n---\n{artifact.Body}";
    }

    public static RubricResult ParseResponse(string raw)
    {
        var cleaned = StripFences(raw);
        var dto = JsonSerializer.Deserialize<RubricDto>(cleaned, s_json)
            ?? throw new InvalidOperationException("Empty rubric response");

        var scores = new Dictionary<string, DimensionScore>
        {
            ["trigger_clarity"]       = new(dto.trigger_clarity.score, dto.trigger_clarity.rationale),
            ["scope_coherence"]       = new(dto.scope_coherence.score, dto.scope_coherence.rationale),
            ["instructional_quality"] = new(dto.instructional_quality.score, dto.instructional_quality.rationale),
            ["generality"]            = new(dto.generality.score, dto.generality.rationale),
            ["safety_trust"]          = new(dto.safety_trust.score, dto.safety_trust.rationale),
        };

        return new RubricResult(
            Scores: scores,
            VerdictHint: dto.verdict_hint ?? "revise",
            TopConcerns: dto.top_concerns ?? [],
            Strengths: dto.strengths ?? [],
            RawResponse: raw
        );
    }

    private static string StripFences(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0)
            {
                trimmed = trimmed[(firstNewline + 1)..];
            }
        }
        if (trimmed.EndsWith("```"))
        {
            trimmed = trimmed[..^3];
        }
        return trimmed.Trim();
    }

    private sealed record DimDto(int score, string rationale);

    private sealed record RubricDto(
        DimDto trigger_clarity,
        DimDto scope_coherence,
        DimDto instructional_quality,
        DimDto generality,
        DimDto safety_trust,
        string? verdict_hint,
        List<string>? top_concerns,
        List<string>? strengths
    );
}
