# PLAN.md — v3 Model Migration

**Status:** Active
**Supersedes:** specific rules in `CLAUDE.md` and `AGENTS.md`, named per stage below
**Scope:** migrating the suitability model from `v2-acsm` (synthetic, rule-derived labels) to `v3-real-outcomes` (real completion outcomes)

---

## How to use this document

This plan has six stages. **Each stage is a separate implementation prompt.** Do not
implement more than one stage in a single pass, and do not start a later stage because
an earlier one touched an adjacent file.

Stages 1–3 have no effect on the running v2 system. Stage 4 is the cutover itself.
Stages 5–6 follow it.

Where this document and `CLAUDE.md` conflict, **this document wins for the items it
names, and only those.** Every other rule in `CLAUDE.md` still applies in full —
particularly the security, authorization, snapshot-immutability, and design-system
rules, none of which this migration changes.

`CLAUDE.md` currently describes the **running v2 system** and stays accurate until
each stage lands. It is updated stage by stage, never in advance.

---

## Background: why v3 exists

v2 trained on 6,000 synthetic rows whose labels were produced by a weighted formula
written by the development team:

```
readiness = 0.45*experience + 0.30*fitness + 0.15*gear + 0.10*bmi
capacity  = 60 + readiness * (230 - 60)
demand    = shenandoah_rating * terrain_multiplier
ratio     = demand / capacity   ->  thresholded into three labels
```

The model then learned to reproduce that formula. Its reported 91.42% accuracy
measures **fidelity to the team's own rule**, not correspondence to anything that
happened on a real hike. This is recorded in `MODEL.md` as v2's known limitation #1
and is the reason for the rebuild.

v3 trains on 1,200 real participant–trail records supplied by a partner hiking
agency, using the observed binary outcome — did this participant complete this hike —
as the training label. The agency confirmed directly that these are drawn from their
actual past events.

**v3 measured results** (see `trailguard-ml-v2/model_metadata.json`):

| | v2-acsm | v3-real-outcomes |
|---|---|---|
| Ground truth | team-authored formula | observed hike completion |
| Dataset | 6,000 synthetic | 1,200 real records, 300 participants, 12 trails |
| Features | 14 | 16 |
| Objective | `multi:softprob`, 3 classes | `binary:logistic` |
| Validation | single 80/20 split | 5-fold GroupKFold by participant |
| Accuracy | 91.42% (vs. its own rule) | **93.33% ± 2.24%** (vs. real outcomes) |
| Thresholds | 0.75 / 1.30, `PENDING EXPERT ELICITATION` | 0.30 / 0.80, sensitivity-derived |
| Borderline share | 35% | 19.4% |

**The two accuracy figures are not comparable.** Do not present the migration as a
two-point accuracy improvement. v2's figure could not measure real-world correctness
at all; v3's can.

---

## Stage 1 — Trail duration

**No effect on the running v2 system. Safe to land independently.**

### Goal

Add a manually entered `TypicalDurationHours` to `Trail`, captured into the Event
snapshot, so v3 has a 16th feature to read at prediction time.

### Why

v3 uses `typical_duration_hours` as a model feature. The system has no source for it
today: v2 derives duration from the NPS pace formula (`DifficultyCalculator.PaceMph`),
and that derivation does not match what the model was trained on. Measured against
the agency's own recorded durations:

| Trail | NPS-derived | Agency-recorded | Difference |
|---|---|---|---|
| Mt. Pulag (Ambangeg) | 7.59 h | 5.47 h | −2.12 h |
| Mt. Tapulao | 15.08 h | 12.25 h | −2.83 h |
| Mt. Makiling | 9.00 h | 6.45 h | −2.55 h |

Sending a computed duration where the model expects an observed one is the same
training-serving skew the project already avoids for weather. The organizer who has
walked the trail repeatedly knows things the formula cannot infer — technical ground,
rest stops, how an organized group actually moves — and that is precisely the
information this feature carries.

### Scope

- `Models/Trail.cs` — add `TypicalDurationHours` (decimal, required, > 0)
- `Models/Event.cs` — add `TrailDurationHoursSnapshot`
- `Services/EventTrailSnapshotHelper.cs` — capture the new field in `CaptureSnapshot`,
  alongside every other snapshot field, in the same single write
- EF Core migration adding both columns. Backfill existing Events from their currently
  linked Trail, matching the pattern already used by `AddEventTrailSnapshot`
- Add/Edit Trail UI — numeric input, validated server-side as well as client-side
- Trail Management and View Trail modal — display the value
- `Data/DbSeeder.cs` — set it for seeded trails

### Out of scope

- Do not remove or alter `DifficultyCalculator.PaceMph`. v2 still uses it and Stage 4
  has not run yet.
- Do not send the new field to the ML service. v2's contract is unchanged.

### Reference values

The agency's recorded durations for the twelve trails in the training set:

| Trail | Distance (km) | Elevation (m) | Class | Duration (h) |
|---|---|---|---|---|
| Mt. Pulag (Ambangeg) | 14.65 | 830 | 2 | 5.47 |
| Mt. Pinatubo (crater) | 11.27 | 625 | 1 | 4.17 |
| Mt. Tapulao (Dampay) | 29.13 | 2036 | 3 | 12.25 |
| Mt. Talamitam | 7.08 | 471 | 3 | 2.90 |
| Mt. Makiling (UPLB) | 17.38 | 971 | 3 | 6.45 |
| Mt. Ayaas (Mascap) | 10.78 | 617 | 3 | 4.05 |
| Mt. Pamitinan (Wawa) | 3.06 | 308 | 4 | 1.62 |
| Mt. Hapunang Banoi (Wawa) | 3.54 | 431 | 4 | 2.15 |
| Mt. Manalmon (Madlum) | 3.70 | 150 | 2 | 1.17 |
| Mt. Kitanglad (Intavas) | 16.09 | 1551 | 4 | 8.27 |
| Mt. Dulang-Dulang (Bol-ogan) | 16.09 | 1543 | 4 | 8.23 |
| Mt. Tagapo (Janosa) | 5.47 | 402 | 2 | 2.37 |

Use these where a seeded trail matches. Any other existing trail needs a value from
the organizer before Stage 4 can run.

### Acceptance criteria

- A Trail cannot be saved without a valid duration
- A newly created Event captures the duration into its snapshot
- Editing a Trail does not change any existing Event's snapshot
- Every existing Event has a non-null `TrailDurationHoursSnapshot` after migration
- `dotnet build` passes; the migration applies cleanly

### Follow-up left open by this stage

The twelve seeded trails carry `Terrain` = "Not recorded" and a placeholder
description, because `PLAN.md` supplies trail class but no descriptive text and
nothing should be invented from the numeric class. Both are display-only and do not
reach the model. **Fill them in through Edit Trail before any demo** — twelve blank
trail cards read as unfinished work.

### Conflicts with existing documentation

None. This is additive.

---

## Stage 2 — ACSM medical clearance moves to C#

**No effect on the running v2 system. Safe to land independently.**

### Goal

Implement the medical clearance decision as a C# service that does not depend on the
ML service, and that never modifies a suitability label.

### Why

In v2, `acsm_gate.apply_acsm_gate()` does two jobs at once: it sets
`medical_clearance_required`, **and** it caps the model's suitability label downward.
These answer different questions and are separated in v3.

- The model answers: *how likely is this participant to complete this trail?*
- The ACSM check answers: *does this participant's declared health history require a
  physician's clearance before exercise of this intensity?*

`Good Match + Clearance Required` is not a contradiction. It means the participant is
likely capable but must produce documentation before acceptance.

Two further reasons for the move:

1. **Medical clearance is a registration requirement, not a prediction.** If the ML
   service is unreachable, the organizer should still see that a participant needs
   clearance. Under `ML Failure — No Fallback` no suitability result is produced in
   that case, and the clearance flag should not disappear with it.
2. **The capping behaviour conflicts with the system's own stated principle.**
   `DESIGN.md` holds that the organizer decides and that a result must not imply
   automatic approval or rejection. A gate that rewrites the model's output before the
   organizer sees it is in tension with that.

### The rule

```
clearance_required = has_signs_symptoms OR has_cvd
```

Derivation, for the manuscript and for the code comment:

- **ACSM Rule 1** — signs or symptoms suggestive of cardiovascular disease require
  clearance regardless of intensity or activity level.
- **ACSM Rule 2** — known CVD and physically inactive: clearance before exercise of
  any intensity.
- **ACSM Rule 3** — known CVD and physically active: clearance before *vigorous*
  intensity. Organized mountain hiking is 6.0–7.0 METs (Compendium of Physical
  Activities: *hiking, cross country* 6.0; *climbing hills, 0–9 lb load* 7.0;
  *backpacking* 7.0), against the ACSM/CDC vigorous threshold of 6.0 METs. Every trail
  in this system's scope is therefore vigorous-intensity, so Rule 3 always fires for a
  participant with known CVD, and Rules 2 and 3 collapse into one condition.
- **ACSM Rule 4** — pulmonary disease is not an automatic referral. Asthma produces an
  advisory, never a clearance requirement.
- **Rule 5 (joint/knee injury)** — **not** a clearance trigger, matching v2. Recorded
  dissent: v2's expert validation found the organizer requiring clearance in nine
  cases the system missed, all six round-1 misses involving a declared knee injury.
  The rule has no ACSM basis either way. Revisit at expert elicitation.

An earlier proposal measured intensity by ascent rate (elevation ÷ duration) with a
threshold of 250 m/h. It was **rejected on evidence**: all twelve trails fall between
128 and 200 m/h, so no trail would ever qualify, and lowering the threshold to make
the rule fire would be exactly the unsourced constant-fitting v3 exists to remove.

### Scope

- New `Services/AcsmClearanceService.cs` (or equivalent), with the derivation above in
  a comment
- Wire it into the assessment flow so `Assessment.MedicalClearanceRequired` is set
  from it
- **Reconcile with the existing rule.** `CLAUDE.md`'s Registration Domain Rules
  already contain a *Requirements by result* table:

  | Result | Medical clearance | Preparation plan |
  |---|---|---|
  | Good-Match / Borderline, no conditions | Optional | Not required |
  | Good-Match / Borderline, with conditions | Required | Not required |
  | Not Recommended | Required | Required |

  Two mechanisms currently determine clearance. Before adding a third, establish which
  is authoritative, whether they can disagree, and what "with conditions" means
  relative to the ACSM rule. **Report the finding rather than guessing** — this is a
  safety-relevant conflict and falls under `CLAUDE.md`'s instruction not to silently
  resolve one.

### Out of scope

- Do not remove `acsm_gate.py` or stop v2 from applying its gate. v2 is still serving.
- Do not change any suitability label.

### Acceptance criteria

- Clearance is determined without calling the ML service
- A participant with `has_cvd` or signs/symptoms is flagged; asthma alone and
  joint/knee injury alone are not
- The relationship between this rule and the *Requirements by result* table is
  documented and reported

### Conflicts with existing documentation

- `CLAUDE.md` → *Non-negotiables*: "The ACSM gate can only lower a model label, never
  raise one." This stays true for v2 and stops being true at Stage 4. Do not change
  the wording in this stage.

---

## Stage 3 — Post-event outcome collection

**No effect on the running v2 system's predictions. Changes what is recorded afterwards.**

### Goal

Replace `FinalSuitabilityLabel`'s three-category label with a binary completion
outcome plus a reason, so that retraining data matches what v3 actually learns from.

### Why

`FinalSuitabilityLabel` exists to persist empirical outcomes for retraining. It
currently stores `Good-Match / Borderline / Not Recommended`, taking the more
conservative of the participant's feedback and the organizer's assessment.

v3 does not learn three categories. It learns **binary completion**, and the three
displayed categories are derived afterwards by thresholding a probability. Asking an
organizer to choose between three labels no longer produces data the model can train
on.

The second change is more valuable. v3's largest remaining limitation is that its
`completed` label does not separate readiness-related non-completion from external
causes. The measured cost is roughly **7% irreducible error**, concentrated in a
minority of participants — cross-validation found 12 participants producing 22 of one
fold's errors, including one who did not finish a Class 1 trail despite reporting 10+
prior mountains. No feature explains those cases because the cause was never recorded.

Collecting `NonCompletionReason` from now on is the single highest-value improvement
available, and it costs nothing to gather while the flow is being changed anyway.

### New shape

| Field | Values |
|---|---|
| `AssessmentId` | unchanged — links the outcome back to its features |
| `Completed` | boolean |
| `NonCompletionReason` | `Readiness` / `External` / `Withdrawal` / `NotApplicable` |
| `DifficultyExperience` | unchanged — from the feedback wizard |
| `RecordedAt` | timestamp |

`NotApplicable` is used when `Completed` is true.

- **Readiness** — fitness, stamina, health, or ability related
- **External** — weather, an incident unrelated to fitness, an emergency, a cancelled
  or curtailed event
- **Withdrawal** — the participant chose not to continue for reasons unrelated to
  readiness

### Scope

- Rewrite `FinalSuitabilityLabel` and `FinalLabelService` around the new shape
- Add the completion question and reason to the post-event flows that already exist:
  the participant feedback wizard and the organizer's post-event assessment
- `Completed` needs a source: `Event.Status == "Completed"` and
  `EventRegistration.Status == "Accepted"` establish that the participant was expected
  to attend, but nothing currently records whether they finished
- EF Core migration
- Delete existing `FinalSuitabilityLabel` rows — they are development test data in a
  shape that cannot be converted, since the three-category label is not derivable from
  a binary outcome

### Out of scope

- Do not change `FinalLabelService.ComputeKappa`'s callers in this stage.
  `ReportsController` is Stage 5.
- Participant feedback remains single-submission and unrevisable. The organizer's side
  remains revisable. That asymmetry is deliberate and unchanged.

### Acceptance criteria

- A completed event with an accepted registration can record `Completed` and, when
  false, a reason
- The reason field is required when and only when `Completed` is false
- Existing eligibility rules for the feedback wizard are unchanged
- Migration applies cleanly against an empty table

### Conflicts with existing documentation

- `CLAUDE.md` → *Final suitability labels*: the "more conservative of the two labels"
  rule no longer applies. Superseded by this stage.

---

## Stage 4 — Cutover

**This is the stage that switches the running system from v2 to v3.**

### Preconditions

Do not begin until all three hold:

1. Stage 1 complete — every Trail has a duration, every Event has a duration snapshot
2. Stage 2 complete — clearance works without the ML service
3. Stage 3 complete — outcome collection matches what v3 trains on

### Goal

Point the application at the v3 service, update the cross-language contract on both
sides, and remove v2.

### The new contract

The v3 service is in `trailguard-ml-v2/` and exposes:

```
GET  /            health check
GET  /model-info  version, features, CV results, thresholds
POST /predict     label, probability, SHAP breakdown, recommendations, medical flags
```

**Request** — 16 fields. Categorical answers are sent as **raw text**, not encoded
integers:

```json
{
  "bmi": 25.9,
  "exercise_frequency": "3-4x",
  "cardio_duration": "15-29min",
  "exercise_consistency": "3+ months",
  "hiking_experience": "1-3",
  "last_hike_recency": "4-12 months ago",
  "hardest_trail_completed": "Minor day hikes",
  "gear_score": 5,
  "has_asthma": 0,
  "has_cvd": 0,
  "has_joint_knee_injury": 0,
  "has_signs_symptoms": 0,
  "distance_km": 16.09,
  "elevation_gain_m": 1543,
  "trail_class": 4,
  "typical_duration_hours": 8.23
}
```

Encoding moved to Python deliberately. v2 kept the same mapping in two languages and
relied on them being changed together. `encoding.py` is now the only copy. An
unrecognised value returns **422** with the expected values listed — it never silently
defaults, because a default here would make a sedentary participant look fit.

**The exact accepted strings** (from `encoding.py` — the C# side must match verbatim):

| Field | Accepted values |
|---|---|
| `exercise_frequency` | `Sedentary`, `1-2x`, `3-4x`, `5+ times per week` |
| `cardio_duration` | `<15min`, `15-29min`, `30-60min`, `>60min` |
| `exercise_consistency` | `<1 month`, `1-2 months`, `3+ months` |
| `hiking_experience` | `First-timer`, `1-3`, `4-10`, `10+ mountains` |
| `last_hike_recency` | `Never`, `>1yr ago`, `4-12 months ago`, `1-3 months ago` |
| `hardest_trail_completed` | `None`, `Minor day hikes`, `Major w/ steep sections`, `Multi-day` |

A first-timer sends `hardest_trail_completed: "None"`. The form's existing
first-timer dependency rule already enforces this.

**`typical_duration_hours` comes from `Event.TrailDurationHoursSnapshot`, never from
`Event.EstimatedDuration`.** The snapshot is the Trail's recorded duration frozen at
event creation; `EstimatedDuration` is an organizer-editable per-event plan that may
legitimately differ (a large group, a sunrise wait). The model learned trail duration
as a property of the trail, and the other three trail features already come from the
snapshot. Sourcing it from the editable field would also let an organizer move a
participant's suitability result by adjusting a number on the event form.

**Response**:

```json
{
  "suitability_label": "Borderline",
  "completion_probability": 0.5634,
  "model_version": "v3-real-outcomes",
  "thresholds": { "borderline": 0.3, "good_match": 0.8 },
  "shap_breakdown": [ { "feature": "...", "friendly_name": "...", "category": "...",
                        "raw_value": 0, "shap_value": 0.0, "share_pct": 0.0,
                        "direction": "Helped" } ],
  "recommendations": [ { "feature": "...", "friendly_name": "...", "raw_value": 0,
                         "shap_value": 0.0, "share_pct": 0.0 } ],
  "medical_flags": [ ]
}
```

Gone from the response: `model_label`, `gate_applied`, `gate_reason`, `nps_score`,
`nps_band`, `medical_clearance_required`, `confidence_score`.

`completion_probability` is deliberately **not** named `confidence_score`. They are
different quantities. v2's confidence measured how certain the model was that its
label matched its training rules. v3's is a calibrated probability that the
participant completes the hike, verified against out-of-fold predictions on all 1,200
rows. Reusing the old name would carry the old meaning across.

### SHAP display

Sixteen features in four categories, all set in `explainer.py`:

| Category | Features | Display rule |
|---|---|---|
| `actionable` | exercise_frequency, cardio_duration, exercise_consistency, gear_score | may become a recommendation |
| `context` | bmi, hiking_experience, last_hike_recency, hardest_trail_completed | shown in the explanation, never as advice |
| `medical` | has_asthma, has_cvd, has_joint_knee_injury, has_signs_symptoms | shown only when the participant has the condition; never a recommendation |
| `trail` | distance_km, elevation_gain_m, trail_class, typical_duration_hours | shown in the explanation, never as advice |

The three exclusions were added after inspecting real output, and none of them would
appear in any accuracy metric:

- With medical features actionable, a participant reporting chest pain was told to act
  on their symptoms — advice they cannot follow, and which teaches under-reporting a
  condition to raise a score
- With `hardest_trail_completed` and `hiking_experience` actionable, low scorers were
  told to complete a harder trail first — pushing toward a less suitable event, which
  the achievement system already forbids for the same reason
- With BMI actionable, the advice became about body weight, which is neither
  changeable in the run-up to an event nor safe to frame as a score problem
- A healthy participant saw `Cardiovascular signs or symptoms — Helped 14.4%`, which
  is correct SHAP but reads as though a condition they do not have influenced their
  result

### Scope

- `AssessmentController.BuildMlRequest` — 16 fields, raw text, duration from the Event
  snapshot. The existing `MapScore` dictionaries are no longer needed for the ML
  request.
- `Services/SuitabilityApiClient.cs` — new request/response shape
- `Services/ShapHelper.cs` — 16 feature names, the four categories, the medical
  display rule. Keep the existing throw-on-unknown-name behaviour.
- `SuitabilityResult` — `CompletionProbability` replaces `ConfidenceScore`; remove
  `GateApplied` and `GateReason`; SHAP storage carries `category`
- Delete existing `Assessment` and `SuitabilityResult` rows. v2 records cannot be
  converted: different features, different confidence semantics, and no duration value
  ever recorded.
- Remove the legacy `ComputeFitnessScore` / `ComputeExperienceScore` /
  `ComputeHealthScore` / `ComputeMlGearScore` methods and the
  `Assessment.FitnessScore` / `ExperienceScore` / `HealthScore` / `GearScore` /
  `TotalScore` fields. `CLAUDE.md` already lists these under Known Cleanup as
  unrendered and no longer feeding the ML request.
- Update the participant-facing confidence context line (see below)
- `appsettings.json` → `MlApi:BaseUrl` points at the v3 service
- Remove `TrailGuard-ML/` once v3 is verified running

### The confidence context line

v2's line reads:

> "Confidence reflects how certain the model is that this result matches its trained
> rules. It does not measure whether the recommendation is right for you."

That was accurate for v2 and is **wrong for v3**. v3's figure is a calibrated
probability of completion, measured out-of-fold across all 1,200 rows:

| Model says | Actually completed |
|---|---|
| 0–10% | 0.0% |
| 40–50% | 46.2% |
| 50–60% | 53.8% |
| 70–80% | 76.4% |
| 90–100% | 98.7% |

The replacement wording should say what the number now means — an estimated chance of
completing this trail, based on outcomes from past participants — without implying the
organizer's decision is settled by it. Draft the wording and **report it for review
rather than shipping it**; this line is manuscript-facing.

### Difficulty bands

`DifficultyCalculator.cs` keeps computing the NPS rating and PinoyMountaineer band for
display. v3 does not use either, so the C#/Python pairing that required both to be
changed together **no longer exists**. Update the comment in
`Services/DifficultyCalculator.cs` that currently describes that pairing — leaving it
would tell a future reader to keep a file in sync with one that has been deleted.

### Out of scope

- Do not reintroduce a rule-based fallback. If the v3 service is unreachable, the
  behaviour is unchanged: an error, no result, no save, and the participant can retry.
  That rule survives this migration intact.
- Do not change the `"Good Match"` / `"Good-Match"` normalization. `NormalizeLabel()`
  stays.

### Acceptance criteria

- A representative assessment reaches the v3 service and returns a result end to end
- A rejected categorical value surfaces as a handled error, not a crash
- The SHAP panel renders for all four categories; a healthy participant sees no
  medical rows
- `Good Match + Clearance Required` is reachable and visually unmissable
- No code path still references `TrailGuard-ML/`

---

## Stage 5 — Reports

### Goal

Rebuild `ReportsController` around what v3 can actually be validated on.

### Why

The current report measures label agreement — Cohen's kappa and weighted kappa between
the system's label and the resolved final label. That made sense when the final label
was also a three-category judgment. After Stage 3 it is a binary outcome, and the
questions worth asking change:

- **Live calibration** — of the cases predicted at 80%+, how many actually completed?
  This is the same measurement made offline during development, now against real use,
  and it is the strongest evidence the system can produce about itself.
- **Safety-critical errors** — how many `Good Match` predictions did not complete.
  Offline this was 4 in 1,200 at the chosen thresholds.
- **Threshold validation** — are 0.30 and 0.80 still the right operating points, or
  does live data suggest otherwise?
- **Non-completion reasons** — how much of the error is external rather than
  readiness-related. This directly sizes the 7% irreducible error against real data.

This is stronger than kappa, because the comparison is against what happened rather
than against an opinion.

### Scope

- Rewrite the accuracy breakdown, confusion matrix, and kappa sections
- Keep: Admin-only authorization, the sampling-bias funnel, breakdowns by difficulty
  band and Trail Class, CSV export, and the `Not Recommended` acknowledgement pathway
- Keep `MinSampleSize = 20` before reporting any derived statistic

### The retraining export

The CSV export exists to produce retraining data, so it must load into
`encoding.py` without manual editing. The exact contract:

**Columns, in this order**, matching `TrailGuard_Training_Data_Final.xlsx`:

```
case_id, participant_profile_id, trail_id,
bmi, exercise_frequency, cardio_duration, exercise_consistency,
hiking_experience, last_hike_recency, hardest_trail_completed, gear_score,
has_asthma, has_cvd, has_joint_knee_injury, has_signs_symptoms,
distance_km, elevation_gain_m, trail_class, typical_duration_hours,
completed
```

The first three are identifiers. `participant_profile_id` must be a **stable
per-participant identifier**, not a per-assessment one — `encoding.py`'s consumer
groups by it to prevent the group leakage described in the technical documentation,
where a row-level split put 167 of 168 test participants into training as well and
inflated accuracy from 93.33% to 97.50%. A per-row identifier would silently defeat
that protection. It must also not be the participant's real identity: hash or map it.

**Categorical values are exported as the exact strings `encoding.py` accepts**, not
as encoded integers — `"3-4x"`, `">60min"`, `"10+ mountains"`. The full list is in
Stage 4's request table. An unmatched string is rejected at import, which is the
intended behaviour.

Trail geometry comes from the **Event snapshot**, not the live Trail, for the same
reason it does at prediction time.

**Rows to exclude:**

- `NonCompletionReason` of `External` or `Withdrawal` — the cause was not readiness,
  so the row teaches the model nothing and adds the noise Stage 3 exists to remove
- Any assessment with no recorded outcome

**Rows to include:** completed hikes, and non-completions with reason `Readiness`.

Export the excluded count alongside the file. It is a useful figure in its own right
— it measures how much of the raw outcome data is non-readiness noise, which is the
quantity the technical documentation currently estimates at roughly 7% from
cross-validation rather than from observation.

### Out of scope

- Authorization, scoping, and export mechanics are unchanged

---

## Stage 6 — Documentation

### Goal

Make the repository's documentation describe the system that now exists.

### Scope

**`MODEL.md`** — replace with the v3 model card. Source material is in
`TrailGuard_Model_Documentation_Technical.pdf`. Keep v2's figures where they appear as
comparisons, clearly labelled as the superseded version.

**`CLAUDE.md`** — the following are no longer true and must be corrected:

| Section | Change |
|---|---|
| *Non-negotiables* → "The ACSM gate can only lower a model label" | Remove. The gate no longer touches the label. |
| *Non-negotiables* → "`BuildMlRequest` and Python `FEATURE_COLUMNS` are a cross-language contract" | Still true, but the contract is now 16 text/numeric fields |
| *Non-negotiables* → "Difficulty ordering/display use the terrain-adjusted rating, and the Python/C# implementations must be changed together" | The Python implementation is gone. Difficulty display is C#-only. |
| *Non-negotiables* → "Confidence displays the real predicted-class probability" | Now a completion probability; update the wording |
| *ML Pipeline* file table | Replace with the v3 file list |
| *Current model performance* | Replace with v3's figures, including the caveat that the two accuracy numbers measure different things |
| *Synthetic dataset basis* | Replace with the real-data description |
| *ML Labels and the ACSM Gate* | Rewrite as clearance-only |
| *Difficulty Bands* | Remove the "changed together" pairing |
| *Confidence Display* | New wording and new calibration figures |
| *Final suitability labels* | Binary outcome plus reason |

Rules that **survive unchanged**, and should not be touched: ML Failure — No Fallback;
the Decision-Making Rule; Event Trail Snapshot immutability; Weather excluded from ML
features; the security and authorization rules; the whole of `DESIGN.md`.

**`AGENTS.md`** — same corrections, matching whatever subset it mirrors.

**Known limitations to carry into `MODEL.md`**, stated plainly:

1. Non-completion reasons unseparated in the training data — ~7% irreducible error
   (Stage 3 begins fixing this for future retrains, not retrospectively)
2. Selection bias — only accepted, attending participants have outcomes; the model is
   calibrated on a borderline-to-fit population
3. The 300 profiles were quota-sampled, so category proportions describe the sampling
   design, not the agency's applicant pool
4. Thresholds are data-informed but not expert-confirmed
5. One agency, twelve trails
6. Self-report is unverified
7. No expert validation study has been run against v3
8. 63.1% of predictions fall in the extreme probability bands; whether this reflects
   decisive screening or a recording convention is unresolved

**What may be claimed:** trained and validated on real observed outcomes, 93.33% ±
2.24% on unseen participants under participant-level cross-validation, thresholds
derived from measured completion rates.

**What may not:** validated across agencies or regions, expert-confirmed, or free of
label noise.

---

## Addition for `CLAUDE.md` and `AGENTS.md` — add now, before Stage 1

Both files should carry this note for the duration of the migration, so that an agent
reading them knows a newer authority exists:

> ## Active migration
>
> A v3 model rebuild is underway. `PLAN.md` describes it and **supersedes specific
> rules in this document for the items it names, and only those.** Every other rule
> here still applies in full.
>
> This file describes the **running v2 system** and is updated stage by stage as each
> stage lands — never in advance. If a statement here contradicts `PLAN.md`, the
> statement is still true of what is running today; `PLAN.md` describes what replaces
> it.

---

## Working agreement

- One stage per implementation prompt
- Stages 1–3 may land in any order; Stage 4 requires all three
- Report conflicts rather than resolving them silently, especially in Stage 2, where
  two mechanisms currently determine medical clearance
- The handoff requirements in `CLAUDE.md` apply unchanged to every stage
