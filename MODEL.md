# TrailGuard active model card

## Identity

| Field | Current record |
|---|---|
| Active model | trailguard-v2.0.0 |
| Serving component | ml-services/trailguard-v2 FastAPI adapter |
| Local endpoint | http://127.0.0.1:8011/predict |
| Model type | XGBoost binary logistic |
| Positive class | Yes (1) / probability_yes |
| Selected trees | 985 |
| Frozen binary threshold | 0.50 |
| Frozen SHA-256 | bdc93bff426fadbcab6d4ce16b4a457050a6875fb67c355df0da5101caeb4e35 |

This card describes the active frozen model only. Historical v1/v2/v3 services and their metrics are not evidence about this artifact.

## Intended use

The model provides one suitability-support score for a participant and a specific Event's captured trail characteristics. It is decision support, not automatic acceptance/rejection, medical advice, clearance, or a completion guarantee. The Organizer makes the final registration decision.

The adapter is the only active assessment mechanism. If it is unavailable, invalid, or fails integrity/explanation validation, the system shows no substitute score and saves no partial assessment.

## Frozen inputs and preprocessing

The exact ordered input contract is:

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

feature_schema.json stores explicit ordinal mappings for the six categorical fields; frozen constants hold numeric range/integer constraints. The adapter rejects unknown categories, nonfinite/non-numeric values, missing keys, and extra keys. MVC validates the same participant-facing contract first.

Trail inputs come from immutable Event snapshots. TrailDurationHoursSnapshot, not editable EstimatedDuration, is model input. Gear score derives from recognized gear selections. First-time hiking answers must be internally consistent.

BMI, height, weight, age/demographics, medical conditions, weather, payment history, and Organizer decisions are not active inputs. Some have separate application uses, including medical clearance/registration logic and weather advisory.

## Output and display policy

ModelScore stores probability_yes unchanged. The model's binary classification is Yes at score 0.50 or higher, otherwise No. The separate UI policy maps the same score:

| Score | UI label |
|---|---|
| Below 0.30 | Not Recommended |
| 0.30 through below 0.80 | Borderline |
| 0.80 or higher | Good Match |

These are bands over one binary-model score, not independently predicted three-class probabilities. The UI uses two decimal percentage places: nonzero values that round to zero display <0.01%; non-one values that round to 100 display >99.99%; exact endpoints display 0.00% and 100.00%.

Available active-model records do not establish calibration to real-world completion. Do not describe probability_yes as a calibrated completion probability, performance result, or guarantee.

## Explanations

The adapter uses XGBoost native TreeSHAP with Booster.predict(pred_contribs=True, iteration_range=(0, 985)). It returns a base value, raw margin, and one ordered contribution for each input on raw_margin_log_odds.

- Positive contributions push the binary model toward Yes; negative contributions push toward No.
- Contributions are not percentage-point changes, causal effects, prescriptions, or medical conclusions.
- The boundary validates finite values, schema order, unchanged prediction, TreeSHAP additivity, and sigmoid reconstruction within 1e-4.

Those checks establish explanation consistency with the frozen artifact, not accuracy, calibration, fairness, or causal validity. All factors display by absolute contribution; up to three suggestions may use only negative exercise frequency, cardio duration, exercise consistency, and gear-score factors.

## Independent safety boundaries

Medical clearance is separate application logic: signs/symptoms or known CVD trigger the C# rule. Medical inputs never enter /predict, and a model label cannot waive clearance. Weather and event difficulty are separate advisory/display systems, not model features.

## Artifact and dataset records

Frozen manifest metadata reports 1,200 scenarios and 300 profiles. It records 840 Train rows/210 profiles, 180 Validation rows/45 profiles, and 180 Test rows/45 profiles. These are metadata counts, not evidence of real participants, collection methods, label provenance, accuracy, calibration, or real-world performance.

training_configuration.json records Train-only fitting, Validation early stopping/configuration selection, and no Test access for fitting, selection, or threshold selection. bundle_verification.json records one source-vs-bundle output equality check. The TreeSHAP report records reconstruction/consistency checks and explicitly limits them to synthetic expert-labeled Train scenarios.

The statement “an empirical dataset provided by our partner hiking agency” is awaiting confirmation. Available frozen records do not name an agency or link an agency source to this model. Before using that wording, retain (1) agency-source documentation/data agreement or equivalent dated provenance, and (2) a reproducible training/run record connecting that dataset identity/SHA-256 to this selected model artifact/version. Do not overwrite conflicting artifacts.

## Evaluation and limitations

No active-model predictive metric, calibration analysis, monotonicity guarantee, external validation, or expert-validation result is documented in the reviewed frozen records. Do not import these claims from retired models. Available evidence is artifact integrity, one source-vs-bundle row match, and TreeSHAP reconstruction/consistency.

Participant-facing inputs are self-reported. The model is not medical clearance and cannot substitute for Organizer judgment.

## Maintenance

For active-contract work, inspect TrailGuardV2AssessmentRequestMapper, TrailGuardV2ApiClient, TrailGuardV2ResponseValidator, and the active bundle schema. Do not modify retired BuildMlRequest, historical encoding.py, or trailguard-ml-v2 as if they serve active inference.
