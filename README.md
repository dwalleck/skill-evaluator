# skill-evaluator

A CLI that evaluates Copilot-format AI agent artifacts — **skills**, **instruction files**, and **agents** — and produces metric-backed **accept / revise / reject** verdicts with cited rationale.

## What it does

Two-layer evaluation:

1. **Static layer** — deterministic C# checks on frontmatter, token tier, referenced files, glob validity, imperative density, all-caps ratio, description length, script inventory, and internal link integrity. Always runs. Static blockers short-circuit the LLM call.
2. **Rubric layer** — one LLM call per artifact scoring five dimensions (Trigger Clarity, Scope Coherence, Instructional Quality, Generality, Safety & Trust) 1–5. Results are combined with the static layer into a final verdict.

Outputs a markdown report (`report.md`) plus a machine-readable JSON report (`report.json`, `schema_version: 1`) for downstream tooling.

## Quick start

```bash
# Static-only, no LLM — always works, fastest
dotnet run --project src/SkillEvaluator -- evaluate ./path/to/artifacts --provider none --out ./report

# Anthropic Claude (home / general use)
export ANTHROPIC_API_KEY=sk-ant-...
dotnet run --project src/SkillEvaluator -- evaluate ./path/to/artifacts --provider anthropic --out ./report

# kiro-cli (work primary)
dotnet run --project src/SkillEvaluator -- evaluate ./path/to/artifacts --provider kiro --out ./report
```

### CLI options

| Flag | Values | Default |
|---|---|---|
| `<path>` | Root directory containing `skills/`, `instructions/`, or `agents/` subdirectories — or the `skills/` directory itself | required |
| `--provider` | `none` \| `anthropic` \| `kiro` \| `gh-models` \| `github-api` | `none` |
| `--model` | Provider-specific model name | provider-specific |
| `--out` | Output directory for `report.md` + `report.json` | `./report` |
| `--parallel` | Max concurrent rubric calls | `8` |

## Providers

| Provider | Backend | Auth | Notes |
|---|---|---|---|
| `none` | No-op | — | Static-only mode; always available as a floor |
| `anthropic` | Anthropic Messages API | `ANTHROPIC_API_KEY` | Primary for home dev/testing |
| `kiro` | `kiro-cli chat --no-interactive` subprocess | kiro-cli login | Primary for work |
| `gh-models` | `gh models run <model>` subprocess | `gh auth` + gh-models extension | Work fallback |
| `github-api` | `models.github.ai/inference/chat/completions` | `GITHUB_TOKEN` or `GH_TOKEN` (scope: `models:read`) | Work fallback |

All LLM providers retry once on malformed JSON with a terse reminder, then surface the failure as a per-artifact `provider_error` in the report (other artifacts still complete).

## How verdicts are derived

- **Any static blocker → Reject** regardless of rubric.
- **Static-only mode**: zero warnings → Accept; one or more warnings → Revise.
- **With rubric**: any dimension scoring below 3 → Reject. Otherwise, if every dimension is ≥ 4 *and* the weighted composite (Trigger Clarity 0.25, Scope Coherence 0.20, Instructional Quality 0.20, Generality 0.20, Safety & Trust 0.15) is ≥ 3.5 → Accept. Else → Revise.

Full rubric text, thresholds, and check catalog are documented in [`docs/plans/2026-04-16-skill-evaluator-design.md`](docs/plans/2026-04-16-skill-evaluator-design.md).

## Running the tests

```bash
dotnet run --project tests/SkillEvaluator.Tests
```

TUnit runs as a standalone test executable under `Microsoft.Testing.Platform`; `dotnet test` isn't supported without extra SDK opt-in.

## Report shape

- `report.md` — human-readable: summary counts, at-a-glance table, per-verdict sections (Rejects first), rubric prompt in an appendix for auditability.
- `report.json` — machine-readable: `schema_version: 1`, full static findings, rubric scores, verdicts, and per-artifact provider errors.
