# TrailGuard — Project Context and Operating Guide

TrailGuard is a PUP College of Computer and Information Sciences capstone: a web-based hiking-event management system with ML-supported participant-to-trail suitability assessment. This is the current implementation guide. CLAUDE.md is an aligned concise entry point.

## Current baseline

- Last verified deployed baseline: commit 8fee29b.
- Active model: trailguard-v2.0.0.
- Active inference path: ml-services/trailguard-v2, a FastAPI adapter on loopback port 8011 locally.
- trailguard-ml-v2 is a historical baseline artifact, not the active assessment path.
- The active model is frozen: 11 inputs and 985 selected XGBoost trees. Do not alter its artifacts, checksums, mappings, or tree count without an approved model release.

## Stack and deployment

| Layer | Current implementation |
|---|---|
| Web app | ASP.NET Core MVC, .NET 10, C# |
| Persistence | EF Core 10 / PostgreSQL via Npgsql |
| Identity | ASP.NET Core Identity; Admin, Organizer, Participant |
| UI | Razor and Tailwind CSS v4; dark glassmorphism TrailGuard design |
| ML | Python 3.14, FastAPI, XGBoost, native TreeSHAP |
| Weather | Open-Meteo forecast/geocoding; separate advisory |
| Deployment | Azure App Service containers with Supabase PostgreSQL |

The MVC main container listens on 8080; the Python sidecar/adapter listens on 8011. In deployment, configure TrailGuardV2Api:BaseUrl to the sidecar address. Local development uses PostgreSQL directly and the loopback adapter. Supabase and Local are independent databases; switching targets never copies or synchronizes records.

## Running locally

Start the active adapter first:

~~~powershell
cd ml-services\trailguard-v2
.venv\Scripts\python -m uvicorn app:app --host 127.0.0.1 --port 8011
~~~

Set TrailGuardV2Api:BaseUrl to http://127.0.0.1:8011, then run:

~~~powershell
dotnet run
~~~

Use npm run dev while changing Tailwind sources, or npm run build before verifying a frontend change. Tailwind generates only classes it finds in source; check wwwroot/css/output.css before diagnosing missing styling.

Credentials belong in User Secrets or an approved deployment secret store, never tracked configuration. Database:Target resolves Local (default) or Supabase through DatabaseTargetResolver; blank, unsupported, malformed, or missing selected connection strings fail startup. See README.md for exact configuration.

DbSeeder creates only operational roles and, if absent, admin@trailguard.com; it never changes existing records. SeedAdmin:Password applies to Local and SeedAdmin:SupabasePassword to Supabase. A missing seed password skips first-time Admin creation and safely retries next startup.

---

## Working in this repository — agent operating rules

### Required context before editing

Read relevant parts of this file before changing a file. Also read:

- DESIGN.md for Razor, Tailwind, JavaScript, or interface work.
- MODEL.md for suitability, dataset, evaluation, safety claims, difficulty calibration, or manuscript-facing model statements.
- MODEL_EXPLAINED_EN.md for the active-model narrative.

Then inspect the implementation and migrations actually affected. Documentation is context, not a substitute for code.

### Instruction precedence and scope

1. The user's current approved implementation prompt and acceptance criteria.
2. This document, including its operating and domain safeguards.
3. DESIGN.md for interaction and visual decisions.
4. Other repository documentation.

Do not silently resolve a conflict that changes safety behavior, stored data, the ML contract, or agreed scope. Planning happens with the user before implementation. Make the smallest cohesive authorized change; do not add speculative features or unrelated cleanup. Never commit, amend, merge, rebase, tag, push, open a pull request, or alter remote state unless explicitly asked.

### Before editing

Run:

~~~powershell
git status --short
git branch --show-current
git log -1 --oneline
~~~

Preserve existing user changes and unrelated untracked files. Do not change generated migrations, compiled CSS, model artifacts, datasets, or lockfiles unless the task requires it. Keep credentials out of tracked files.

### Non-negotiables

- The active ML service is the only suitability mechanism. Never restore a rule-based fallback or legacy category-score bars.
- Failed inference produces no substitute result and no partial assessment/screening record; the participant may retry after the adapter recovers.
- Organizer review is the final registration decision. A model label is decision support, never automatic approval or rejection.
- The active contract is TrailGuardV2AssessmentRequestMapper / TrailGuardV2PredictionRequest, TrailGuardV2ApiClient, and ml-services/trailguard-v2/frozen-bundle/feature_schema.json—not AssessmentController.BuildMlRequest, historical encoding.py, or trailguard-ml-v2.
- The exact 11 names/order, categories, numeric constraints, model SHA-256, and 985 trees are pinned. Reject unknown or unsupported inputs; never silently coerce them.
- BMI, medical conditions, and demographics are not active-model features. Medical screening remains independent server-side application logic.
- Weather is a separate event advisory, not an ML feature.
- Difficulty is a provisional display-only route-effort policy using an immutable Event snapshot.
- Preserve antiforgery, authorization, role boundaries, ownership checks, locks, and persisted-state checks.
- Reuse DESIGN.md patterns; do not add competing modal visibility, hover scaling, or undocumented radii.

### Verification

At minimum:

~~~powershell
git diff --check
dotnet build
~~~

Run every safe relevant check as well:

- Frontend: npm run build, confirm output classes, and check desktop/mobile, error/loading/empty states, keyboard focus, reduced motion, and overflow.
- JavaScript: exercise state transitions, duplicate-submit protection, validation boundaries, keyboard behavior, and failure recovery.
- Python/ML: syntax/import checks; compare mapper/request with the active bundle schema; test /predict if the adapter is available. Do not train or tune to verify.
- Difficulty: test representative boundaries against DifficultyCalculator.
- Database model: create/inspect a needed migration, but do not apply it to an unknown/shared database without authorization.
- Workflow: test permitted and denied roles, direct access, duplicates, stale records, cancellation, and retries.

Do not call a build or manual browser check a unit test. If a check cannot run, report its precise blocker and expected manual result. Do not install dependencies, reset/seed a database, or alter real data merely to make a check pass.

### Required handoff

Report summary; changed files and purposes; exact verification with honest pass/fail/not-run status; git status --short and git diff --stat; created untracked files; manual checks requiring credentials/browser/services; risks/unresolved items; and confirmation no commit or push occurred.

---

## Active assessment architecture

~~~text
ASP.NET Core MVC
  TrailGuardV2AssessmentRequestMapper
  TrailGuardV2ApiClient (TrailGuardV2Api:BaseUrl)
                    │ HTTP/JSON
                    ▼
ml-services/trailguard-v2 FastAPI adapter (:8011)
  frozen bundle / XGBoost / native TreeSHAP
~~~

TrailGuardV2ApiClient checks adapter identity, model version, pinned SHA-256, and selected tree count. It rejects unreachable, timed-out, malformed, or contract-inconsistent responses. TrailGuardV2AssessmentRequestMapper validates participant answers and Event snapshots before requests. The result factory persists only a validated graph.

### ML failure — no fallback

The historical GetResult heuristic is deleted. If /predict cannot return a fully valid active-model response, the form shows a safe error. No assessment, suitability result, SHAP record, or partial screening record is persisted. This is deliberate: a substitute rule would be an unvalidated, potentially disagreeing answer and could bypass separate safety workflow.

### Frozen 11-feature contract

The authoritative input order is:

1. exercise_frequency
2. cardio_duration
3. exercise_consistency
4. hiking_experience
5. hiking_recency
6. hardest_trail_completed
7. gear_score
8. distance_km
9. elevation_gain_m
10. trail_class
11. typical_duration_hours

The first six are exact raw categorical strings from the frozen schema. The mapper derives gear_score from recognized gear. Trail inputs always come from immutable Event snapshots, never a live Trail, browser-posted value, or editable EstimatedDuration.

Profile/demographic and health information has separate application uses. Medical answers support clearance and registration rules; profile data supports eligibility/contact workflows. Historical age/BMI/height/weight values are not sent to the active model. Do not infer model use from old forms or retired models.

First-time hiking consistency is server- and client-enforced: hiking_experience 0 requires hiking_recency Never and hardest_trail_completed None; experienced hikers cannot select either value. Gear's None of the above is mutually exclusive with recognized gear.

### Scores and labels

SuitabilityResult.ModelScore stores the unchanged probability_yes output. It displays to two decimal percentage places; exact 0/1 become 0.00%/100.00%, nonzero values rounding to zero become <0.01%, and non-one values rounding to 100 become >99.99%. Formatting never changes storage.

The model's binary Yes/No threshold is 0.50. Participant-facing labels are a separate inherited interpretation policy:

| Score | Label |
|---|---|
| Below 0.30 | Not Recommended |
| 0.30 through below 0.80 | Borderline |
| 0.80 or higher | Good Match |

These are interpretation bands over one binary-model score, not three independently predicted class probabilities. Do not call ModelScore a calibrated real-world completion probability; no active-model evaluation evidence supports that claim.

### Native TreeSHAP explanation

The adapter returns native TreeSHAP contributions for all 11 inputs, plus base value and raw margin. The raw_margin_log_odds scale means positive values push toward Yes and negative values toward No; values are not percentage-point probability changes, causal effects, fitness diagnoses, or medical conclusions.

The adapter/MVC boundary fails closed unless schema order, finite values, 985-tree identity, unchanged prediction, TreeSHAP additivity, and sigmoid reconstruction are within 1e-4. These are explanation-consistency and integrity checks, not accuracy, calibration, fairness, or causality evidence.

TrailGuardV2Presentation.BuildV2Factors displays all factors in descending absolute contribution, with feature-order tie breaks. BuildV2Suggestions returns at most three negative actionable contributors only: exercise frequency, cardio duration, exercise consistency, and gear score. Trail factors never become participant suggestions.

### Screening and organizer decision

AcsmClearanceService.RequiresMedicalClearance(hasSignsSymptoms, hasCvd) is independent C# screening: signs/symptoms or known CVD requires clearance. RegistrationRulesHelper owns registration requirements. Medical answers never enter /predict, and a label cannot waive clearance. Organizer review remains final.

---

## Weather, difficulty, and Event snapshots

Weather is separate from ML: forecasts are mutable and near-event while registration precedes them. WeatherService supplies forecast/risk/advisory data, never an input, explanation, or model claim.

DifficultyCalculator is the sole provisional, display-only route-effort policy:

~~~text
T0 = distanceKm / 5 + elevationGainMeters / 600
score = 5 × classMultiplier × (T0 + 0.25 × max(0, typicalDurationHours − T0))
~~~

Class multipliers are 1.00/1.15/1.35/1.60; Class 4 has a score floor of 35. The score rounds once to two decimals away from zero. Labels are below 35 Minor Hike, 35 through below 75 Major Hike, and 75 or higher Major Hike - Difficult. This is TrailGuard policy, not a validated external scale or ML input. Sort by bucket, stored canonical score, then stable event ties; display unknown labels explicitly.

EventTrailSnapshotHelper.CaptureSnapshot(Event, Trail) is the single writer for Trail identity, location, difficulty/score, and all name/distance/duration/elevation/terrain/class/thumbnail snapshots. It captures from a fresh persisted Trail. Editing without a Trail change preserves every snapshot; deliberate Trail replacement captures all values atomically. Completed Events cannot be edited. Historical displays, assessment inputs, and participant progress use snapshots, not live Trail values.

EstimatedDuration is an organizer-editable per-event estimate, not the inference input. TrailDurationHoursSnapshot is the recorded trail duration used by inference and route effort.

---

## Event retention and cancellation

There is no active Event hard-delete path. Events and linked registrations, assessments, results, documents, and snapshots are retained. Cancellation requires a reason and records status/time/reason.

A cancelled Event closes registration, retakes, payment, approval/rejection, verification, and new alternative recommendations without rewriting existing registration status, payment data, receipts, assessments, results, documents, or snapshots. Mutating processors re-check persisted Event status after locks and reload. Assessment persistence checks again after inference before replacing/saving a graph; overdue-expiry excludes cancelled Events before and after its lock. Completed Events remain read-only.

Only authorized owners may run organizer actions. Keep antiforgery and locks. A participant can have historical rows for one Event, so resolve workflow rows explicitly and deterministically—never unordered FirstOrDefault.

---

## Authorization, registration, and data safeguards

The only operational roles are Admin, Organizer, and Participant. Role creation and assignment use the operational-role allow-list; an account must not silently accumulate conflicting operational roles. Preserve active-account checks, role authorization, Organizer ownership checks, and participant identity checks on every route.

Registration state is persisted workflow data, not a browser choice. The participant can have historical attempts for the same Event, including cancelled or voided rows. Capacity and action availability use the established status helpers and event locks. Keep direct URL/API attempts subject to the same authorization, status, duplicate, and stale-state protection as the UI.

Medical-clearance documents, payment receipts, and other sensitive uploads are private application resources, not public static files. Preserve file-signature validation, storage access controls, and authorization checks. Never expose a document URL merely because a related record is visible.

Event feedback and post-event assessment workflows retain their explicit eligibility rules. Cancelled Events remain retained historical records but are not eligible for new processing. Do not reuse assessment output, medical data, payment data, or cancellation status for analytics, ranking, or profile disclosure unless the owning service explicitly authorizes that use.

---

## Current interface notes

- Add Trail validates before pending state begins; after a valid full-page submit starts, its button disables and shows Saving, preventing duplicates. Failed validation remains retryable.
- Assessment Question 9 describes technical Trail Classes: Walking, Hiking, Scrambling, and Simple Climbing.
- /Home/Privacy is a public, accessible responsive privacy notice. Contact and formal retention content remain unresolved; do not invent either.
- Popular Trails uses corrected distance/elevation statistics. Mobile hides its carousel scrollbar while retaining touch/swipe navigation.
- Follow DESIGN.md for current responsive layouts, accessibility, component-scoped scrolling, and role-specific data projections.

## Historical material and unresolved work

Historical v1/v2/v3 work remains in Git history, but must be labeled historical. Do not attribute historical accuracy, outcome/calibration claims, monotonicity guarantees, thresholds, NPS calculations, expert validation, or 16-feature behavior to trailguard-v2.0.0.

- Active-model evaluation/calibration evidence is still needed before real-world performance claims.
- The requested agency-provenance wording is awaiting confirmation. It requires agency-source documentation plus a training/run record linking that source/dataset SHA-256 to this frozen model artifact.
- Privacy Policy contact and retention schedule await approved content.
