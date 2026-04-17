# Skill Evaluator — Design

- **Date**: 2026-04-16
- **Author**: Daryl Walleck
- **Status**: Approved for implementation

## Purpose

Build a CLI tool that evaluates Copilot-format AI agent artifacts (skills, instruction files, agents) dropped into the team by an architect, and produces metric-backed acceptance verdicts with rationale.

**Phase 1 (tonight)** — acceptance review: per-artifact verdict (accept / revise / reject) with evidence the reviewer can defend.

**Phase 2 (later)** — constructive feedback: the same data, reshaped as a change list the architect can act on.

Target artifact count: < 20. Execution target: Copilot at work; tool runs via kiro-cli (Claude Sonnet via Bedrock) for the qualitative rubric step. Home development/testing uses the Anthropic API.

## Scope

**In scope**: static analysis + one-shot LLM rubric grading per artifact, producing a markdown + JSON report.

**Out of scope (tonight)**:
- A/B effectiveness testing with confidence intervals (needs eval authoring and multi-run infrastructure).
- Description-optimization loops.
- Composition regression testing against existing team skills.

These are viable Phase 3+ extensions but not tonight.

## Architecture overview

Two-layer evaluation:

1. **Static layer** — deterministic C# checks. No LLM. Always runs.
2. **Rubric layer** — one LLM call per artifact via a pluggable provider. Returns structured JSON scoring 5 dimensions 1–5 plus a verdict hint.

The final verdict combines both: static blockers can auto-reject before the rubric even runs.

### Stack

| Decision | Pick | Rationale |
|---|---|---|
| Runtime | .NET 10 | Parity with `dotnet/skills` `skill-validator`; approved for the work stack |
| CLI framework | Spectre.Console.Cli | Mature; good terminal output |
| YAML | YamlDotNet | Standard for .NET YAML |
| Token counting | Microsoft.ML.Tokenizers | First-party; has `cl100k_base` for SkillsBench-tier classification |
| JSON | System.Text.Json | Built-in |
| HTTP | HttpClient | Built-in; Anthropic + GitHub Models are plain HTTP |
| Concurrency | `Parallel.ForEachAsync` with `MaxDegreeOfParallelism = 8` | Matches rubric-call throughput without overwhelming providers |
| Tests | TUnit | Parity with work repo; per CLAUDE.md `dotnet run` pattern |
| Error handling | Exceptions at CLI boundary | Tonight-scope skunkworks tool; OneOf discriminated unions would be overbuilt |

### File layout

```
skill-evaluator/
├── README.md
├── global.json                      # pin SDK to .NET 10
├── SkillEvaluator.sln
├── src/SkillEvaluator/
│   ├── SkillEvaluator.csproj
│   ├── Program.cs                   # Spectre.Console.Cli bootstrap
│   ├── Models.cs                    # Artifact, Finding, RubricScore, Verdict (records)
│   ├── Discovery.cs                 # walks dirs, parses frontmatter
│   ├── StaticAnalyzer.cs            # all deterministic checks as methods
│   ├── Rubric.cs                    # prompt assembly + JSON parsing
│   ├── VerdictDeriver.cs            # static + rubric -> Accept/Revise/Reject
│   ├── Reporter.cs                  # markdown + JSON output
│   └── Providers/
│       ├── IProvider.cs
│       ├── AnthropicProvider.cs
│       ├── KiroProvider.cs          # subprocess to kiro-cli
│       ├── GhModelsProvider.cs      # subprocess to gh models run
│       ├── GitHubModelsApiProvider.cs
│       └── StaticOnlyProvider.cs
└── tests/SkillEvaluator.Tests/
    ├── SkillEvaluator.Tests.csproj
    ├── Fixtures/
    └── StaticAnalyzerTests.cs
```

### Code style

Conformance to the work `CLAUDE.md` style guide:

- Allman braces everywhere, even single-statement bodies
- 4 spaces, no tabs
- `_camelCase` private fields, `s_camelCase` statics
- Explicit visibility on all members
- `is null` / `is not null`; `nameof(...)` over string literals
- Pattern matching / switch expressions preferred
- File-scoped namespaces
- Records for immutable data models

### Data flow

```
cli.main(path, --provider, --out)
         |
         v
discover_artifacts(path) -> list[Artifact]  (kind: skill | instruction | agent)
         |
         v
for artifact in artifacts (parallel, pool=8):
    static = run_static_analysis(artifact)
    if static.has_blocker():
        rubric = null                # short-circuit LLM cost
    else:
        rubric = provider.grade(artifact)    # one LLM call
    verdict = derive_verdict(static, rubric)
         |
         v
write_report(verdicts) -> out/report.md + out/report.json
```

## Artifact discovery

Each artifact kind has its own parser:

| Kind | Location pattern | Required frontmatter | Notes |
|---|---|---|---|
| skill | `<root>/skills/<name>/SKILL.md` (+ `scripts/`, `references/`, `assets/` siblings) | `name`, `description` | Walk sibling dirs for referenced files |
| instruction | `<root>/instructions/<name>.instructions.md` | `description`, `applyTo` | Flat file; no sibling dirs |
| agent | `<root>/agents/<name>.agent.md` | `description`, `name` | Flat file; no sibling dirs |

Shared shape:

```csharp
public sealed record Artifact(
    ArtifactKind Kind,
    string Name,
    string Path,
    IReadOnlyDictionary<string, object> Frontmatter,
    string Body,
    IReadOnlyList<string> ReferencedFiles
);
```

## Rubric

Five dimensions scored 1-5 by an LLM judge.

| # | Dimension | What it asks | Grounded in |
|---|---|---|---|
| 1 | Trigger Clarity | Can a model reliably decide *when* to use this? | skill-creator "primary triggering mechanism" |
| 2 | Scope Coherence | Does it do one well-defined thing? Internal consistency? | SkillsBench "2-3 focused > 4+ bundled" |
| 3 | Instructional Quality | Does it explain the *why*, or pile on MUSTs? | skill-creator "explain why... avoid oppressive structure" |
| 4 | Generality | Generalizes vs. overfit to specific examples | skill-validator overfitting classifier |
| 5 | Safety & Trust | Surprise content? Injection surface? Contradictions? | skill-creator "lack of surprise" |

### Rubric prompt (sent verbatim to the provider)

```
[SYSTEM]
You are evaluating a Copilot-format AI agent artifact for acceptance review.
You have no tools available. Do not attempt to invoke any tools.
Return a strict JSON object matching the schema. Do not include prose
outside the JSON, and do not wrap the JSON in markdown fences.

[USER]
## Artifact type
{kind}    # one of: skill | instruction | agent

## Artifact content
```
{full content, frontmatter + body}
```

## Rubric (score each dimension 1-5)

### 1. Trigger Clarity
Can a model reliably decide WHEN to use this artifact?
- skill:       Is the description specific about what it does AND when to invoke it?
               Is it "pushy" enough to avoid undertriggering, without overclaiming?
- instruction: Is `applyTo` appropriately scoped? Is the activation context clear?
- agent:       Is the persona's activation context and role clearly bounded?

5 = Precise, scoped, unambiguous triggering signal.
3 = Roughly clear but could be sharpened; might misfire on adjacent cases.
1 = Vague or contradictory; would undertrigger or overtrigger unpredictably.

### 2. Scope Coherence
Does it do one well-defined thing, or sprawl?
5 = Single clear purpose, no internal contradictions.
3 = Mostly coherent but touches 2+ concerns that belong in separate artifacts.
1 = Sprawling or self-contradictory; doesn't know what it is.

### 3. Instructional Quality
Does it explain WHY, or pile on MUSTs/NEVERs?
5 = Explains reasoning, treats the model as capable, restrained imperatives.
3 = Mix of explained and rote; some oppressive structure.
1 = Wall of MUST/NEVER/ALWAYS without reasoning.

### 4. Generality
Does it generalize, or is it overfit to specific examples?
5 = Patterns generalize to many variants of the use case.
3 = Works for the author's cases but would need work for adjacent ones.
1 = Overfit to specific examples; won't transfer beyond them.

### 5. Safety & Trust
Would a reasonable reader be surprised by what this does? Injection surface?
Contradictory personas?
5 = Transparent, no surprises, no injection surface.
3 = Minor surprises or unclear sections, nothing concerning.
1 = Surprising, contradictory, or has meaningful injection/trust concerns.

## Output schema

{
  "trigger_clarity":       { "score": <1-5>, "rationale": "<one sentence>" },
  "scope_coherence":       { "score": <1-5>, "rationale": "<one sentence>" },
  "instructional_quality": { "score": <1-5>, "rationale": "<one sentence>" },
  "generality":            { "score": <1-5>, "rationale": "<one sentence>" },
  "safety_trust":          { "score": <1-5>, "rationale": "<one sentence>" },
  "verdict_hint": "<accept|revise|reject>",
  "top_concerns": ["<specific concern>", ...],   // 0-3
  "strengths":    ["<specific strength>", ...]   // 0-3
}
```

### Rubric score composition

Weighted composite (1-5 scale):

- Trigger Clarity 0.25
- Scope Coherence 0.20
- Instructional Quality 0.20
- Generality 0.20
- Safety & Trust 0.15

## Static-check catalog

Each check produces a `Finding` with `Severity ∈ {Info, Warn, Blocker}`.

| Check | Applies to | Measurement | Thresholds | Severity |
|---|---|---|---|---|
| FrontmatterPresent | skill, agent | Has `name` + `description` | missing required field | Blocker |
| FrontmatterPresent | instruction | Has `description` + `applyTo` | missing `applyTo` | Blocker |
| FrontmatterYamlValid | all | YamlDotNet round-trip succeeds | parse error | Blocker |
| TokenTier | skill, agent | `cl100k_base` token count | < 400 compact; 400-2500 detailed; 2501-5000 standard (warn); > 5000 comprehensive (blocker) | per tier |
| BodyLength | instruction | Body line count | < 50 ok; 50-150 info; > 150 warn | per tier |
| ApplyToGlobValidity | instruction | Parse glob; detect overly broad | invalid → Blocker; `**/*` or `**` alone → Warn | per case |
| ReferencedFilesExist | skill | Regex-scan body for `scripts/`, `references/`, `assets/` files | missing file → Blocker per file | Blocker |
| InternalLinksResolve | all | Relative markdown links must resolve | broken → Warn per link | Warn |
| ImperativeSmellRatio | all | `MUST`/`NEVER`/`ALWAYS`/`REQUIRED` per 1000 words | > 20/kw → Warn; always report ratio | Info/Warn |
| AllCapsRatio | all | ALL-CAPS words ≥ 3 chars per 1000 words (excluding HTTP/JSON/API/CLI/URL/YAML/MCP) | > 15/kw → Warn | Info/Warn |
| DescriptionLength | skill, agent | Char count of `description` | < 40 or > 500 → Warn | Warn |
| ScriptInventory | skill | For each file in `scripts/`: language, LOC, grep for `eval`/`exec`/`subprocess`/`shell=True`, network imports, file writes | always Info, never Blocker | Info |

### Static score

- Each Warn: -5 points from 100
- Info findings don't affect score
- Floor at 0
- Any Blocker → auto-reject regardless of score

## Verdict derivation

```csharp
Verdict DeriveVerdict(StaticReport staticRpt, RubricResult? rubric)
{
    if (staticRpt.HasBlocker)
    {
        return Verdict.Reject(reasons: staticRpt.Blockers);
    }

    if (rubric is null)  // static-only mode
    {
        return staticRpt.WarningCount == 0
            ? Verdict.Accept(score: staticRpt.Score)
            : Verdict.Revise(score: staticRpt.Score, warnings: staticRpt.Warnings);
    }

    var minDim = rubric.Scores.Values.Min(s => s.Score);
    var composite = ComputeWeightedComposite(rubric.Scores);

    return (minDim, composite) switch
    {
        (<= 2, _) => Verdict.Reject(reason: "rubric dimension scored <= 2"),
        (_, >= 3.5) when minDim >= 4 => Verdict.Accept(score: composite),
        _ => Verdict.Revise(score: composite, concerns: rubric.TopConcerns),
    };
}
```

## Providers

```csharp
public interface IProvider
{
    string Name { get; }
    Task<RubricResult> GradeAsync(Artifact artifact, string rubricPrompt, CancellationToken ct);
}
```

| Provider | Backend | Auth | Usage |
|---|---|---|---|
| AnthropicProvider | `https://api.anthropic.com/v1/messages` via HttpClient | `ANTHROPIC_API_KEY` env | Home testing |
| KiroProvider | `kiro-cli chat --no-interactive "<prompt>"` subprocess | kiro-cli auth | Work primary |
| GhModelsProvider | `gh models run <model> "<prompt>"` subprocess | `gh auth` | Work fallback if `gh-models` extension installed |
| GitHubModelsApiProvider | `https://models.github.ai/inference/chat/completions` | PAT with `models:read` | Work fallback |
| StaticOnlyProvider | no-op | none | Always available; skips rubric |

Providers retry once on malformed JSON with a terse reminder ("Respond with valid JSON only"). On second failure, return empty `RubricResult` with `raw_response` populated for debugging.

## CLI shape

```
skill-evaluator evaluate <path> --provider <name> --out <dir>

--provider   anthropic | kiro | gh-models | github-api | none
--model      provider-specific model name (optional)
--out        output directory (default: ./report)
--parallel   max concurrent rubric calls (default: 8)
```

## Report format

Two files per run:

- `report.md` — human-readable; sections ordered Rejects → Revises → Accepts; at-a-glance table at top; rubric prompt in appendix for auditability.
- `report.json` — machine-readable; `schema_version: 1`; full static findings + rubric scores + verdicts per artifact.

Full shape in Section 4 of the brainstorming transcript; reproduced in code comments in `Reporter.cs`.

## Implementation plan (vertical slice)

| # | Milestone | Est | End state |
|---|---|---|---|
| 1 | Scaffold (sln, csproj, global.json, Spectre skeleton) | 30 min | `skill-evaluator --help` works |
| 2 | Models + Discovery | 45 min | Lists discovered artifacts |
| 3 | Static analyzer core checks (FrontmatterPresent, YamlValid, TokenTier, BodyLength, ApplyToGlobValidity) | 60 min | StaticReport produced |
| 4 | StaticOnlyProvider + VerdictDeriver + MarkdownReporter | 45 min | **End-to-end static-only** |
| 5 | AnthropicProvider + Rubric prompt/parser | 60 min | **Home milestone** — full rubric against Claude |
| 6 | Remaining static checks (ReferencedFilesExist, InternalLinksResolve, ImperativeSmellRatio, AllCapsRatio, DescriptionLength, ScriptInventory) | 45 min | Complete static catalog |
| 7 | JSON reporter + polish | 30 min | Matches spec exactly |
| 8 | KiroProvider + GhModelsProvider | 45 min | **Work milestone** |
| 9 | GitHubModelsApiProvider | 30 min | Second work fallback |
| 10 | TUnit smoke tests for static analyzer | 60 min | Confidence in core logic |

**Total**: ~7 hours.

### Cut-corner priority (drop from top if running late)

1. Drop Milestone 9 (GitHubModelsApiProvider).
2. Compress Milestone 10 to 3-4 boundary-case tests only.
3. Defer ratio checks in Milestone 6 (keep `ReferencedFilesExist`).
4. Drop JSON reporter, ship markdown only.
5. Drop GhModelsProvider half of Milestone 8 (KiroProvider alone covers work).

**Hard floor for usable Phase 1**: Milestones 1-5 + `ReferencedFilesExist` + `KiroProvider` = ~4.5 hours.

## Non-goals / explicit deferrals

- **A/B effectiveness testing** — requires eval authoring and multi-run infrastructure. Future extension.
- **Description optimization loops** — skill-creator's `run_loop.py` equivalent. Future extension.
- **Composition regression** ("does this new skill hurt existing skills?") — plugin-level A/B, needs existing eval suites.
- **Overfitting classifier** — skill-validator has this; Phase 1 doesn't need it because we're not measuring effectiveness, just construction.
- **Dashboard / web UI** — markdown + JSON is enough.

## Risks

| Risk | Mitigation |
|---|---|
| kiro-cli emits non-JSON or wraps in markdown fences | Prompt forbids fences; one retry with reminder; fall through to `raw_response` for debugging |
| LLM scores vary run-to-run (no A/B confidence here) | Acknowledged trade-off; Phase 2 can add multi-run averaging if needed |
| Rubric prompt overfits to skill-creator philosophy | Rubric is documented verbatim in report appendix; architect can contest specific dimensions with evidence |
| Static check false positives (e.g., imperative ratio on a deliberately strict compliance doc) | Info/Warn only for ratio checks; Blocker reserved for objective failures |
| Provider auth issues at work | Four provider options; StaticOnlyProvider is always available as a floor |

## Success criteria

Phase 1 is successful if:

1. The tool runs to completion against the architect's artifact set.
2. Every artifact gets a verdict (accept/revise/reject) with cited evidence.
3. The reviewer can defend each verdict using the static findings + rubric rationale in the report.
4. The reviewer feels confident sharing the Reject and Revise sections back with the architect (Phase 2 handoff).
