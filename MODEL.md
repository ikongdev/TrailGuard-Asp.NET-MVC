# TrailGuard Suitability Model — Model Card

Documentation of the two model versions produced during development.
All figures below were measured on the held-out test split of the version's
own dataset, not estimated.

---

## Version summary

| | **v1-synthetic** | **v2-acsm** |
|---|---|---|
| Status | Superseded | Current |
| Dataset rows | 2,000 | 6,000 |
| Features | 27 | 14 |
| Test split | 400 | 1,200 |
| Algorithm | XGBoost, `multi:softprob` | XGBoost, `multi:softprob` |
| Monotonic constraints | None | Enforced on 13 of 14 features |
| Health handling | Additive score term (weight 0.10) | ACSM clearance gate |
| Trail demand | Z-scored difficulty gap | NPS Shenandoah rating; displayed as PinoyMountaineer-calibrated bands |
| Post-prediction safety check | None | ACSM gate, may only lower a label |

### Terrain class composition (v2, n = 6,000)

| Class | Share | Rows |
|---|---|---|
| 1 — Walking | 39.9% | 2,396 |
| 2 — Hiking | 36.0% | 2,159 |
| 3 — Scrambling | 17.9% | 1,075 |
| 4 — Simple Climbing | 6.2% | 370 |

Class 4 is genuinely rare among organized hiking events, so it's drawn at a
correspondingly low rate (`p=0.06` in `generate_synthetic_dataset.py`) rather
than being oversampled to match the other classes - 370 examples is enough
for the model to learn from without distorting the overall distribution the
model sees in production.

---

## Performance

Retrained to include Trail Class 4 (see "Terrain class composition" above);
figures below are from that retrain, not the original v2 build. Every v2
figure on this page - this table, the confusion matrix, the safety-critical-
errors table, the Confidence/Saturation/Calibration section, the Safety
behaviour table, and all four gate-override rows - has been re-measured
against the retrained model.

| Metric | v1 | v2 |
|---|---|---|
| Accuracy | 0.8050 | **0.9142** |
| Weighted F1 | 0.8100 | **0.9151** |
| Borderline — precision / recall | 0.68 / 0.74 | **0.83 / 0.92** |
| Good Match — precision / recall | 0.85 / 0.83 | **0.95 / 0.94** |
| Not Recommended — precision / recall | 0.90 / 0.84 | **0.97 / 0.87** |
| Fidelity to the rule engine | 80.50% | **95.22%** |

### Confusion matrices (rows = actual)

**v1** (n = 400)

| | Borderline | Good Match | Not Recommended |
|---|---|---|---|
| Borderline | 96 | 20 | 13 |
| Good Match | 24 | 114 | 0 |
| Not Recommended | 21 | 0 | 112 |

**v2** (n = 1,200)

| | Borderline | Good Match | Not Recommended |
|---|---|---|---|
| Borderline | 339 | 21 | 10 |
| Good Match | 29 | 462 | 0 |
| Not Recommended | 42 | 1 | 296 |

### Safety-critical errors, normalised per 1,000 test rows

| Error | v1 | v2 (model alone) | v2 (with gate) |
|---|---|---|---|
| Not Recommended shown as Good Match | **0.00** | 0.83 | **0.00** |
| Borderline shown as Good Match | 50.00 | **17.50** | 15.83 |

**v1 is better than v2 on one metric.** The v1 model never predicted
Good Match for a participant the rules called Not Recommended; the v2 model
does so once in 1,200. This is a real regression in the model taken alone,
and it is why the ACSM gate is applied as an independent post-prediction
check rather than trusted to the model. Across the full 6,000-row dataset
the v2 model alone produces 3 such cases; with the gate applied, 0.

---

## Safety behaviour

| Test | v1 | v2 |
|---|---|---|
| Label changes when all health conditions are declared | 1.40% | **76.60%** |
| Label changes when known CVD is declared | not separable | **62.93%** |
| Label changes when age is set to 18 vs 59 | 2.00% | feature removed |
| Label changes when all gear is removed | 17.70% | 5.45% |
| Participants shown a feature that never affected the label | 47.40% | **0.00%** |
| Share of SHAP magnitude held by such features | 5.20% | **0.00%** |
| Monotonicity violations | 0 (unconstrained, incidental) | PASS WITH NOTE (1 of 6,000, 0.017%) |
| Gate override rate | n/a | 0.20% (confirmed unchanged post-retrain) |
| Label changes: same trail, Class 1 vs Class 4 | not measured | **38.17%** |

In v1, health conditions influenced the outcome less than age, which was
never used to generate a label. Gear influenced it roughly twelve times more
than all four health conditions combined. In v2 the ordering is inverted:
health is the strongest signal in the system.

Trail class alone had never been isolated as its own sensitivity check before
- earlier rows vary a participant's own inputs, not the trail. Holding a
participant's fitness, experience, gear, and health flags fixed and moving
only the trail from Class 1 (Walking) to Class 4 (Simple Climbing) changes
the label 38.17% of the time. That's a large effect for a single feature, and
expected given the multiplier now spans 1.00 to 1.60: at fixed participant
readiness, that alone can push a borderline case across a decision boundary.

---

## Confidence

Confidence is the maximum class probability, displayed raw. An earlier
version capped it at 99.9% in six locations; the cap concealed a measured
characteristic of the model and has been removed.

Recomputed against the Trail Class 4 retrain (see "Performance" above); the
figures below were previously measured on the pre-Class-4 model. Before ->
after for each:

- Predictions above 99%: 27.3% -> **24.0%**
- Predictions above 99.9%: 11.2% -> **8.0%**
- Predictions rounding to exactly 100.0%: 7.8% -> **5.6%**
- Mean confidence: 87.0% -> **84.5%**
- Median confidence: 92.3% -> **89.1%**
- Calibration, Below 70%: 195 cases / 64.1% agreement -> **245 cases / 68.6%**
- Calibration, 70–90%: 335 cases / 91.3% agreement -> **376 cases / 93.9%**
- Calibration, 90–99%: 342 cases / 100.0% agreement -> **291 cases / 99.0%**
- Calibration, Above 99%: 328 cases / 100.0% agreement -> **288 cases / 100.0%**

Saturation dropped across the board and the low-confidence band grew (195 ->
245 cases): Class 4 introduces more genuinely hard, high-demand cases near
the model's decision boundaries, so it has less to be near-certain about than
the 3-class model did. The 90–99% band's agreement also slipped from 100.0%
to 99.0% - a small number of cases now disagree with the rule engine at high
(but not saturated) confidence; not measured further here.

### Saturation (v2 test split, n = 1,200)

| | v1 | v2 |
|---|---|---|
| Predictions above 99% | 24.2% | 24.0% |
| Predictions above 99.9% | 9.2% | 8.0% |
| Predictions rounding to exactly 100.0% | — | 5.6% |
| Mean confidence | — | 84.5% |
| Median confidence | — | 89.1% |

Saturation is expected: the training labels are near-deterministic functions
of the features, so the model can reach near-certainty on clear-cut cases.
It is a property of learning from rule-generated labels, not a defect in the
classifier.

### Calibration (v2 test split)

| Confidence band | Cases | Agreement with the rule engine |
|---|---|---|
| Below 70% | 245 | 68.6% |
| 70–90% | 376 | 93.9% |
| 90–99% | 291 | 99.0% |
| Above 99% | 288 | 100.0% |

Confidence is informative, but about a narrower thing than it appears.
It predicts whether the model reproduces the rule engine — not whether the
recommendation is correct for a real hiker. A prediction at 99% confidence
is one the model is near-certain matches our rule; whether that rule is
right is the question the expert validation exists to answer.

This distinction matters for what the interface may claim. Text shown
alongside a confidence figure must not describe it as a measure of the
recommendation's real-world accuracy.

---

### Gate override rate by rule (full dataset, n = 6,000)

This table measures the gate as applied at **training-label generation
time** in `generate_synthetic_dataset.py`'s `build()` - i.e. against the
deterministic score-based label (`label_before_gate`, the demand/capacity
ratio comparison), not a trained model's live prediction. Reproduced by
`evaluate_gate_breakdown.py`, which reconstructs the same rule sequence
`acsm_gate.apply_acsm_gate` runs and cross-checks itself against the
dataset's own `suitability_label`/`gate_reason` columns (100.00% match on
both the before and after runs below).

| Column | Meaning |
|---|---|
| Cases matched | Rows the rule's own condition applies to |
| Gate lowered | Of those, rows where this rule actually changed the label (`gate_reason` recorded for it) |
| Already at ceiling | Of those, rows the score-based path had already placed at or below the rule's cap, independently |

"Already at ceiling" is informative on its own - it's how often the plain
demand/capacity comparison reaches the same conclusion as the ACSM safety
rule without needing it.

**A previous version of this table conflated two different things.** The
original pre-retrain figures (410 / 202 / 22 / 25) were each "Gate lowered"
counts, not "Cases matched" counts. When this table was first updated for
the Trail Class 4 retrain, the new "Cases matched" numbers (493 / 289 / 42 /
217) were reported against those old "Gate lowered" numbers as if they were
the same metric - e.g. "410 → 493" was presented as if the case count for
Signs/symptoms had grown by 20%, when 410 was never a case count in the
first place. All four rows are restated below using the current
methodology on **both** sides of the comparison, so before and after are
finally measuring the same thing.

| Rule | Cases matched (before → after) | Gate lowered (before → after) | Already at ceiling (before → after) |
|---|---|---|---|
| Signs or symptoms present | 493 → 493 | 410 → 392 | 83 → 101 |
| Known CVD, physically inactive | 289 → 289 | 202 → 189 | 87 → 100 |
| Known CVD, vigorous-intensity trail | 41 → 42 | 22 → 20 | 19 → 22 |
| Joint or knee injury on steep or technical terrain | 189 → 218 | 25 → 30 | 164 → 188 |

The joint-injury rule is the only one without a clinical source. It is
marked `PENDING EXPERT ELICITATION` in `generate_synthetic_dataset.py`.

**Signs or symptoms present** and **known CVD, physically inactive** have
identical "Cases matched" before and after (493 and 289), confirming they
really are terrain-independent - both trigger purely on health flags
(`has_cvd_symptoms`, `has_cvd` + inactivity), computed before trail terrain
is even assigned in `generate_participants()`. Yet "Already at ceiling" rose
for both (83→101, 87→100) and "Gate lowered" fell by the same amount. The
cause is not these rules themselves: the higher, data-fitted terrain
multipliers (1.00/1.15/1.35/1.60) raise `demand` on average across the whole
dataset, which lowers the plain demand/capacity ratio-based label on its own
for more participants - independent of which specific ACSM rule they'd also
trip. More rows arrive at these rules already at "Not Recommended" from the
score alone, so there's less left for the rule itself to lower.

**Known CVD, vigorous-intensity trail** and **joint/knee injury** both gained
a few cases (41→42, 189→218) because their own trigger conditions include a
`demand` threshold directly (`vigorous = demand >= 100`; `demand >= 150 OR
terrain >= 3`) - so, unlike the first two rules, some of their case-count
growth is a direct, mechanical consequence of the higher multipliers. For
joint injury, the 189→218 increase (+29) splits roughly in half: holding the
rule at `==3` and only changing the dataset (higher multipliers, Class 4
present) accounts for 189→206 (+17); widening `==3` to `>=3` on top of that
accounts for the remaining 206→218 (+12). Neither change dominates the
other.

---

## Features

### Removed from v1 (13)

| Feature | Reason |
|---|---|
| `age` | Never used to generate labels. Not among the three factors of the 2015 ACSM algorithm. |
| `exercise_type_category` | Never used to generate labels. |
| `height_cm`, `weight_kg` | Redundant with `bmi`; contributed only through it. |
| 8 individual gear flags | Redundant with `gear_score`; splitting SHAP credit across nine collinear features understated gear's influence. |
| `trail_distance_km`, `trail_elevation_gain_m`, `trail_estimated_duration_hr` | Replaced by `trail_shenandoah_score`, which the model previously had to approximate from them. |

### Added in v2 (2)

| Feature | Reason |
|---|---|
| `exercise_consistency_score` | Required by the ACSM criterion of at least 3 months at the stated frequency. |
| `trail_shenandoah_score` | The NPS difficulty rating, supplied directly instead of approximated. Principal cause of the accuracy gain. |

### Renamed in v2 (2)

| v1 | v2 | Change |
|---|---|---|
| `has_hypertension_heart_condition` | `has_cvd` | Naming only. |
| `has_vertigo` | `has_cvd_symptoms` | Now covers all three screened ACSM signs and symptoms: dizziness, chest pain, shortness of breath. |

### Final v2 feature list (14)

`bmi`, `exercise_frequency_score`, `continuous_cardio_duration_score`,
`exercise_consistency_score`, `hiking_experience_score`,
`last_hike_recency_score`, `hardest_trail_completed_score`, `gear_score`,
`has_asthma`, `has_cvd`, `has_joint_knee_injury`, `has_cvd_symptoms`,
`trail_shenandoah_score`, `trail_terrain_type`

The eight individual gear items remain in the `Assessment` table and drive
the Recommendations panel. They are a checklist, not a model input.

---

## Difficulty: PinoyMountaineer tiers over the NPS rating

`trail_shenandoah_score` (the NPS Shenandoah formula) is the only difficulty
input to the model itself. The band shown to users - on the event, trail,
report, and organizer pages, and as `SuitabilityResult.NpsBand` /
`PredictionResponse.nps_band` - is a separate, deterministic step applied on
top of it: the NPS rating is multiplied by the trail's TrailClass multiplier
(see limitation 4) to get an adjusted rating, which is then mapped onto one
of four PinoyMountaineer-derived tiers.

| Adjusted rating | Band | PinoyMountaineer level |
|---|---|---|
| < 81 | Easy | 1–2/9 |
| 81–354 | Minor Climb | 3–4/9 |
| 354–411 | Major Climb | 5–6/9 |
| ≥ 411 | Major Climb — Difficult | 7–9/9 |

These boundaries replace the published NPS bands (50/100/150/200), which are
calibrated for Shenandoah National Park - 68% of the 28 Philippine mountains
used to fit the boundaries above exceed the NPS bands' top threshold, so the
NPS bands alone cannot distinguish, e.g., Mt. Amuyao from Mt. Halcon.

**This is not the PinoyMountaineer scale.** The system computes the NPS
Shenandoah rating and maps it onto PinoyMountaineer difficulty tiers using
boundaries calibrated on 28 Philippine mountains, achieving 82% exact-tier
agreement and 100% agreement within one tier (Spearman rho 0.859). Applying
PM's own written rule (duration + trail class) directly against the same 28
mountains reproduced their published ratings only 50% of the time, because
multi-day status in the Philippines often reflects logistics rather than
difficulty - Mt. Pulag via Ambangeg is two days because you camp for the
sunrise, and is rated PM 3/9. See limitation 8 below for the caveat that the
82%/100% figures were measured on the same sample the boundaries were fitted
to.

---

## Expert validation

Two blind rating rounds were completed with a practising hiking organizer.
In each round the organizer received participant profiles and trail
specifications with no system output attached, and classified each as Good
Match, Borderline, or Not Recommended. The system's labels were withheld
until the sheet was returned.

| | Round 1 | Round 2 | Combined |
|---|---|---|---|
| Profiles | 60 | 40 | 100 |
| Exact agreement | 55.0% | 57.5% | 56.0% |
| Within one category | 88.3% | 97.5% | 92.0% |
| Quadratic weighted kappa | 0.495 | 0.655 | **0.555** |
| Disagreements two categories apart | 7 | 1 | 8 |

The categories are ordinal, so the weighted figure is reported. An
unweighted kappa cannot distinguish a Good Match / Borderline disagreement
from a Good Match / Not Recommended one.

**The two rounds do not show improvement.** The model configuration was
identical for both; the round 2 profiles were held back from the same
dataset before round 1 was sent. The difference between 0.495 and 0.655 is
sampling variation at n = 40 to 60, not the effect of any change. The
combined figure is the one to cite.

### Where the system and the organizer disagree

**The system over-uses Borderline.** Across all 100 profiles the organizer
chose Borderline 13 times; the system chose it 35 times. This replicated
independently in both rounds — 17% and 8% of the organizer's ratings
against 35% of the system's. The largest cells in the combined confusion
matrix are organizer Not Recommended against system Borderline (16 cases)
and organizer Good Match against system Borderline (13 cases). The
organizer commits; the system hedges.

The likely cause is structural rather than a model error. Two parameters
widen the Borderline band:

- The decision thresholds are 0.75 and 1.30, a span of 0.55 in
  demand-to-capacity ratio.
- `CAPACITY_MAX` is 230, so a participant scoring the theoretical maximum
  readiness of 1.0 has a capacity of 230. Six of eight seeded events exceed
  a demand of 172, the point beyond which no participant can reach Good
  Match at all.

Both are marked `PENDING EXPERT ELICITATION` in the generator.

**Direction of disagreement.** In round 1 the system was harsher than the
organizer on 17 profiles and more lenient on 10. In round 2 it was balanced
— harsher on 8, more lenient on 9.

**Medical clearance.** The organizer required clearance in 9 cases the
system did not flag, across both rounds, with no cases in the opposite
direction. All six round 1 misses involved a declared joint or knee injury.
The system's Rule 5 applies only when the trail is steep or technical and
does not require clearance; the organizer treats a knee injury as more
serious than that regardless of trail difficulty. Rule 5 is the only gate
rule with no clinical source and is marked `PENDING EXPERT ELICITATION`.

### Limitations of this validation

- **One rater.** These figures measure agreement with a single practising
  organizer, not agreement with the profession. They are calibration
  evidence, not independent statistical validation.
- **Synthetic profiles.** The organizer judged generated participants
  against real trail specifications, not people they have taken up a
  mountain.
- **No correction was tested.** No parameters were adjusted between rounds,
  so neither round measures whether a change improves agreement. A third
  round is planned after expert elicitation, using hiker–trail scenarios
  rather than individual profiles.

---

## Known limitations

1. **Labels remain synthetic.** Both versions learn from labels produced by
   a rule the development team wrote. v2 improves the sourcing and the
   structure of that rule; it does not remove the circularity. See item 2
   for how far expert validation has gone toward closing that gap.

2. **Expert validation is preliminary, not independent statistical
   validation.** Two blind rating rounds with a single practising hiking
   organizer (100 synthetic profiles total) produced a combined quadratic
   weighted kappa of 0.555 against the system's labels — see "Expert
   validation" above for the full breakdown, including where the system and
   the organizer disagree. This measures calibration against one rater's
   judgment of generated profiles: no parameters were adjusted between the
   two rounds, so agreement was measured, not improved, and no real hikers
   were involved. A claim of real-world accuracy still requires validation
   beyond this.

3. **Several constants are unsourced.** The readiness component weights, the
   capacity range, the decision thresholds, and the joint-injury rule are
   marked `PENDING EXPERT ELICITATION` in `generate_synthetic_dataset.py`.
   They are placeholders. (The terrain multiplier itself is no longer one of
   these - see item 4.)

4. **The terrain multiplier is a local adaptation, now data-fitted.** The NPS
   Shenandoah formula has no terrain term. TERRAIN_MULTIPLIER (Walking 1.00,
   Hiking 1.15, Scrambling 1.35, Simple Climbing 1.60) is our addition for
   Philippine trails and is not part of the cited NPS source; it was fitted
   against 28 Philippine mountains with published PinoyMountaineer difficulty
   ratings (Spearman rho 0.859 between the resulting adjusted rating and the
   published PM rating). It replaces an earlier, un-sourced 1.00/1.10/1.25
   guess. See item 8 for the calibration caveat that applies to this fit.

5. **Training is not bit-reproducible.** XGBoost histogram construction is
   multi-threaded, so retraining on a different machine yields small
   variation (observed: accuracy identical at 0.9175, rule fidelity 95.07%
   to 95.12%, monotonicity violations 0 to 1 in 6,000, predictions rounding
   to 100.0% between 7.83% and 7.92%). Set `n_jobs=1` for exact
   reproducibility. On the current Trail Class 4 retrain, `evaluate_safety.py`
   measured 1 violation in 6,000 (0.017%) on `exercise_frequency_score`, all
   other features at exactly 0. This is the same class of artifact: monotonic
   constraints are enforced per-class logit, and argmax across three
   separately-constrained logits can flip a near-tie even when each logit
   individually respects its constraint. `evaluate_safety.py`'s Test 3 reports
   this as **PASS WITH NOTE** rather than a bare pass/fail - a worst-feature
   rate of exactly 0% is PASS, above 0% but below 0.1% is PASS WITH NOTE
   (naming the feature and rate), and 0.1% or above is FAIL. The 0.1%
   threshold is a judgment call, not a sourced figure: chosen to separate a
   single-row argmax artifact from a violation large enough to indicate the
   constraint isn't holding.

6. **Scope is limited to ages 18–60.** This is a scope decision, not a model
   limitation — `age` is not a model input. Participants over 60 are the
   group most likely to require clearance under ACSM and are currently
   excluded from the system entirely.

7. **Self-report is unverified.** Every health and fitness input is taken at
   face value. The system has no way to detect a participant who understates
   a condition to gain acceptance.

8. **The PinoyMountaineer difficulty band boundaries are fitted and tested on
   the same 28-mountain sample.** The reported 82% exact-tier agreement and
   100% agreement-within-one-tier (Spearman rho 0.859) describe how well the
   boundaries reproduce the sample they were calibrated on, not performance
   on a held-out sample of Philippine mountains. This is the same caveat as
   item 1 applied to a second, smaller calibration - independent validation
   against mountains outside the 28 is required before the agreement figures
   can be cited as generalization performance. The system computes the NPS
   Shenandoah rating and maps it onto PinoyMountaineer difficulty tiers using
   these boundaries; it does not implement the PinoyMountaineer scale itself.
   Applying PM's own written rule (duration + trail class) against the same
   28 mountains reproduced their published ratings only 50% of the time,
   because multi-day status in the Philippines often reflects logistics
   (e.g. Mt. Pulag via Ambangeg is two days because you camp for the
   sunrise, and is rated PM 3/9) rather than difficulty.

---

## References

- Riebe D, Franklin BA, Thompson PD, Garber CE, Whitfield GP, Magal M,
  Pescatello LS. *Updating ACSM's Recommendations for Exercise
  Preparticipation Health Screening.* Medicine & Science in Sports &
  Exercise. 2015;47(11):2473–2479.
- National Park Service, Shenandoah National Park. *How to Determine Hiking
  Difficulty.*
- World Health Organization. *BMI classification for adults.*
- PinoyMountaineer. Trail Class scale and published difficulty ratings
  (1–9) for 28 Philippine mountains, used to calibrate the terrain
  multiplier and the difficulty band boundaries.
