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
| Trail demand | Z-scored difficulty gap | NPS Shenandoah rating and bands |
| Post-prediction safety check | None | ACSM gate, may only lower a label |

---

## Performance

| Metric | v1 | v2 |
|---|---|---|
| Accuracy | 0.8050 | **0.9175** |
| Weighted F1 | 0.8100 | **0.9178** |
| Borderline — precision / recall | 0.68 / 0.74 | **0.84 / 0.90** |
| Good Match — precision / recall | 0.85 / 0.83 | **0.95 / 0.96** |
| Not Recommended — precision / recall | 0.90 / 0.84 | **0.96 / 0.86** |
| Fidelity to the rule engine | 80.50% | **95.12%** |

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
| Borderline | 331 | 24 | 12 |
| Good Match | 20 | 508 | 0 |
| Not Recommended | 42 | 1 | 262 |

### Safety-critical errors, normalised per 1,000 test rows

| Error | v1 | v2 (model alone) | v2 (with gate) |
|---|---|---|---|
| Not Recommended shown as Good Match | **0.00** | 0.83 | **0.00** |
| Borderline shown as Good Match | 50.00 | **20.00** | 20.00 |

**v1 is better than v2 on one metric.** The v1 model never predicted
Good Match for a participant the rules called Not Recommended; the v2 model
does so once in 1,200. This is a real regression in the model taken alone,
and it is why the ACSM gate is applied as an independent post-prediction
check rather than trusted to the model. Across the full 6,000-row dataset
the v2 model alone produces 2 such cases; with the gate applied, 0.

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
| Monotonicity violations | 0 (unconstrained, incidental) | 0 (enforced) |
| Gate override rate | n/a | 0.20% |

In v1, health conditions influenced the outcome less than age, which was
never used to generate a label. Gear influenced it roughly twelve times more
than all four health conditions combined. In v2 the ordering is inverted:
health is the strongest signal in the system.

---

## Confidence

Confidence is the maximum class probability, displayed raw. An earlier
version capped it at 99.9% in six locations; the cap concealed a measured
characteristic of the model and has been removed.

### Saturation (v2 test split, n = 1,200)

| | v1 | v2 |
|---|---|---|
| Predictions above 99% | 24.2% | 27.3% |
| Predictions above 99.9% | 9.2% | 11.2% |
| Predictions rounding to exactly 100.0% | — | 7.8% |
| Mean confidence | — | 87.0% |
| Median confidence | — | 92.3% |

Saturation is expected: the training labels are near-deterministic functions
of the features, so the model can reach near-certainty on clear-cut cases.
It is a property of learning from rule-generated labels, not a defect in the
classifier.

### Calibration (v2 test split)

| Confidence band | Cases | Agreement with the rule engine |
|---|---|---|
| Below 70% | 195 | 64.1% |
| 70–90% | 335 | 91.3% |
| 90–99% | 342 | 100.0% |
| Above 99% | 328 | 100.0% |

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

| Rule | Cases | Model correct alone | Gate overrode |
|---|---|---|---|
| Signs or symptoms present | 410 | 100.0% | 0.0% |
| Known CVD, vigorous-intensity trail | 22 | 100.0% | 0.0% |
| Known CVD, physically inactive | 202 | 98.0% | 2.0% |
| Joint or knee injury on steep or technical terrain | 25 | 68.0% | **32.0%** |

The joint-injury rule is the only one without a clinical source and the only
one the model fails to learn reliably. It is marked
`PENDING EXPERT ELICITATION` in `generate_synthetic_dataset.py`.

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

## Known limitations

1. **Labels remain synthetic.** Both versions learn from labels produced by
   a rule the development team wrote. v2 improves the sourcing and the
   structure of that rule; it does not remove the circularity. Validation
   against an independent expert is required before any accuracy claim can
   be made about real-world suitability.

2. **Several constants are unsourced.** The readiness component weights, the
   terrain multipliers, the capacity range, the decision thresholds, and the
   joint-injury rule are marked `PENDING EXPERT ELICITATION` in
   `generate_synthetic_dataset.py`. They are placeholders.

3. **The terrain multiplier is a local adaptation.** The NPS Shenandoah
   formula has no terrain term. The multipliers applied for Moderate,
   Difficult, and Technical terrain are our addition for Philippine trails
   and are not part of the cited source.

4. **Training is not bit-reproducible.** XGBoost histogram construction is
   multi-threaded, so retraining on a different machine yields small
   variation (observed: accuracy identical at 0.9175, rule fidelity 95.07%
   to 95.12%, monotonicity violations 0 to 1 in 6,000, predictions rounding
   to 100.0% between 7.83% and 7.92%). Set `n_jobs=1` for exact
   reproducibility.

5. **Scope is limited to ages 18–60.** This is a scope decision, not a model
   limitation — `age` is not a model input. Participants over 60 are the
   group most likely to require clearance under ACSM and are currently
   excluded from the system entirely.

6. **Self-report is unverified.** Every health and fitness input is taken at
   face value. The system has no way to detect a participant who understates
   a condition to gain acceptance.

---

## References

- Riebe D, Franklin BA, Thompson PD, Garber CE, Whitfield GP, Magal M,
  Pescatello LS. *Updating ACSM's Recommendations for Exercise
  Preparticipation Health Screening.* Medicine & Science in Sports &
  Exercise. 2015;47(11):2473–2479.
- National Park Service, Shenandoah National Park. *How to Determine Hiking
  Difficulty.*
- World Health Organization. *BMI classification for adults.*
