# TrailGuard — Codex Instructions

This file applies to the entire repository. It is the operating guide for Codex when implementing an already-approved TrailGuard task.

## Required Context

Before changing any file, read these repository documents:

1. `CLAUDE.md` — authoritative project context, architecture, domain rules, ML contracts, workflows, and known issues.
2. `DESIGN.md` — authoritative UI system, component patterns, accessibility rules, and current design progress.
3. `MODEL.md` — required when work touches the suitability model, dataset, evaluation, safety claims, difficulty calibration, or manuscript-facing metrics.
4. `MODEL_EXPLAINED_EN.md` — supporting explanation only. Where figures differ, use the current figures identified by `CLAUDE.md` and `MODEL.md`.

Inspect the relevant implementation after reading the documents. Documentation is context, but the current code and migrations determine what is actually implemented.

If instructions conflict, follow this order:

1. The user's current implementation prompt and its approved acceptance criteria.
2. This `AGENTS.md`.
3. `CLAUDE.md` for project and domain behavior.
4. `DESIGN.md` for interface behavior and visual decisions.
5. Other repository documentation.

Do not silently resolve a conflict that could change safety behavior, stored data, the ML contract, or the agreed task scope. Stop and report it.

## Collaboration Workflow

TrailGuard work follows this sequence:

1. Planning is completed with the user before implementation begins.
2. Codex receives an implementation prompt containing the approved scope and acceptance criteria.
3. Codex implements and performs all safe automated verification available in its environment.
4. Codex returns a structured handoff for independent checking.
5. The user decides whether the work is ready to commit and push.

The implementation prompt authorizes only the scope it describes.

- Do not redesign the plan, add speculative features, or perform unrelated cleanup.
- A small adjacent fix is allowed only when it is necessary to make the requested change correct and verifiable. Explain it in the handoff.
- If a new decision would materially affect behavior or scope, stop and ask instead of guessing.
- Never commit, amend, merge, rebase, tag, push, open a pull request, or alter remote repository state. The user owns all Git history and remote operations.
- Do not prepare or modify commit messages unless the user explicitly asks.

## Before Editing

Run read-only checks first:

```bash
git status --short
git branch --show-current
git log -1 --oneline
```

Then inspect all files directly involved in the task and their callers or consumers.

- Preserve pre-existing user changes and unrelated untracked files.
- Never discard or overwrite work using destructive Git commands.
- Do not edit `CLAUDE.md`, `DESIGN.md`, or model documentation unless the approved task explicitly includes documentation updates.
- Do not change generated migrations, compiled CSS, model artifacts, datasets, or dependency lockfiles unless the task requires it.
- Never place credentials or real secrets in tracked configuration. Database credentials belong in User Secrets or an explicitly approved deployment secret store.

## Project Non-Negotiables

These are high-risk rules summarized here for visibility. `CLAUDE.md` contains their full rationale and current details.

### Suitability and safety

- The FastAPI/XGBoost service is the only suitability mechanism. Never add or restore a rule-based fallback.
- If the ML service is unavailable or times out, produce no result, save nothing, and return the participant to a retryable form state.
- The ACSM post-prediction gate can only lower a model label; it must never raise one.
- The organizer makes the final registration decision. ML output is decision support, not automatic approval or rejection.
- Never reintroduce legacy category-score bars, threshold comparisons, risk-flag displays, or other rule-based results beside an ML prediction.
- Display confidence as the real predicted-class probability, to one decimal place, without an artificial cap.

### ML contract and explainability

- `AssessmentController.BuildMlRequest` and Python `FEATURE_COLUMNS` form a cross-language contract. Check both sides before changing any input, mapping, type, or feature order.
- Unknown categorical answers must fail explicitly; never silently map an unknown value to zero.
- Preserve the API/application label normalization between `Good Match` and `Good-Match` unless a separately approved migration changes every consumer safely.
- SHAP explanations remain anchored to the documented `Good Match` class behavior.
- A displayed SHAP percentage is a share of displayed impact, not outcome probability.
- Recommendations come from negative participant-actionable factors. Do not recommend changing trail-side properties such as distance, elevation, or terrain.
- Do not present synthetic-data performance as measured accuracy on real hikers.

### Trail difficulty and weather

- Weather remains a separate event advisory and is not an ML feature.
- Difficulty ordering and display use the terrain-adjusted rating, not the plain NPS value.
- Matching difficulty logic exists in Python and C#. Any approved change to formulas, multipliers, thresholds, or bands must update and verify both implementations together.

### UI and accessibility

- Reuse an established pattern from `DESIGN.md` before introducing a new one.
- Preserve the dark glassmorphism system, documented palette, status semantics, two-radius system, focus treatments, reduced-motion support, and non-color status cues.
- Do not add card hover scaling, button glow, undocumented radii, or a second modal visibility mechanism.
- The assessment report's hierarchy is: result, confidence, explanation, SHAP factors, recommendations.
- Form and wizard changes must preserve dependency rules and JavaScript validation described in `CLAUDE.md`.

## Implementation Discipline

- Prefer the smallest cohesive change that fully satisfies the approved acceptance criteria.
- Keep business rules in the existing shared service/helper when one already owns the rule. Do not create a second inline implementation.
- Follow current controller, service, model, Razor, and JavaScript conventions in nearby code.
- Validate at server boundaries even when client-side validation exists, especially for safety-relevant or persisted values.
- Do not catch an error only to hide it or substitute a plausible-looking result.
- Preserve antiforgery protection, authorization checks, role boundaries, and ownership checks on every changed endpoint.
- Avoid broad formatting or generated-file churn that obscures the functional diff.
- Add or update comments only when they explain a non-obvious constraint or safety decision; do not narrate obvious code.

## Verification Requirements

Perform every check that safely applies to the change. At minimum, consider:

```bash
git diff --check
dotnet build
```

Additional checks by change type:

- Razor, Tailwind, or frontend class changes: run `npm run build` and verify the needed classes exist in `wwwroot/css/output.css`.
- JavaScript changes: exercise the affected state transitions, validation boundaries, keyboard behavior, and failure states.
- Python changes: run syntax/import checks and the most relevant evaluation or safety scripts documented in `CLAUDE.md`/`MODEL.md`.
- ML contract changes: compare the C# request model and mapping against Python request fields and `FEATURE_COLUMNS`, then test a representative request end to end.
- Difficulty changes: compare representative boundary cases in both the C# and Python implementations.
- Database model changes: create and inspect the required EF Core migration. Do not apply it to an unknown or shared database without explicit authorization.
- Authorization or workflow changes: test allowed and forbidden roles, direct URL or request access, duplicate submissions, stale states, and retry behavior.
- UI changes: check desktop and mobile layout, empty/error/loading states, keyboard focus, reduced motion, and content overflow.

There may be no automated test project for the affected behavior. Never describe `dotnet build` or manual browser checks as unit tests. If a check cannot run because of missing dependencies, credentials, services, or environment access, report the exact blocker and provide the user with precise manual steps and expected results.

Do not install or upgrade dependencies, start destructive seed/reset operations, or modify a real database merely to make verification pass unless the approved prompt explicitly authorizes it.

## Required Handoff

Finish with a concise implementation report using this structure:

### Summary

- What behavior changed and why.

### Files changed

- Each changed file and its purpose.

### Verification performed

- Exact commands or checks, each marked passed, failed, or not run.
- Include relevant build/test output summaries without claiming more coverage than was performed.

### Review evidence

- Include the output of `git status --short` and `git diff --stat`.
- Identify every untracked file created for the task, since ordinary `git diff` does not include its contents.
- Make the complete diff and all new-file contents available to the user for independent review. Do not rely on a prose summary as a substitute for the code changes.
- For visual work, list the routes, viewport sizes, and UI states checked, and provide screenshots when the environment supports them.

### Manual verification for the user

- Only checks that require the user's credentials, browser, local services, database, or judgment.
- Give numbered steps, required setup/data, and the expected result for each step.
- Write `None` when no user-only verification remains.

### Risks and unresolved items

- Known limitations, assumptions, scope deviations, or follow-up work.
- Write `None` when there are none.

### Repository state

- Confirm that no commit or push was performed.
- Report any pre-existing changes that were left untouched.

Do not state that the task is ready to commit merely because implementation finished. The user will submit this handoff for independent verification before deciding whether to commit and push.
