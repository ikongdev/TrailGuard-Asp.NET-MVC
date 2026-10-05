# TrailGuard’s active suitability model, explained

## What runs today

TrailGuard runs trailguard-v2.0.0 through ml-services/trailguard-v2. MVC validates/maps answers using TrailGuardV2AssessmentRequestMapper, calls FastAPI through TrailGuardV2ApiClient, and validates model identity plus explanation consistency before saving a result.

Historical trailguard-ml-v2 and its 16-feature/v3 material are not deployed assessment code. They are Git-history references only.

## Inputs

The model accepts exactly 11 inputs: exercise frequency, cardio duration, exercise consistency, hiking experience, hiking recency, hardest trail completed, gear score, distance, elevation gain, technical trail class, and typical duration.

Six answer categories use frozen allowed values. Numeric values are range-validated. Gear score is derived from the recognized checklist. Trail-side values come from the Event's immutable trail snapshot, so an assessment reflects the trail information captured for that scheduled Event rather than a Trail that may later be edited.

BMI, height, weight, age/demographic fields, medical-condition flags, weather, payment information, and Organizer decisions are not model inputs. Medical answers support independent clearance/registration logic; weather is a separate advisory.

## Score and label

The model returns one binary-model probability_yes, stored unchanged as ModelScore. Its binary Yes/No threshold is 0.50. The display has separate interpretation bands:

| Score | Display |
|---|---|
| Below 0.30 | Not Recommended |
| 0.30 through below 0.80 | Borderline |
| 0.80 or higher | Good Match |

These are not three independently predicted class probabilities. The interface formats the one score to two decimal percentage places while retaining very small/large non-endpoint values as <0.01% and >99.99%.

The artifact records do not establish that the score is calibrated to real-world hike completion. Describe it as a model score or probability_yes, not as a verified likelihood or guarantee.

## Explanations

The adapter asks XGBoost for native TreeSHAP values using 985 selected trees. A base value plus one contribution per input reconstructs the raw model margin. The scale is raw-margin/log-odds: positive pushes toward Yes and negative pushes toward No. A contribution is not a percentage-point change, causal finding, medical conclusion, or instruction.

TrailGuard rejects an explanation unless feature order remains frozen, values are finite, the prediction is unchanged, contributions reconstruct the margin, and the margin's sigmoid reconstructs the score within 1e-4. This establishes internal explanation consistency, not predictive accuracy, calibration, fairness, or causality.

Factors display by absolute contribution. Suggestions are intentionally narrow: at most three negative contributions from exercise frequency, cardio duration, exercise consistency, and gear score. Trail properties are explained, never turned into participant suggestions.

## Safety around the model

Medical clearance remains independent C# screening. Signs/symptoms or known CVD trigger the clearance rule; a Good Match cannot waive it. The Organizer makes the final registration decision.

If the adapter is unavailable, slow, invalid, or fails validation, TrailGuard does not fall back to a rules score and does not save a partial assessment. The participant can retry later. Weather and route-effort difficulty are separate systems, not features or evidence for the model.

## Evidence available

Frozen metadata reports 1,200 scenarios and 300 profiles: 840 Train rows/210 profiles, 180 Validation rows/45 profiles, and 180 Test rows/45 profiles. It records Train-only fitting and no Test use for fitting, selection, or threshold selection.

These counts are artifact metadata, not proof of 1,200 observed participants or an evaluation result. Retained checks demonstrate bundle integrity, one source-vs-bundle prediction match, and TreeSHAP reconstruction consistency. They do not provide active-model accuracy, calibration, monotonicity, agency validation, or expert-validation evidence.

The wording “an empirical dataset provided by our partner hiking agency” is awaiting confirmation for this exact frozen model. It needs both agency-source documentation and a dated training/run record linking that source/dataset checksum to this selected artifact. Historical v3 wording is insufficient.

## Next evidence needed

Before stronger claims, retain an approved active-model evaluation report with held-out methodology, metrics, calibration evidence, subgroup/safety analysis, and provenance. Privacy contact and retention details also await approved project content. Do not retrofit historical v3 claims onto this model.
