# TrailGuard — Project Context

Capstone project: **TrailGuard — A Web-Based Hiking Event Management System with Machine Learning-Based Participant-to-Trail Suitability Assessment**

PUP College of Computer and Information Sciences. This repository started as an App Dev project using rule-based suitability scoring and has been extended into the capstone version with ML-based participant-to-trail suitability prediction and explainability. The rule-based path has since been removed outright, not just superseded — see "ML Failure — No Fallback" below.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Web framework | ASP.NET Core MVC, .NET 10 (C#) |
| ORM | Entity Framework Core 10.0.10 |
| Database | PostgreSQL 18 via Npgsql — migrated from MySQL |
| Authentication | ASP.NET Core Identity |
| Roles | Admin, Organizer, Participant |
| Frontend | Razor Views + Tailwind CSS v4 |
| UI style | Dark glassmorphism with TrailGuard brand gradient |
| ML service | Python 3.14 + FastAPI + XGBoost + SHAP |
| ML architecture | Separate Python process accessed through HTTP/JSON |
| Weather | Open-Meteo (geocoding + forecast) |
| Local development | PostgreSQL local install, no Docker |
| Planned cloud DB | Aiven PostgreSQL free tier — not yet deployed |

---

## Running the Project

Three processes, two of them required.

### Terminal 1 — ML service (required)

```bash
cd TrailGuard-ML
python -m uvicorn main:app --reload --port 8000
```

Must be run from inside `TrailGuard-ML/` — `main.py` loads the model files (`trailguard_xgboost_model_v2.json`, `label_encoder_v2.pkl`, `trailguard_synthetic_dataset_v2.csv`) from the current directory.

### Terminal 2 — Web application (required)

```bash
dotnet run
```

### Terminal 3 — Tailwind watcher (optional but recommended)

```bash
npm run dev
```

**Tailwind only generates classes it finds in source.** A new colour, radius, or utility won't render until `npm run build` runs. If a style isn't appearing, check whether the class exists in `wwwroot/css/output.css` before assuming the markup is wrong — this has caused hours of confusion here.

Database credentials live in **User Secrets**, not `appsettings.json`.

---

## Architecture

```
ASP.NET Core MVC (C#)  ──HTTP/JSON──▶  Python FastAPI (XGBoost + SHAP)
        │                                  localhost:8000/predict
        ▼
   PostgreSQL
```

The ML model can't run in-process because XGBoost and SHAP are Python-only. `SuitabilityApiClient.cs` bridges the two through `HttpClient`, configured in `Program.cs` with a timeout and a base URL from `MlApi:BaseUrl`.

---

## ML Failure — No Fallback

**This section reverses earlier guidance in this file that said to preserve a rule-based fallback. That guidance was wrong and has been acted on in the opposite direction: the fallback was deleted (commit `287c1a4`, "Remove the rule-based fallback; reject submissions when ML is unavailable"). Do not reintroduce it.**

The ML service is the **only** suitability mechanism. If it's unreachable or times out, `AssessmentController` shows an error and returns the participant to the form — no result is produced, nothing is saved, and the participant can retry once the service is back.

```
Primary:         ML prediction + SHAP explanation
If unreachable:  No result. Participant returns to the form and can retry.
Final decision:  Organizer
```

The legacy `GetResult()` heuristic no longer exists in `AssessmentController`. In its place is a code comment explaining why:

> No rule-based fallback: `GetResult()` was a v1 heuristic with its own notion of trail demand, agreeing with neither the model nor the ACSM/NPS-based ground truth in `generate_synthetic_dataset.py`. Producing a result from it would be a third, unvalidated answer to the same question. If the model can't answer, neither do we.

Concretely: `GetResult()`'s trail-demand formula was never reconciled with the NPS Shenandoah formula the model is now trained against, so the two could disagree — in testing, the same assessment returned Good-Match from the model and Borderline from the fallback. Silently falling back to it would mean a participant — including one reporting chest pain or another ACSM-gated condition — could receive a confident-looking result that bypassed the ACSM gate entirely, produced by a path nobody has validated the way the model and gate have been. Producing no result and asking the participant to retry is safer than producing a second, disagreeing one.

**Never reintroduce a rule-based fallback in the assessment flow, and never display legacy rule-based category scores beside an ML result** — the old category-score breakdown was removed from all participant-facing pages for exactly this reason, and that removal is unrelated to (and unaffected by) the fallback deletion above.

---

## ML Pipeline (`TrailGuard-ML/`)

| File | Purpose |
|---|---|
| `acsm_gate.py` | Single source of truth for the NPS Shenandoah rating, the difficulty-band tiering, and the ACSM preparticipation gate. Imported by both `generate_synthetic_dataset.py` and `main.py` so training-time and serving-time logic can't diverge |
| `generate_synthetic_dataset.py` | Generates the 6,000-row v2 synthetic training dataset; defines `FEATURE_COLUMNS` (the 14-feature contract) and `TERRAIN_MULTIPLIER` |
| `train_model.py` | Trains the v2 XGBoost model with monotonic constraints on 13 of 14 features |
| `evaluate_safety.py` | Sensitivity, monotonicity, and safety-regression checks; source of the safety tables in `MODEL.md` |
| `evaluate_gate_breakdown.py` | Reproduces the ACSM gate's rule-by-rule override statistics for `MODEL.md`, cross-checked against the dataset's own stored `suitability_label`/`gate_reason` columns |
| `main.py` | FastAPI service exposing `POST /predict` and `GET /model-info` |
| `trailguard_xgboost_model_v2.json` | Trained v2 model (committed; loaded by `main.py`) |
| `label_encoder_v2.pkl` | v2 class index → label mapping (committed; loaded by `main.py`) |
| `trailguard_synthetic_dataset_v2.csv` | v2 training data, 6,000 rows; also reloaded by `main.py` at startup to compute a live test-accuracy figure for `/model-info` |

`tune_model.py` and `test_shap.py` no longer exist.

The v1 files — `trailguard_xgboost_model.json`, `label_encoder.pkl`, `trailguard_synthetic_dataset.csv` — are still present on disk but are **not loaded by anything**. They exist only as the "before" side of the v1-vs-v2 comparisons in `MODEL.md`. Don't treat their presence as meaning v1 is still in use.

### Current model performance

```
Version:                  v2-acsm   (superseded: v1-synthetic)
Accuracy:                  91.42%   (v1: 80.50%)
Weighted F1:               0.9151   (v1: 0.8100)
Fidelity to the rule engine: 95.22% (v1: 80.50%)
Dataset:            6,000 rows, 14 features   (v1: 2,000 rows, 27 features)
```

These are the figures after the model was retrained to include Trail Class 4 (Simple Climbing) — every table in `MODEL.md` has been re-measured against that retrain; an earlier v2 build reported 91.75%/95.12%, which is superseded and appears only in `MODEL_EXPLAINED_EN.md`'s narrative.

These figures describe performance on **synthetic** data, measured against the project's own rule engine. They must not be presented as accuracy on real hikers — see "Known limitations" in `MODEL.md` for what independent validation exists and doesn't.

### Synthetic dataset basis

Cite these in the manuscript:

- **Trail demand** — NPS Shenandoah difficulty formula: `sqrt(elevation_gain_ft × 2 × distance_mi)`. Fed to the model directly as a feature (`trail_shenandoah_score`), not merely used to derive labels — this is the principal cause of the accuracy gain over v1
- **Terrain multiplier** — 1.00 / 1.15 / 1.35 / 1.60 across the four PinoyMountaineer Trail Classes (Walking / Hiking / Scrambling / Simple Climbing). Fitted against 28 Philippine mountains with published PinoyMountaineer difficulty ratings (Spearman rho 0.859; see "Difficulty Bands" below)
- **Fitness/health screening** — 2015 ACSM preparticipation screening algorithm: Riebe D, Franklin BA, Thompson PD, Garber CE, Whitfield GP, Magal M, Pescatello LS. *Updating ACSM's Recommendations for Exercise Preparticipation Health Screening.* Med Sci Sports Exerc. 2015;47(11):2473–2479
- **BMI** — WHO BMI classification for adults
- **Gear** — Ten Essentials Systems (The Mountaineers). The eight individual gear items remain on the participant checklist and drive the Recommendations panel; for the model they're collapsed into a single `gear_score` feature
- **Health** — an ACSM preparticipation clearance **gate**, not weighted binary flags (see "ML Labels and the ACSM Gate" below)

Labels come from a **demand-to-capacity ratio**, not z-score thresholding: `demand = trail_shenandoah_score × TERRAIN_MULTIPLIER[trail_class]`, compared against a participant readiness/capacity score and thresholded into Good Match / Borderline / Not Recommended. The ACSM gate is then applied on top of that label and can only lower it, never raise it.

Several constants feeding this remain `PENDING EXPERT ELICITATION` in `generate_synthetic_dataset.py`: the readiness component weights, the capacity range, the exact ratio thresholds, and the joint-injury gate rule (the one gate rule with no ACSM basis).

---

## Weather and ML

**Weather is deliberately excluded from the ML features.**

1. Reliable forecasts only exist close to the event date, not at registration
2. Conditions change after registration
3. There's no historical weather-incident dataset to build defensible synthetic rules from

```
Suitability ML  =  Participant + Trail characteristics
Weather         =  Separate event advisory
```

Weather provides forecast details, a rule-based risk level, and an organizer-editable reminder. It must not be described as an ML feature unless the architecture changes.

### Weather implementation notes

- Fetched **live** on the participant dashboard (`ParticipantController.GetEventWeather`) and event detail page, then **written back** to the Event — a forecast saved at event creation is stale by the event date
- An organizer-edited `WeatherReminder` is preserved across refreshes unless the risk level changes
- `Trail.Location` is stored as `"City, Province"`, which Open-Meteo's geocoder can't parse. `WeatherService` splits on the comma, searches the city with `countryCode=PH`, and matches the province against **both** `admin1` (region) and `admin2` (province)
- An out-of-range date returns **HTTP 400**, not null data — that's the `TooFarAhead` case, and it's a normal outcome (~16-day forecast horizon), not an error
- Failure reasons are distinct: `NoLocation`, `LocationNotFound`, `TooFarAhead`, `ServiceDown`, `Error`

---

## Assessment Input Contract

The form is not a set of independent questions — several answers map into the ML feature space and have dependencies.

### Age

Restricted to **18–60**, matching the synthetic training range (`age` is not itself a model input, but the scope of who the system will assess at all). Predicting outside it returns a confident-looking score with no basis. Documented as a limitation in `MODEL.md` to revisit at retraining, when real participant data covers a wider demographic.

Note: the BMI thresholds are the adult WHO ranges. Widening the age range later isn't just an input change — BMI handling would need revisiting, since children are assessed against percentile charts.

### First-timer dependency

Q8 (mountains climbed) drives Q9 (recency) and Q10 (hardest trail):

- **First-timer** → Q9 auto-selects "Never climbed", Q10 auto-selects "None", others disabled
- **Anything else** → "Never climbed" and "None" are disabled, and cleared if previously selected

Changing Q8 re-applies the rule. Nothing previously stopped a participant from answering "First-timer" plus "hiked 1–3 months ago".

### Medical conditions

"None of the above" is mutually exclusive with **six** listed conditions: Asthma, Hypertension/heart condition, Joint or knee injury, Vertigo/dizziness, Chest pain, and Shortness of breath. The last two were added specifically for the v2/ACSM migration — ACSM screens on all three cardiovascular signs and symptoms (dizziness, chest pain, shortness of breath), and v1 only asked about the first. In the ML feature space, all four cardiac-symptom checkboxes (vertigo, chest pain, shortness of breath — asthma is separate) collapse into a single `has_cvd_symptoms` flag.

### Exercise consistency

A question asking how long the participant has sustained their stated exercise frequency (Less than 1 month / 1 to 2 months / 3 months or more) feeds `exercise_consistency_score`. It exists because ACSM's "physically active" criterion requires at least 3 months of consistency at the stated frequency, which v1 had no way to know.

### Cardio duration boundary

The cardio-endurance options are "Less than 15 minutes / 15 to 29 minutes / 30 to 60 minutes / More than 60 minutes". The "15 to 29" / "30 to 60" split is deliberate — it lines up exactly with ACSM's 30-minute threshold instead of straddling it the way v1's "15 to 30 minutes" option did.

### Physical measurements

Age, height, and weight are range-validated **in JavaScript**. Native HTML `min`/`max` doesn't fire reliably inside hidden wizard steps — the same reason native `required` was dropped from the feedback wizard.

---

## Feature Mapping

`AssessmentController.BuildMlRequest` maps raw form answers into the 14 values the Python model expects. It uses a generic `MapScore(dictionary, value, fieldName)` helper against named lookup dictionaries (`ExerciseFrequencyMap`, `CardioEnduranceMap`, `ExerciseConsistencyMap`, `MountainsClimbedMap`, `RecencyOfHikeMap`, `TrailDifficultyCompletedMap`) — an answer that doesn't match a dictionary entry **throws** rather than silently defaulting to 0, since a silent default could make an unfit participant look fit, or vice versa. `trail.TrailClass` is sent straight through as `trail_terrain_type`; no separate mapping method is needed since `TrailClass` is already the 1–4 the model expects (and `BuildMlRequest` throws if it isn't).

The legacy `ComputeFitnessScore` / `ComputeExperienceScore` / `ComputeHealthScore` / `ComputeMlGearScore` methods still run and still populate `Assessment.FitnessScore` / `ExperienceScore` / `HealthScore` / `GearScore` / `TotalScore` — **but they no longer feed the ML request.** `BuildMlRequest` computes the model's features independently, from the same raw form values. These legacy fields are stored on `Assessment` and rendered nowhere in the current UI; see Known Cleanup.

Don't alter `BuildMlRequest`'s field mapping without checking the Python `FEATURE_COLUMNS` contract on both sides (`generate_synthetic_dataset.py` / `acsm_gate.py`).

---

## ML Labels and the ACSM Gate

The API returns `"Good Match"` (space). The application uses `"Good-Match"` (hyphen) throughout controllers and views. `NormalizeLabel()` converts between them.

Keep this — changing every usage site is riskier than the conversion.

`main.py`'s `/predict` applies an independent post-prediction safety check — the ACSM gate (`acsm_gate.apply_acsm_gate`) — after the model produces its label. **The gate can only lower a label, never raise one**, and runs four rules:

1. Signs or symptoms present (`has_cvd_symptoms`) → capped at Not Recommended, medical clearance required, regardless of fitness or intensity
2. Known CVD (`has_cvd`) and physically inactive → capped at Not Recommended, clearance required
3. Known CVD, physically active, vigorous-intensity trail → capped at Borderline, clearance required
4. Joint or knee injury on Trail Class ≥3 or a high-demand trail → capped at Borderline, no clearance flag (`PENDING EXPERT ELICITATION` — the one gate rule with no ACSM source)

Asthma deliberately triggers no gate rule — ACSM treats pulmonary disease as not an automatic referral, unlike cardiovascular disease.

The gate exists because the v2 model alone still occasionally predicts Good Match for a case the rules call Not Recommended (0.83 per 1,000 test rows, vs. 0 for v1). Applying the gate reduces that to 0. `SuitabilityResult.GateApplied` / `GateReason` record whether and why it fired for a given prediction, and `medical_clearance_required` in the API response drives `Assessment.MedicalClearanceRequired`.

---

## Difficulty Bands

`trail_shenandoah_score` (the plain NPS rating) is the only difficulty input the model itself sees. The band **shown to users** — on event, trail, report, and organizer pages, and as `SuitabilityResult.NpsBand` / `PredictionResponse.nps_band` — is a separate, deterministic step: the NPS rating is multiplied by the trail's `TrailClass` multiplier to get an *adjusted rating*, which is then mapped onto one of four PinoyMountaineer-derived tiers.

| Adjusted rating | Band | PinoyMountaineer level |
|---|---|---|
| < 81 | Easy | 1–2/9 |
| 81–354 | Minor Climb | 3–4/9 |
| 354–411 | Major Climb | 5–6/9 |
| ≥ 411 | Major Climb — Difficult | 7–9/9 |

These replace the published NPS bands (50/100/150/200), which are calibrated for Shenandoah National Park — 68% of the 28 Philippine mountains used to fit the boundaries above exceed the NPS bands' top threshold.

**This is not the PinoyMountaineer scale itself.** The system computes the NPS Shenandoah rating and maps it onto PinoyMountaineer difficulty tiers using boundaries calibrated on 28 Philippine mountains (82% exact-tier agreement, 100% agreement within one tier, Spearman rho 0.859). Applying PM's own written rule (duration + trail class) directly reproduced the same mountains' published ratings only 50% of the time — multi-day status in the Philippines often reflects logistics (e.g. camping for sunrise) rather than difficulty. **The 82%/100%/0.859 figures were measured on the same 28-mountain sample the boundaries were fitted to** — independent validation on mountains outside that sample hasn't been done; this is documented as a limitation in `MODEL.md`.

This logic is duplicated deliberately in two places that must be changed together:
- Python: `acsm_gate.shenandoah_rating()` / `nps_band()` (training and serving)
- C#: `Services/DifficultyCalculator.cs` — `ComputeRating`/`ComputeAdjustedRating`/`LabelFor` (event/trail/report pages)

Sort and compare by the **adjusted** rating, not the plain one — ordering by the plain NPS score would rank a short Class 4 trail as easier than a long Class 1 walk.

`Trail.Terrain` (a free-text string) and `Trail.TrailClass` (int, 1–4) are both still fields on `Trail` — `TrailClass` is the one that feeds difficulty and the ML request; `Terrain` predates it.

---

## Explainability

Required, not optional. Every ML prediction is accompanied by an explanation when SHAP data exists.

### Participant

Assessment report shows: result → confidence → "Why This Result?" → SHAP factors → recommendations.

SHAP factors use **Helped / Reduced** with a percentage representing that factor's share of total displayed impact.

Recommendations are **derived from negative SHAP factors**, not score thresholds. Trail-side features (`trail_shenandoah_score`, `trail_terrain_type`) are excluded — the participant can't act on them.

### Organizer

Registration review shows an "Assessment Explanation" panel using **Supported / Weakened**, with a disclaimer that the ML result is decision support only.

### Feature-name mapping — single shared source

`Services/ShapHelper.cs` is the single shared display logic and friendly feature-name mapping, reused by every page that renders SHAP factors: `AssessmentController` (the Assessment Report page), `RegistrationController.cs` (the My Registrations SHAP modal, participant-facing), and `OrganizerController.cs` (the RegistrationDetails Suitability Assessment panel) all call `ShapHelper.BuildDisplayItems`/`ShapHelper.GetFriendlyFeatureName` — none of them holds a second, private copy. `GetFriendlyFeatureName` covers all 14 v2 features and **throws** on an unrecognized name rather than silently falling through to the raw snake_case string, so a Python-side feature-contract change that isn't mirrored here fails loudly instead of rendering `has_cvd_symptoms` to an organizer or participant.

This file previously documented two divergent copies of this mapping (a stale v1 27-feature `ShapHelper` plus a separate, correct, private v2 copy inside `AssessmentController`) as a known, unfixed bug. That has since been resolved — confirmed by searching the repository for every `GetFriendlyFeatureName`/`BuildDisplayItems` call site — and this section is corrected to describe the current, single-mapping state.

---

## Confidence Display

Displayed to one decimal place, **raw — no cap**. It was previously capped at 99.9% in six locations; the cap hid a real, measured property of the model rather than fixing anything: a meaningful share of predictions saturate near 100% because the training labels are near-deterministic. That is documented as a known limitation in [`./MODEL.md`](./MODEL.md); capping the UI while documenting the saturation in the model card meant the two contradicted each other, and the model card is what gets read at defense. **The cap has been removed and must not be re-added** — it was already documented as deliberate once (commit `1939df0`) and the file you're reading is what would mislead a future session into restoring it.

```
MODEL.md               — model card, versions, metrics, limitations
MODEL_EXPLAINED_EN.md  — narrative on why v1 was rebuilt into v2
```

Shown to participants in: the participant dashboard, the My Registrations modal, and the assessment report (both the main panel and the sidebar). Also shown to organizers in RegistrationDetails, without the context line below (organizers get the disclaimer that ML is decision support only, covered under Explainability instead).

Wherever confidence is shown to a **participant**, it carries one line of context beneath it: "Confidence reflects how certain the model is that this result matches its trained rules. It does not measure whether the recommendation is right for you." Calibration measurement backs this precise a claim and no more — measured against the Trail Class 4 retrain: below 70% confidence the model agrees with the rule engine 68.6% of the time (245 cases), 70–90% agrees 93.9% (376 cases), 90–99% agrees 99.0% (291 cases), above 99% agrees 100.0% (288 cases). The number predicts agreement with the model's own training rules, not real-world correctness — don't word this line to imply the latter.

When there's no `SuitabilityResult` — the ML service was unreachable and the assessment was rejected rather than falling back to a rule-based guess (see "ML Failure — No Fallback") — show the label without a confidence value. Don't leave an empty space and don't invent one.

Only a single confidence figure (the winning class's probability) is stored per `SuitabilityResult`, not the full three-class distribution — see Known Cleanup re: the three-segment confidence donut.

---

## Decision-Making Rule

The system **never automatically approves or rejects**.

```
Participant → Assessment → ML prediction → ACSM gate → SHAP explanation
    → Registration → Organizer review → Organizer final decision
```

A `Not Recommended` result doesn't block registration. It triggers additional requirements and organizer review.

This is stated explicitly in the manuscript's Limitations section, and the interface must not contradict it.

---

## Registration Domain Rules

Suitability labels: `Good-Match`, `Borderline`, `Not Recommended`

Statuses:

```
Pending → Awaiting Payment → For Payment Verification → Accepted
```

Plus `Rejected`, `Cancelled`, `Voided`, `Alternative Recommended`. `RegistrationButtonHelper.GetState` is the single source of truth for what the participant-facing register/continue button shows for each status.

### Key decisions

| Decision | Reasoning |
|---|---|
| Payment happens **after** approval | Participant has 3 days from approval, capped at 11:59 PM the day before the event so the organizer can still verify |
| Expiry is a **lazy check**, not a background service | `RegistrationStatusHelper.ExpireOverdueRegistrations` runs at the top of every registration read path. Missing one reintroduces stale status |
| Cancellation blocked after approval | Only `Pending` and `Awaiting Payment` can be cancelled. Logistics are committed by then; anything later goes through the organizer |
| Post-event flows count only `Accepted` | A participant still in `For Payment Verification` isn't considered to have joined. Payment disputes at the trailhead are handled outside the system |
| Capacity counts all active statuses | Not just `Accepted` — an approved registration holds its slot during the payment window |
| Feedback requires `Accepted` + `Completed` | Feedback is an account of a hike the participant actually went on. See Feedback → Eligibility |
| Resolve a registration by status, never `FirstOrDefault` alone | A participant can hold several rows for one event (cancel, then register again). An unordered, unfiltered lookup can return the cancelled row, and `UpsertFinalLabel` then silently writes no label at all |

### Registration contact snapshot

`ContactNumber` and `Email` are stored **on the registration**, not read from the profile. The profile may hold a newer or different number than the one given for a specific event, and this is safety-critical information the organizer needs.

The organizer view reads the registration values first, falling back to account values only for rows predating this change.

Phone inputs use a fixed `+63` prefix with the local number starting at 9. Existing profile values with `+63` or a leading `0` are normalised before display so the prefix isn't doubled.

### Requirements by result

| Result | Medical clearance | Preparation plan |
|---|---|---|
| Good-Match / Borderline, no conditions | Optional | Not required |
| Good-Match / Borderline, with conditions | Required | Not required |
| Not Recommended | Required | Required |

`DecisionReason` is a free-text field on `EventRegistration`, persisted whenever the organizer approves, rejects, or otherwise decides — this is the "organizer decision reason" feature and it is fully implemented, not pending.

---

## Event Lifecycle

Explicit actions, not a status dropdown:

```
Upcoming ──┬── Reschedule (new date, stays Upcoming)
           ├── Cancel (reason required)
           └── Complete → Completed
```

Completion is **manual, never automatic** — hiking events have travel time, delays, and multi-day trips, so only the organizer knows when it's actually done. `CompletedAt` records when the organizer confirmed, not when the hike ended.

Completing an event **voids** all registrations still in `Pending`, `Awaiting Payment`, or `For Payment Verification`.

`Event.NotesAndReminders` (organizer-authored, separate from the weather-derived `WeatherReminder`) and the weather fields (`WeatherForecastAdvisory`, `WeatherRiskLevel`, `WeatherReminder`) are all implemented, persisted fields on `Event`. `Event.MASL` does not exist — elevation lives on `Trail.ElevationGainMeters`, not on the event.

### Completed Events are immutable

A `Completed` Event is a historical record — participant completion history, Trail Points/Tier/Rank, Achievements, post-event assessments, and the Organizer/Reports comparison views all read it. Once `Event.Status == "Completed"`, the Event stays fully **readable** (Details, assessment, comparison, participant history, progress calculations all keep working exactly as before) but is never editable or deletable, by Admin or Organizer, through any path:

- **Event Management cards** (`Views/Event/Index.cshtml`) render a Completed card's action row as `View` only — full-width, same secondary styling — never Edit or Delete. The condition (`eventItem.Status == "Completed"`) is evaluated server-side per card, inside the status-group loop, not per group heading — a stray/legacy status sharing a group with real Completed rows is not assumed to behave the same way.
- **`EventController.GetEvent`** (the Edit modal's hydration endpoint) rejects a Completed Event *after* the existing `CanManageEventAsync` authorization/ownership check succeeds, with the same JSON failure shape (`{ success: false, message: "Completed events are read-only and cannot be edited." }`) already used elsewhere — never editable hydration data. An unauthorized or missing Event still gets the existing indistinguishable `"Event not found"` response; the Completed-specific message only appears for a caller already authorized to manage that Event.
- **`EventController.EditEvent`** independently rejects a Completed Event using `existingEvent.Status` — the value already loaded from the database — never the posted `model.Status`, which line ~878 would otherwise apply verbatim and let a stale modal (or a crafted request) silently "reopen" a Completed event by posting a different value. The guard sits after both the Admin and Organizer-ownership branches succeed and before any further validation or mutation, so a stale Edit modal opened while the Event was still `Upcoming` — then marked `Completed` by another process before the organizer submits — is still rejected, since `existingEvent` is re-read fresh from the database on every request.
- **`EventController.DeleteEvent`** independently rejects a Completed Event the same way, after `CanManageEventAsync` succeeds and before `_context.Events.Remove`/`SaveChangesAsync` — no row, registration, assessment, or progress-related record is touched for a rejected request.

All three guards compare the exact stored string `"Completed"`, matching every other status comparison in this file — never the posted/client-supplied status. This is intentionally three independent, narrow checks rather than a new shared "Event lifecycle policy" abstraction — no such policy exists yet for these code paths, and `CanManageEventAsync` already owns the one thing that *should* be shared (ownership), so duplicating a three-line status comparison next to it is safer than inventing a new abstraction for two call sites. Upcoming and Cancelled Events are unaffected — this only restricts `Completed`.

### Final suitability labels

`FinalSuitabilityLabel` persists the empirical outcome for retraining:

- Both participant feedback and organizer assessment present → the **more conservative** label
- One present → use it
- Neither → **no record**, excluded from the retraining dataset

`AssessmentId` on that table is what links a label back to its features. Without it there's a label with nothing attached.

The upsert must handle edits — feedback arrives in either order and either side can be revised. `FinalLabelService.ComputeKappa` and `LabelOrder`/`LabelCategories` (`{ "Good-Match", "Borderline", "Not Recommended" }`, best-to-worst by array index) are the single source of truth for what "more conservative" and "accurate" mean; the Reports page (below) reuses the same service so the per-event and aggregate views can't define agreement differently.

---

## Reports: Aggregate Model Validation

`ReportsController` is **Admin-only** (`[Authorize(Roles = "Admin")]`) — `Index` and `Export` both require the Admin role; Organizer and Participant accounts cannot reach either. The dataset is system-wide (no `OrganizerId` scoping). The Reports link renders only in the Admin navbar; a dual-role Admin+Organizer account is allowed via its Admin role. It is the multi-event counterpart to `OrganizerController.EventComparison`, reusing `FinalLabelService` for every label comparison so "accurate" and the ordinal category order can't drift between the per-event and aggregate views.

It shows, over all resolved `FinalSuitabilityLabel` rows:

- A sampling-bias funnel: total assessments → registrations with an assessment → accepted → resolved final labels (each stage narrows, and the narrowing itself is informative about who never gets an outcome recorded)
- Accuracy breakdown (Accurate / Over-cautious / Missed risk / Unclassifiable) for both the pre-hike label shown to the participant and the model's label alone (pre-gate)
- Confusion matrices for both
- Cohen's kappa and weighted kappa (`FinalLabelService.ComputeKappa`), shown only once the sample is large enough (`ReportsController.MinSampleSize = 20`) — below that, only raw counts are shown
- Breakdowns by NPS difficulty band and by Trail Class
- A dedicated breakdown for the `Not Recommended` acknowledgement pathway — the only evidence the system has about whether its negative predictions were correct, since every other Not-Recommended participant either never registered or was rejected before an outcome could be observed
- CSV export (`ReportsController.Export`) of the full row-level data behind the report

This is new since the last time this file was accurate, and is **not yet in the UI/UX pass** (see below).

---

## Feedback

Three-step wizard:

1. **Hiking experience** — `DifficultyExperience` (this alone drives the final label)
2. **Trail conditions** — condition, signage, water availability, hazards
3. **Organizer evaluation** — rating, communication, safety, group management, comment

Sections 2 and 3 **don't** affect the suitability label. Trail condition and organizer quality are different questions from whether this participant suited this trail.

The wizard is one form with JS-toggled visibility, not real navigation — otherwise answers are lost going back.

### Single submission

Participant feedback is submitted **once and cannot be revised**. `DifficultyExperience` is empirical training data; if it were editable it could be edited after the participant has seen the outcome.

This is not the same thing as `FinalLabelService` tolerating change. The upsert must recompute rather than assume a first write, because the **organizer's** post-event assessment can be revised and the two sides arrive in either order. That is a requirement on the service, not permission for the participant to edit.

### Eligibility

The feedback form — both the GET and the POST — requires all three:

- the event exists
- `Event.Status == "Completed"`
- the current user holds an `EventRegistration` for that event with `Status == "Accepted"`

`Participant/Details.cshtml` gates the *Give Feedback* link on exactly this. The controller now enforces the same rule, because a gate that only exists in a view is not a gate.

All four failure modes return the **same generic message**. Don't distinguish "event not found" from "not yours" — see Security.

---

## Account Roles

Each active TrailGuard account has exactly one operational role:
Admin, Organizer, or Participant.

`Services/RoleAssignmentService.cs` (paired with the static `OperationalRolePolicy`) is the single source of truth for this — the exact allow-listed role names, reading an account's role integrity (`Admin` / `Organizer` / `Participant` / `Conflict` / `Missing`), creating a brand-new account with its initial role, and the exclusive role-replacement flow used by Admin. Every account-creation and role-edit path (`AccountController.Register`, `AdminController.AddAccount`, `AdminController.ChangeRole`, `Data/DbSeeder.cs`) goes through it rather than calling `UserManager.CreateAsync`/`AddToRoleAsync`/`RemoveFromRoleAsync` directly.

**Account creation is transactional.** `RoleAssignmentService.CreateAccountWithRoleAsync` runs `UserManager.CreateAsync` and the initial role assignment inside one database transaction on the same DI-scoped `ApplicationDbContext` `UserManager`'s store uses — a role-assignment failure (or the final role set not converging to exactly the requested role) rolls the new user row back too. There is no committed, role-less account left behind to compensate for with a follow-up delete.

**Existing multi-role or role-less accounts are never auto-mutated.** Startup only *audits* and logs an aggregate warning (`Program.cs`, via `RoleAssignmentService.AuditRoleIntegrityAsync`) — resolving a conflict or a missing role requires an explicit Admin choice through Account Management's Resolve action. There is no "keep the highest role" or "navbar precedence" normalization anywhere in the codebase.

The navbar's Admin-first role check (`Views/Shared/_Layout.cshtml`: `User.IsInRole("Admin")` before Organizer/Participant) is a **transitional defensive fallback** for an unresolved historical conflict, not the policy itself — once an account holds exactly one operational role, that check only ever matches its actual role. `AccountController.Login` has the same defensive Admin-first redirect precedence. Reports remains Admin-only regardless (see below).

`RoleAssignmentService.ReplaceRoleAsync` also enforces, inside one flow: a normally configured single-role Admin can't change their own role (a conflicted account holding Admin may only resolve itself to Admin — the one safe self-repair path); the last Admin account can't be demoted; and an Organizer can't be moved off the Organizer role while they still own `Upcoming` Events (`Event.OrganizerId`) — Completed/Cancelled history keeps its stable `OrganizerId` regardless of the account's current role. `RoleAssignmentService.SetAccountActiveAsync` (used by `AdminController.ToggleAccountStatus`) applies the same last-Admin protection to deactivation.

**`ApplicationUser.IsActive` is enforced at sign-in** (`AccountController.Login`, via `SignInManager.CheckPasswordSignInAsync` before any cookie is issued) — a disabled account can no longer sign in. The check happens only *after* the password itself has already validated, so a wrong password reads identically whether the account is active, disabled, or doesn't exist, and disabled-ness is never leaked through a failed-login response.

**Every role or active-status change updates the target's security stamp**, but this does not terminate an already-issued cookie immediately — ASP.NET Core Identity's default `SecurityStampValidator` interval (30 minutes; `Program.cs` never overrides it) is the actual bound on how long a stale session keeps its old claims. The one exception is the acting Admin resolving their own conflicted account, which also calls `SignInManager.RefreshSignInAsync` to fix their *own* current session immediately.

**Last-Admin checks are concurrency-protected**, not just ordered-before-the-write: removing Admin through `ReplaceRoleAsync` and disabling an account through `SetAccountActiveAsync` both re-read the target and re-count other Admins inside a `Serializable` transaction (Npgsql/PostgreSQL), so two concurrent requests each removing the last Admin from a different account can't both commit — PostgreSQL aborts the second with a `40001 serialization_failure`, which surfaces as a generic "changed concurrently, try again" result. Every other role/status change keeps the provider's default isolation.

---

## Participant Progress, Ranking, and Achievements

There is **no public leaderboard yet**. Progress/ranking calculation, dynamic achievement evaluation, and the read-only Participant Profile page are all implemented — this section documents their actual current behavior.

### Canonical participation rule

TrailGuard-recognized participation (not independently verified physical attendance — the system has no attendance marker beyond this) is:

```
EventRegistration.Status == "Accepted"
AND Event.Status == "Completed"
```

Because a participant can cancel and re-register for the same Event, and the schema does not forbid more than one historical row reaching this state, every qualifying count is **by distinct `EventId`**, never by raw registration-row count. `ParticipantProgressService.GetProgressAsync` collapses duplicate rows for the same `EventId` before counting anything.

### Trail Points and tiers

`ParticipantProgressPolicy` is the single source for the formula, the tier table, and competition-rank math — nothing here is duplicated in a controller, service, or view.

```
TrailPoints = (DistinctCompletedEventCount × 10) + (DistinctCompletedTrailCount × 5)
```

No payment state, assessment outcome, suitability result, ML/SHAP data, feedback, medical data, cancellation behavior, or editable profile field feeds this — only `EventRegistration.Status`, `Event.Status`, `Event.TrailId`.

| Tier | Trail Points |
|---|---|
| Trail Starter | 0–14 |
| Pathfinder | 15–74 |
| Trail Explorer | 75–149 |
| Summit Seeker | 150–299 |
| Trailblazer | 300+ |

A clean, active Participant with zero qualifying completions is `Trail Starter` but is never ranked.

Each tier also has a stable, code-defined key (`trail-starter`, `pathfinder`, `explorer`, `summit-seeker`, `trailblazer`). `ParticipantProgressPolicy` stores the name, key, and minimum-points threshold for all five tiers together in one private immutable catalog (never three separately maintained arrays), and resolves a Trail Points value's name (`TierFor`) and key (`TierKeyFor`) from that same one lookup — never a second independent threshold chain, and never derived from the display name itself at render time. The key is the only legal source for a Tier emblem asset path (`/images/tiers/tier-{key}.webp`); a future display-name rename must never change which file is shown. Each of the five tiers has a fixed, original WebP emblem (`wwwroot/images/tiers/`, 512×512, transparent). Tiers still advance automatically from Trail Points alone; there are no sublevels, and achievements do not currently award points or gate tier advancement.

`ParticipantProgressPolicy.TierPreviewEntriesFor(trailPoints)` is the only way a caller may enumerate all five tiers together — it returns a fresh read-only list of `ParticipantTierPreviewEntry` (key, name, `IsCurrent`, `IsUnlocked`, `Position`), computed from the same lookup `TierFor`/`TierKeyFor` use, never the private catalog or its entries directly. This backs the Profile page's Tier preview carousel (see Profile page, below) and is the single source of truth every carousel slide's key/name/locked-state comes from — no second, view- or JavaScript-local list of tier names/keys/thresholds exists anywhere.

### Leaderboard eligibility and ranking

All-time only; nothing seasonal exists. Eligible for ranking means: active, exactly one operational role (`OperationalRolePolicy.Evaluate`), that role is `Participant`, and at least one qualifying completed Event. Admins, Organizers, inactive accounts, conflicted/missing-role accounts, and zero-completion Participants are excluded from the ranked population entirely — they don't count toward the denominator either.

```
rank = (number of eligible participants with strictly higher Trail Points) + 1
```

Equal Trail Points share the same rank; there is no tie-breaker. `RoleAssignmentService.GetActiveUserIdsInSingleRoleAsync` is the bounded (constant-query-count), read-only helper that supplies the eligible population — it reuses `OperationalRolePolicy.Evaluate` rather than calling `GetRolesAsync` per account, the same shape `AuditRoleIntegrityAsync` already uses.

`ParticipantProgressService.GetProgressAsync(userId)` takes no caller-supplied leaderboard flag — eligibility is decided entirely inside the service (`activeValidParticipantIds.Contains(userId) && distinctCompletedEventCount > 0`, where `activeValidParticipantIds` comes from `RoleAssignmentService.GetActiveUserIdsInSingleRoleAsync("Participant")`), so no caller can accidentally rank an inactive, conflicted, missing-role, Admin, or Organizer account. An inactive clean Participant (an Admin viewing historical data, once a Profile page exists) still gets a computed tier and history, but never a rank — `IsRanked` is `false` and `Rank`/`RankedParticipantCount` carry no placement. This never affects the Dashboard itself, since a disabled account cannot sign in (see Account Roles) and therefore never reaches it.

### Achievements

Nine fixed, code-defined achievements (`ParticipantAchievementCatalog.Definitions`) — never database rows, never written or unlocked anywhere. `ParticipantAchievementEvaluator.Evaluate` is a pure function: no database access, no persistence, no notion of "already unlocked" carried between calls. Every call recomputes all nine results from scratch against the same deduplicated qualifying history `ParticipantProgressService.GetProgressAsync` already loaded for Trail Points/tier/rank — there is no second history query, and no per-achievement query.

| Code | Name | Category | Criterion |
|---|---|---|---|
| `first_adventure` | First Adventure | Milestone | 1 distinct qualifying Event |
| `five_adventures` | Five Adventures | Milestone | 5 distinct qualifying Events |
| `double_digits` | Double Digits | Milestone | 10 distinct qualifying Events |
| `new_ground` | New Ground | Exploration | 3 distinct Trails across qualifying Events |
| `trail_collector` | Trail Collector | Exploration | 5 distinct Trails across qualifying Events |
| `steady_steps` | Steady Steps | Consistency | Qualifying Events in 3 distinct calendar months |
| `seasoned_explorer` | Seasoned Explorer | Consistency | Qualifying Events in 6 distinct calendar months |
| `technical_explorer` | Technical Explorer | Variety | Qualifying Events across 3 distinct valid (1–4) Trail Classes |
| `versatile_hiker` | Versatile Hiker | Variety | Qualifying Events across 3 distinct canonical Event Difficulty levels |

Distinct months need not be consecutive — this is never described as a streak. Trail Class is described accurately as technical trail metadata (`Trail.TrailClass`), never as "Event Difficulty." None of the nine touch payment state, medical data, assessment/suitability/ML/SHAP results, organizer approval rate, cancellation behavior, self-entered profile fields, or a specific Trail Class/Event Difficulty/speed/distance/elevation **threshold** — the achievement system must never pressure a Participant toward a harder or less suitable Event just to earn a badge. Technical Explorer and Versatile Hiker are the two exceptions to "never touches Trail Class/Event Difficulty at all": both count *distinct values reached*, never a minimum difficulty/class a Participant must clear, so neither can be satisfied by seeking out harder Events — a Participant who only ever completes Easy-rated Events is never nudged toward a Major Climb to progress either one.

**Versatile Hiker's difficulty source.** `Event.Difficulty` is a plain string column with no database-level constraint, but it is only ever written by `DifficultyCalculator.ComputeDifficulty` (see Difficulty Bands, above) — so in practice every qualifying completion's difficulty is already one of `DifficultyCalculator.Bands`' four canonical values (`Easy`, `Minor Climb`, `Major Climb`, `Major Climb — Difficult`). `ParticipantAchievementEvaluator.NormalizeDifficulty` is what turns the raw stored string into a trusted value for this achievement: it trims whitespace and matches case-insensitively against `DifficultyCalculator.Bands` (the same single canonical source every other Difficulty consumer in the app already reuses — see Difficulty Bands), returning `Bands`' own canonical casing so two differently-cased matches for the same band collapse into one distinct-difficulty count. Null, empty, whitespace-only, and any value that doesn't match one of the four Bands (a stray/legacy row) are excluded outright and can never advance or unlock Versatile Hiker — there is no fifth, invented difficulty level.

Each of the nine also has a fixed, original **512×512 transparent WebP badge** (`wwwroot/images/achievements/achievement-{key}.webp`) and a stable `AssetKey` (`AchievementDefinition.AssetKey`, e.g. `first-adventure`) assigned per-entry in `ParticipantAchievementCatalog`, never derived from `Name`/`Code` at render time. `ParticipantAchievementEvaluator` copies `AssetKey` straight through onto `ParticipantAchievementResult`, so the Profile view (the only current renderer) builds the image path from that field alone — no title-to-filename transformation in Razor, and no database/route/query/user-controlled value ever reaches an asset path. At the Profile owner's `xl:grid-cols-3` breakpoint, nine cards form a complete 3×3 grid with no empty final slot.

**Dynamic, non-persisted design.** Nothing is written when a Dashboard or future Profile is viewed, by anyone — including an Organizer/Admin viewing a Participant's Profile. No migration or backfill exists or is needed: existing history is automatically reflected the first time this runs, a corrected Registration or Event status changes achievement progress on the very next read, and an achievement **re-locks** if the qualifying history that satisfied it no longer does. Do not describe an unlocked achievement as a permanent certificate — it is a live reflection of current history, not an award record.

**Earned dates** use the qualifying Event's own `EventDate` — never the administrative `CompletedAt` timestamp — derived by walking the same chronological history (ordered by `EventDate` ascending, then `EventId` ascending as a deterministic tie-breaker for same-day Events) once: a completion milestone's date is the Nth distinct qualifying Event's `EventDate`; a distinct-Trail/month/Trail-Class/Difficulty milestone's date is the `EventDate` of the qualifying Event on which that Trail/month/Trail Class/canonical Difficulty was *first* seen — repeating an already-seen Trail, month, Trail Class, or Difficulty advances the completed-Event count but never that achievement's progress.

### Participant Dashboard

`ParticipantController.Index()` no longer runs its own grouped ranking query. Completed-hike count, Trail Points, rank, the ranked-hiker denominator, and ranked/unranked state all come from `ParticipantProgressService`. The personal-best difficulty/distance/elevation figures shown alongside them remain a separate, controller-local `Max`/`OrderByDescending` selection over the same Accepted+Completed registrations — safe to leave separate because a duplicate row for the same Event cannot change a maximum, only a plain count would need the shared service's deduplication.

The Dashboard's `ParticipantProgressResult` now also carries `Achievements`/`EarnedAchievementCount`/`TotalAchievementCount`, but nothing on the Dashboard itself renders them — no achievement badges, locked-progress list, or count appears there. That data is rendered on the Profile page instead (below).

### Public Profile identifier

`ApplicationUser.PublicProfileId` (`Guid`, non-null, unique-indexed) is the routing key for `GET /Profile/{publicProfileId:guid}`. It is never derived from email, name, or the internal Identity `Id`, and is assigned via a C# property initializer (`= Guid.NewGuid()`) on every `new ApplicationUser` construction, so every existing creation path (`AccountController.Register`, `AdminController.AddAccount`, `DbSeeder`) gets one automatically with no controller-specific assignment. Existing rows were backfilled by migration `AddPublicProfileIdToUsers` (nullable column → per-row `gen_random_uuid()` backfill → `NOT NULL` → unique index) — never a single baked-in default applied to every row. The internal Identity `Id` must never be exposed through a Profile-facing response, log line, or view — `ProfileController` reads it only to pass to `ProfileAccessService`/`ParticipantProgressService` within the same request and never renders or logs it.

### Profile authorization

`ProfileAccessService` is the authorization boundary for both Profile routes. It resolves a viewer/target pair into one of: Owner, Admin, or Organizer access, or a single generic denial (`ProfileAccessResult.Denied`) for every rejected or unresolvable case — unknown public id, non-Participant target, unauthorized Participant viewer, unrelated Organizer, an inactive target viewed by an Organizer, or a conflicted/missing-role viewer. `ProfileController` maps every denial to the same `NotFound()`, never a distinguishable message.

Organizer access requires an owned Event (`Event.OrganizerId == organizer.Id`) and a registration from the target Participant in one of exactly five statuses, defined once in `ProfileAccessPolicy.OrganizerRelationshipStatuses` — `Pending`, `Awaiting Payment`, `For Payment Verification`, `Alternative Recommended`, `Accepted`. This is a **separate, private policy** from `RegistrationStatusHelper.ActiveStatuses`: the two overlap but answer different questions (capacity/duplicate-registration vs. Profile visibility) and must never be collapsed into one. `Rejected`, `Cancelled`, and `Voided` never grant Profile access. Admin access requires the viewer to be a clean, single-role Admin, and may reach both active and inactive clean Participants; it can never resolve an Organizer or Admin account as a Participant Profile. A role-conflicted or role-missing viewer gets no Profile privilege regardless of which raw role rows it holds — the navbar's Admin-first fallback (see Account Roles) is display convenience only and is never treated as authorization here.

### Profile page

`ProfileController.Index(Guid? publicProfileId)` serves both `GET /Profile` (the signed-in Participant's own Profile, via `ProfileAccessService.ResolveOwnAsync`) and `GET /Profile/{publicProfileId:guid}` (contextual access, via `ResolveAsync`) — one action, `[Authorize]` with no role restriction, so an anonymous request is challenged before either branch runs. After authorization, the controller projects only display-safe `ApplicationUser` fields (first/last name, profile picture, bio, member-since date, email, phone number, Facebook link — no password/security fields or internal Id) into `ProfileViewModel`, and calls `ParticipantProgressService.GetProgressAsync`/`GetRecentAdventuresAsync` for stats, tier, rank, achievements, and recent history. Display full name is `FirstName + LastName` (no `MiddleName`), matching the existing `AccountManagementViewModel.FullName` display convention in Admin > Account Management — `MiddleName` is used elsewhere in the app only for a separate legacy string-matching convention (reconciling a free-text `OrganizedBy` snapshot against a `User` row), never for display. Email/phone/Facebook are existing `ApplicationUser`/Settings fields, not new database state; they render only inside this already-authorized response, never through a new endpoint or route. A stored Facebook value only becomes a clickable link when `ProfileController.SafeAbsoluteHttpUrl` confirms it parses as an absolute `http`/`https` URI (`target="_blank" rel="noopener noreferrer"`) — anything missing, relative, or on an unsafe scheme (`javascript:`, `data:`, etc.) renders as `Not provided` instead.

The page is four separate `rounded-2xl` cards, not one combined hero: a top row (Profile card + Tier Progress card, `items-start lg:items-stretch` — stacked mobile/tablet cards keep their own independent natural height, side-by-side desktop cards stretch to the taller one's height), Summary cards, then a lower row (Recent Hikes at 1/3 width, Achievements at 2/3, `lg:grid-cols-3` with `lg:col-span-1`/`lg:col-span-2`) — see DESIGN.md, Profile, for the exact layout. The Profile card is structured, not a single centered block: a header row (icon + "Profile" heading, a fixed "Participant" context label), a horizontal identity block (the participant's own avatar — photo or initials, the sole identity image — alongside name/Member Since/Bio), a "Contact Details" group (Email/Contact Number/Facebook only — Member Since lives beside the name, never duplicated here), and (owner-only, bottom-anchored via `flex flex-col`/`mt-auto`) the `Edit Profile` link.

**The Tier Progress card's contents differ by viewer**, decided server-side by `Model.IsOwner` (Razor `@if`, not CSS/JS concealment) — this is the only place on the page where owner-vs-visitor content differs beyond Achievement visibility. The owner sees a manual preview carousel built from `ParticipantProgressPolicy.TierPreviewEntriesFor(progress.TrailPoints)` (exposed as `ProfileViewModel.TierPreviewEntries`/`CurrentTierIndex`) — all five tiers' emblems+names+lock-status are server-rendered up front as one combined unit per tier (`[data-tier-slide]`), plus the actual "Your Progress" bar/copy, Trail Points, rank placement, and the Trail Points calculation disclosure beneath it. An authorized Organizer/Admin visitor gets no arrows, no other tier slides, no "Your Progress" heading, no points-to-next-tier copy, and no progress bar — but otherwise sees the same server-computed values as the owner: the heading, the fixed "Current Tier" row (shown to every viewer), the participant's actual current emblem and display name, and (below the emblem) the same Trail Points row, rank placement/ranked-explanatory text, and Trail Points calculation disclosure the owner sees. Only the next-tier progress detail is owner-only; Trail Points and rank are not. The visitor's emblem uses the same controller-validated `Model.TierKey`/`Model.Tier` the owner's "Current Tier" row already uses. `wwwroot/js/profile-tier-carousel.js` (Profile-page-only, included via `@section Scripts`, guarded to no-op when its `[data-tier-carousel]` element doesn't exist — which is always true in the visitor branch, since that markup isn't merely hidden, it's never rendered) only toggles which slide is visible/settled by index; it never contains a tier name, key, or threshold itself.

Browsing the carousel never changes the participant's actual tier/progress — the "Your Progress" bar and copy beneath it are computed once from `progress.TrailPoints`/`ParticipantProgressPolicy`, independent of whatever slide is currently previewed. Navigating plays a short (~200ms), direction-aware slide+fade (next → exits left/enters from right; previous → exits right/enters from left) built from a fixed sequence of literal Tailwind opacity/translate-x classes toggled via `classList` (never constructed from string fragments, so the Tailwind scanner can always find them — see `wwwroot/css/input.css`'s `@source` list, which already includes `wwwroot/js/**/*.js`); a transition lock (`isAnimating`) drops any click/keypress that arrives mid-transition rather than queuing or overlapping it, and `prefers-reduced-motion: reduce` swaps directly to the new slide with no transition at all. The initial page load is never animated - the server-rendered markup already matches the resting state exactly. The emblem is `alt=""`/`aria-hidden="true"` since adjacent text (the fixed "Current Tier" row above the carousel, and each slide's own name/status caption) already carries the same information for a screen reader; the carousel stage is `aria-live="polite"` so a navigation's final selected tier is announced once, not per intermediate animation frame. The same historical tier/emblem renders identically for the owner, an authorized Organizer, and an Admin viewer (including an inactive target Admin can see — see Leaderboard eligibility above for what that does and doesn't affect) — only the surrounding interactive/detail markup differs by viewer, never the underlying data.

Achievement visibility differs by viewer: the owner sees all nine (locked and unlocked, with locked progress); an Organizer or Admin visitor sees only `ParticipantProgressResult.EarnedAchievements` — locked achievements and their progress are never shown to anyone but the owner. This filter is unaffected by presentation: each authorized card (whichever set the controller already resolved) renders as a compact badge — resting state shows only the WebP badge, the achievement title, and its progress bar; the exact requirement description, current-vs-target progress, and (once earned) earned date are never shown at rest and only surface in a per-card requirement reveal, available via mouse hover, keyboard focus, or mobile tap (`wwwroot/js/profile-achievements.js`, Profile-page-only, included once from `Views/Profile/Index.cshtml`'s existing `@section Scripts` alongside `profile-tier-carousel.js`, and a no-op when no achievement cards are rendered). An earned badge renders full color; a locked badge is `grayscale opacity-40`, matching the existing locked-tier-emblem treatment — the underlying WebP is never modified for either state. None of this changes what `ParticipantAchievementEvaluator` computes, when an achievement counts as earned, its earned date, or which achievements a given viewer is authorized to see. Recent Hikes (heading text only — the underlying `ParticipantProgressService.GetRecentAdventuresAsync`/`RecentAdventures` name and behavior are unchanged) reuses the identical Accepted+Completed, distinct-`EventId` canonical rule as every other progress figure, newest-first, capped at 20 server-side; Organizer names are resolved in one bulk query per request (never per row) with `null` → "Unassigned" and an unresolved id → "Organizer unavailable."

There is still no public/anonymous Profile, no participant-to-participant Profile viewing, no public leaderboard, and no Profile editing on this page (the owner's only action is a link to the existing Settings page).

### Profile entry points

Three entry points exist:

- The Participant navbar's account dropdown (desktop and mobile) reads "Profile" instead of "Dashboard" and routes to `GET /Profile` — the top-level Dashboard nav link is unchanged.
- A Registered Participant row on the shared Admin/Organizer Event Details page (`EventController.Details` / `Views/Event/Details.cshtml`) becomes a link to `GET /Profile/{publicProfileId:guid}` when `EventParticipantRowViewModel.CanViewProfile` is true, computed from one bounded, page-level bulk lookup (`RoleAssignmentService.GetRoleIntegrityStatusesAsync`) rather than a role query or a `ProfileAccessService.ResolveAsync` call per row. A row that isn't linkable renders as plain, non-interactive content — no chevron, no dead anchor, no raw id/email fallback.
- A "View Profile" link in the participant identity card on the Organizer's Registration Details page (`OrganizerController.RegistrationDetails` / `Views/Organizer/RegistrationDetails.cshtml`), gated by one direct `ProfileAccessService.ResolveAsync` call per page load (this page shows exactly one participant, so a per-row bulk lookup doesn't apply). This route is `[Authorize(Roles = "Organizer")]` only — Admin has no access to `OrganizerController` today, so this link is Organizer-only until/unless that authorization changes; it was left unchanged, not broadened, to add this link. Denied cases (Rejected/Cancelled/Voided-only relationship, inactive target, missing participant identity, or any other `ProfileAccessService` denial) render no link, no disabled control, and no `PublicProfileId` in markup.

No other entry point exists yet: not on the Participant Dashboard, Participant My Registrations, Admin Accounts, Admin Dashboard, Records, Reports, or any Participant directory — none of those were touched by this work.

---

## Settings

`SettingsController` (`[Authorize]`, no role restriction) is the account owner's own editable profile and password — distinct from the read-only Profile page above, which is the participant's public-facing hiking identity. Every action resolves the user from `_userManager.GetUserAsync(User)` (the authenticated cookie), never from a route parameter, posted id, or claim value — there is no way to open or submit Settings for another account.

**Two independent forms, one shared `UpdateProfileViewModel`.** Profile Information posts to `UpdateProfile`; Security posts to `ChangePassword`. Both carry `[ValidateAntiForgeryToken]`. Because both forms bind the same model class, each action explicitly `ModelState.Remove`s the other form's fields before checking `ModelState.IsValid` — otherwise the model binder's validation of properties that were never posted (defaulting to `""`) fails the *other* form's submission for reasons unrelated to what was actually typed. This is also why `[Phone]` and `[Url]` (on the optional `PhoneNumber`/`FacebookLink` fields) are explicitly cleared when blank: neither DataAnnotation treats an empty string as valid the way `[Required]`'s absence implies "optional" — confirmed empirically (`PhoneAttribute.IsValid("") == false`, `UrlAttribute.IsValid("") == false`) rather than assumed, since getting this wrong would reject every profile save that left an optional field blank.

Both actions now actually enforce `ModelState.IsValid` (previously dead — the declared attributes, including `ConfirmPassword`'s `[Compare("NewPassword")]`, were never checked, so a mismatched password confirmation silently succeeded). On failure, both redirect back to `Index` with a combined message in `TempData["Error"]` — the same toast-and-full-reload pattern the page already used for every other failure path, not a new per-field inline-error round trip. `asp-validation-for` spans in the view are populated by jquery-unobtrusive **client-side** validation only; a server-side rejection (JS disabled, or a value that only fails the stricter server-side checks below) surfaces as the generic toast, not an inline message next to the field. Changing that to a true server round-trip display is a real interaction-pattern change, not a bug fix, and was left alone.

**Facebook link.** `[Url]` on the model is deliberately loose (it preserves its existing jquery-unobtrusive client validation) and is *not* the enforced rule. `UpdateProfile` additionally checks `Uri.TryCreate(..., UriKind.Absolute)` plus an `http`/`https` scheme — the same rule `ProfileController.SafeAbsoluteHttpUrl` uses to decide whether a stored value ever renders as a clickable link on the Profile page. A value that passes Settings' check is guaranteed to render there; before this fix, a value that passed `[Url]` (e.g. `ftp://...`) could be saved but would always silently show as "Not provided" on Profile, with no indication why.

**Profile picture upload** is validated server-side, not just by the browser's `accept` attribute: size is capped at 5MB, and the file's first bytes are checked against the JPEG (`FF D8 FF`) and PNG (`89 50 4E 47 0D 0A 1A 0A`) signatures — never the client-supplied filename or declared content type, both fully attacker-controlled. The stored filename is always server-generated (`Guid.NewGuid() + <extension implied by the validated signature>`); the original upload filename is never used to build a path, closing a path-traversal gap that existed before (the old code appended the raw client filename directly into the stored path). A validation failure saves nothing and leaves no orphaned file — the image is fully read into memory and checked before any existing file is deleted or any new one written.

**Known limitation — signature-only, not full decode validation.** `SettingsController.ReadValidatedImageAsync` proves a file *begins* with a recognized JPEG/PNG signature, not that the complete file is a valid, decodable image — a correct header followed by truncated or random bytes still passes. Full decode-based validation (reject anything that doesn't actually decode, require positive width/height) was evaluated and is intentionally not implemented: the project references no image-decoding library (`TrailGuard.csproj` carries only Identity/EFCore/Npgsql packages) and no platform image API is already in use anywhere in the app. `System.Drawing.Common` was considered and rejected — it needs its own NuGet package the project doesn't reference, is Windows-only since .NET 6 without an unsupported runtime switch, and this project's deployment target (Aiven PostgreSQL/cloud) isn't guaranteed to be Windows. Adding a decoding dependency (e.g. `SixLabors.ImageSharp`) needs explicit approval before it can close this gap — don't add one without it, and don't claim corrupt-image rejection is implemented until one is.

**Safe replacement order.** `UpdateProfile` writes the new file and resolves the previous file's path *before* calling `_userManager.UpdateAsync` — it does not delete the previous file first. If `UpdateAsync` fails, only the newly-written file (this request's own) is removed; the previous photo (both its stored reference and its file) is untouched, since the database row still points at it. The previous file is deleted only *after* `UpdateAsync` succeeds. A cleanup failure at that point (the old file, post-success) is logged via `ILogger<SettingsController>` and swallowed — it never reverts the already-successful profile update or surfaces a raw filesystem path to the participant. Every deletion target — new-file cleanup on failure, old-file cleanup on success — is re-verified to resolve inside `wwwroot/images/profiles` (`SettingsController.ResolveOwnedProfileImagePath`/`TryDeleteOwnedProfileImage`) before any `File.Delete` call, even though `ProfilePictureUrl` is always server-generated and never taken directly from posted data — defense-in-depth against a legacy or manually edited stored value. There is no shared/default avatar file to accidentally target: an absent picture renders as CSS-drawn initials in the view, never a file path.

**Email/username.** `user.Email` and `user.UserName` are always set together (no verification workflow). Identity's `UserManager.UpdateAsync` enforces username uniqueness by default regardless of `RequireUniqueEmail` (which `Program.cs` leaves at its default `false`) — since `UserName` always equals `Email`, attempting to change to another account's email fails via a `DuplicateUserName` `IdentityResult` error, surfaced through the same generic `TempData["Error"]` path as any other `UpdateAsync` failure. Normalization (`NormalizedEmail`/`NormalizedUserName`) is handled internally by `UpdateAsync` — not something Settings needs to do itself.

**Password change** uses `UserManager.ChangePasswordAsync` (verifies the current password and enforces the configured password policy in one call) and calls `SignInManager.RefreshSignInAsync` on success, matching the security-stamp-refresh behavior documented under Account Roles. Password values are never logged, put in TempData, or reflected back into a rendered field.

**Account Information** (Role / Date Joined / Account Status) is entirely read-only and server-derived: Role comes from `OperationalRolePolicy.Evaluate` (the same centralized classification Admin > Account Management uses), Date Joined from `user.DateCreated`, Account Status from `user.IsActive`. There is no editable control, hidden field, or route anywhere on this page that can change any of the three.

**Not fixed, by design:** the shared `UpdateProfileViewModel` (rather than two purpose-built models) is why the `ModelState.Remove` scoping above is needed at all. Splitting it would be a larger structural change than the confirmed defects required; overposting risk from the shared model is not currently exploitable since neither action ever reads the other form's fields for its own purpose.

---

## Security

Participant endpoints that take a registration ID **must** verify ownership before acting. `CancelRegistration`, `UpdatePaymentReceipt`, and `GetRegistrationDetails` all previously acted on any ID — any participant could cancel or attach a receipt to someone else's registration by incrementing a number.

Return the **same generic message** whether the ID doesn't exist or belongs to someone else. Distinguishing them lets an attacker enumerate valid IDs.

The same pattern was found and fixed in `ParticipantController.Feedback` and `SubmitFeedback`: both acted on any `eventId` without checking whether the caller had actually joined the event or whether the event had happened. The ML retraining set was never exposed — `FinalLabelService` refuses any registration that isn't `Accepted` — but `EventFeedbacks` is what the organizer reads for trail condition, hazards, and organizer ratings, and anyone could write to it.

**Remaining controllers have not been audited for this pattern.**

### Antiforgery

**Corrected claim:** this file previously stated that `SubmitFeedback` carries `[ValidateAntiForgeryToken]`. It does not — checked directly against `ParticipantController.cs`, which has no antiforgery attribute anywhere in the file.

There is **no global antiforgery filter** — `Program.cs` registers a bare `AddControllersWithViews()`. As of this check, `[ValidateAntiForgeryToken]` exists on exactly six actions, across three controllers: `AdminController` (3, including the `ChangeRole` role-management endpoint), `SettingsController` (2), `TrailController` (1). Every POST action in `AssessmentController`, `EventController`, `OrganizerController`, `ParticipantController` (including `SubmitFeedback`), and `RegistrationController` is unprotected. Several views still render `@Html.AntiForgeryToken()` into forms whose actions never validate it, which looks like protection and isn't.

**Assume nothing is protected unless you check the controller directly** — the specific list above is a snapshot, not a guarantee it's still current by the time you read this.

---

## Conventions

- Tailwind classes only — no custom CSS without a documented reason
- Use surface theme tokens, never raw hex (see `DESIGN.md`)
- Strongly typed ViewModels where one exists; `ViewBag` otherwise
- `DateTime.Now` throughout — `Program.cs` sets `Npgsql.EnableLegacyTimestampBehavior`
- Keep C# feature mappings (`AssessmentController.BuildMlRequest`, `Services/DifficultyCalculator.cs`) synchronised with the Python model (`generate_synthetic_dataset.py` / `acsm_gate.py`)
- Never display legacy rule-based category scores beside ML results
- Never treat an ML prediction as an automatic organizer decision
- Never reintroduce a rule-based fallback for a failed ML call — see "ML Failure — No Fallback"
- Preserve ownership checks when touching registration endpoints
- Scrollable `wwwroot/js/custom-select.js` listbox menus must use the shared, component-scoped `.tg-custom-select-scrollbar` class (`wwwroot/css/input.css`) — never a page-local scrollbar recipe (see DESIGN.md, Form Inputs, for the full rule)

---

## Current State

The pre-capstone App Dev feature set (result-based registration workflow, event completion with persisted final labels, organizer decision reason, notes & reminders, weather risk level as a separate field, three-section feedback) is complete and working end-to-end, along with trail/event CRUD, organizer approve/reject, alternative event recommendation, and post-event assessment. This is no longer usefully described as a gap list — see "Known Cleanup / Outstanding Work" below for what's actually unresolved.

Since then, the ML pipeline has been migrated to v2 (ACSM gate, NPS-based difficulty, Trail Class), the rule-based fallback has been removed, and the Reports aggregate-validation page has been added.

### UI/UX pass

**Done — participant side complete:**
landing, login, register, participant dashboard, browse trails, browse events, event detail, my registrations, assessment form, assessment report, registration form, Settings.

`Views/Participant/Events.cshtml` (Browse Events) has been visually aligned to the Event Management card shell (`Views/Event/Index.cshtml`), which itself follows the Trail Management card shell: same `group`/`bg-white/5`/`backdrop-blur-xl`/`rounded-xl` card, `h-48` image with `group-hover:scale-105` zoom (`motion-reduce:*` respected) and the same broken/missing-image fallback (`handleEventImageError`, mirrored inline on this page rather than extracted — every other card page keeps its own copy of the same small handler), the same `-mt-10` overlapping content composition, and `py-2.5` action buttons. Participant-specific content is preserved on top of that shell: Difficulty stays the top-left image badge (not Event Status, since Browse Events only ever lists Upcoming events), Trail name gets its own compact accent-icon row, and the Register/Upload Payment CTA is `RegistrationButtonHelper`'s solid `bg-accent` treatment (the same non-gradient Participant primary-action recipe used by `Participant/Details.cshtml`), not the brand gradient. `trailFilter`/`difficultyFilter`/`sortFilter` are the shared portal-enabled `data-custom-select` component (`wwwroot/js/custom-select.js`); the difficulty options come from `DifficultyCalculator.Bands` rather than a hardcoded, PM-range-suffixed list. Filtering/sorting stays fully client-side and instant (no query-string round trip) — see Known Cleanup re: the still-dead `searchString`/`difficulty`/`trailFilter`/`sortOrder` query parameters `ParticipantController.Events` accepts.

`Views/Participant/Trails.cshtml` (Browse Trails) received the same kind of pass: cards gained `group`/`group-hover:scale-105` image zoom and a broken-thumbnail fallback (`handleTrailImageError`, mirroring `Views/Trail/Index.cshtml`'s own copy) alongside their already-approved content and instant client-side search/sort (unchanged: name+location search, the same eight sort values, no new filter was added — the task explicitly ruled that out). `sortOrder` is now the shared portal-enabled custom select, inheriting `.tg-custom-select-scrollbar` automatically. The bespoke Trail Details modal was replaced with a read-only Participant adaptation of the approved Trail Management View modal — see DESIGN.md, Modals, Participant Trail Details, for the full comparison. Two Participant-facing endpoints on `ParticipantController` back it: `GetTrailEvents` (pre-existing, now guards non-positive ids) and `GetTrailPhotos` (new — a narrow, read-only, Participant-authorized counterpart to the Admin/Organizer-only `TrailController.GetTrailPhotos`, returning photo URL only, no `TrailPhoto.Id`/uploader/file-path data). Both the Additional Photos gallery and the Upcoming Events list guard against a stale response from a previously opened trail populating a newly opened one (an incrementing request token plus `AbortController`), and the Upcoming Events renderer was rebuilt on safe DOM construction (`document.createElement`/`textContent`) — it previously interpolated organizer-entered event fields (title, date, time, difficulty) directly into an `innerHTML` template.

**Remaining:**
- Feedback page (functionally a wizard, not yet restyled)
- **All organizer pages** — dashboard, events, registrations, registration details, post-event assessment, event comparison
- **All admin pages** — dashboard, accounts, records
- **Reports** (aggregate model validation) — new page, not yet styled
- Shared: navbar partials, footer, error pages

Note: `Organizer/RegistrationDetails.cshtml` has a SHAP panel added during earlier feature work, but has **not** been through the UI pass, and (see Explainability above) is currently rendering four SHAP feature names incorrectly regardless of styling.

---

## Known Cleanup / Outstanding Work

- **Antiforgery** — `AutoValidateAntiforgeryTokenAttribute` is the correct end state but would break every `fetch()` POST that sends no token. Only 5 of the state-changing POST actions across the whole app currently carry `[ValidateAntiForgeryToken]`; get an up-to-date per-controller count before scoping this rather than trusting a number written here
- **`AssessmentResultViewModel`** (used by `Views/Registration/Register.cshtml`) still carries `FitnessScore`/`ExperienceScore`/`HealthScore`/`GearScore` fields. They're populated (by the still-running legacy `Compute*` methods) but not rendered anywhere — the model isn't clean even though the display already was
- **Ownership checks** — fixed in `RegistrationController` and `ParticipantController`'s feedback endpoints; the rest are unaudited (see Security)
- **Three-segment confidence donut** for the organizer view — needs the Python service to return all three class probabilities (only the winning class's confidence is stored today) plus a migration to persist them
- **Seed data** should be regenerated once the system is finalised; registration seeding is currently commented out
- **Expert validation** — a physician and a hiking expert have completed rounds 1 and 2 (100 profiles total), reporting a quadratic weighted kappa of 0.555. **`MODEL.md` and `MODEL_EXPLAINED_EN.md` do not yet reflect this** — both still describe the expert instrument as prepared but not yet returned ("Until that is returned, no claim about real-world accuracy is supportable"). Updating those two files with the actual result is outstanding, and until it's done, treat the model card's stated limitations as authoritative over this bullet, not the other way around
- **The joint-injury ACSM gate rule** has no clinical source (`PENDING EXPERT ELICITATION` in `generate_synthetic_dataset.py`), as do the readiness component weights, the capacity range, and the decision thresholds
- **Manuscript realignment** — the approved proposal specified Laravel/PHP/MySQL; the system is ASP.NET Core/C#/PostgreSQL. Chapter 3 needs updating, along with the documented age-range limitation and the v1→v2 model change
- **`ParticipantController.Events`'s `searchString`/`difficulty`/`trailFilter`/`sortOrder` query parameters and their `ViewData["Current*"]` entries are dead** — Browse Events filters entirely client-side and no caller (navbar, dashboard, Trails, event/assessment "back" links) passes any of these on the route, so the controller's server-side filter/sort logic and the corresponding `ViewData` are unreachable from the UI. Confirmed by search, not removed in the Event Management alignment pass (a narrower, in-scope fix instead: the malformed-`trailFilter` `int.Parse` that could throw on manual/malformed input was replaced with the `int.TryParse` convention `EventController.Index` already uses). A broader cleanup — deleting the dead parameters and `ViewData`, or wiring them up as real deep-link entry points — is future work, not done here

---

## Development Workflow

- Commit per logical phase, not grouped unrelated changes
- `dotnet build` after significant changes; `npm run build` after any new Tailwind class
- Run the ML service when testing the assessment flow. There is no fallback path to test anymore — if the ML service is down, the only correct behavior is the form rejecting the submission with an error, not a rule-based result
- Migrations:
  ```bash
  dotnet ef migrations add <Name>
  dotnet ef database update
  ```
- **Inspect the generated migration before applying** when it warns about data loss. EF has produced a wrong rename here before, matching columns by position rather than name
- For registration changes, test both participant ownership and organizer access
- For ML changes, verify the C# mapping (`AssessmentController.BuildMlRequest`, `DifficultyCalculator`) and the Python `FEATURE_COLUMNS`/`acsm_gate.py` contract stay in sync

### Working with the planning conversation

Plans are discussed and written into `PLAN.md`, then implemented from that file. If an instruction in the plan looks wrong — a wrong assumption about an API, a change that would break something outside the stated scope — say so rather than implementing it literally. That has caught real problems here more than once.
