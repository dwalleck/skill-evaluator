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
        var content = artifact.Reassemble();
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

    public static RubricResult ParseResponse(string raw)
    {
        var cleaned = StripFences(raw);
        var dto = JsonSerializer.Deserialize<RubricDto>(cleaned, s_json)
            ?? throw new JsonException("Rubric response deserialized to null.");

        var trigger       = RequireDim(dto.trigger_clarity, nameof(dto.trigger_clarity));
        var scope         = RequireDim(dto.scope_coherence, nameof(dto.scope_coherence));
        var instructional = RequireDim(dto.instructional_quality, nameof(dto.instructional_quality));
        var generality    = RequireDim(dto.generality, nameof(dto.generality));
        var safety        = RequireDim(dto.safety_trust, nameof(dto.safety_trust));

        if (dto.verdict_hint is null)
        {
            throw new JsonException("Rubric response missing required field: verdict_hint.");
        }

        return new RubricResult(
            TriggerClarity: trigger,
            ScopeCoherence: scope,
            InstructionalQuality: instructional,
            Generality: generality,
            SafetyTrust: safety,
            VerdictHint: dto.verdict_hint,
            TopConcerns: dto.top_concerns ?? [],
            Strengths: dto.strengths ?? [],
            RawResponse: raw
        );
    }

    private static DimensionScore RequireDim(DimDto? dim, string fieldName)
    {
        if (dim is null)
        {
            throw new JsonException($"Rubric response missing required dimension: {fieldName}.");
        }
        return new DimensionScore(dim.score, dim.rationale ?? "");
    }

    private static string StripFences(string raw)
    {
        var trimmed = raw.Trim();

        // Only strip fences when both appear — avoids mangling responses that
        // happen to start or end with ``` but aren't actually fenced.
        if (!trimmed.StartsWith("```", StringComparison.Ordinal) || !trimmed.EndsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewline = trimmed.IndexOf('\n');
        if (firstNewline == -1)
        {
            return trimmed;
        }

        var inner = trimmed[(firstNewline + 1)..^3];
        return inner.Trim();
    }

    private sealed record DimDto(int score, string? rationale);

    private sealed record RubricDto(
        DimDto? trigger_clarity,
        DimDto? scope_coherence,
        DimDto? instructional_quality,
        DimDto? generality,
        DimDto? safety_trust,
        string? verdict_hint,
        List<string>? top_concerns,
        List<string>? strengths
    );
}
