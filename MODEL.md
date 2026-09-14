# TrailGuard v3 Model Card

## Identity

| Item | Value |
|---|---|
| Version | `v3-real-outcomes` |
| Model | XGBoost binary classifier (`binary:logistic`) |
| Training data | 1,200 observed participant–trail outcomes from one partner agency |
| Coverage | 300 participants, 12 trails |
| Target | Completion (`Yes` / `No`) |
| Validation | Five-fold GroupKFold by stable opaque participant profile ID |

The 16 inputs are BMI; exercise frequency, cardio duration, consistency; hiking experience, recency, hardest trail; gear score; four declared medical flags; and distance, elevation, class, and recorded trail duration. `encoding.py` is the only categorical encoder.

## Performance and interpretation

v3 achieved **93.33% ± 2.24% accuracy** on unseen participants under participant-level cross-validation (folds: 92.92%, 95.83%, 92.92%, 89.58%, 95.42%). At 0.30/0.80 thresholds, out-of-fold labels were 391 Good Match, 233 Borderline, 576 Not Recommended; there were 4 unsafe Good Match predictions and 5 over-rejected completions.

The superseded v2-acsm reported 91.42% on 6,000 synthetic records. That measured agreement with a development-team formula. v3’s 93.33% ± 2.24% measures agreement with real observed hikes. **They measure different things and are not a two-point improvement.**

The model returns a calibrated completion probability, with Good Match ≥0.80, Borderline 0.30–<0.80, and Not Recommended <0.30. It supports the organizer’s decision; it never approves or rejects automatically.

## Explainability

SHAP factors are `actionable`, `context`, `medical`, or `trail`. The participant display contains the top five eligible factors and hides zero-valued medical factors. Recommendation selection scans all 16 but permits only negative actionable factors (exercise frequency, cardio duration, consistency, gear) with at least 2% of total absolute impact, capped at three. Medical, context, and trail factors never become recommendations.

ACSM screening is separate C# registration logic: declared signs/symptoms or known CVD require clearance. It does not change a v3 label; Not Recommended may separately require clearance under agency policy.

## Known limitations

1. Training labels did not separate readiness non-completion from External or Withdrawal causes. About 7% error is irreducible under this definition: a hypothetical perfect model scores around 93%, not 100%. New collection improves future retrains only.
2. Only accepted, attending participants have outcomes, creating selection bias toward a borderline-to-fit population.
3. The 300 profiles were quota-sampled; category proportions are sampling design, not the agency applicant pool.
4. Thresholds are data-informed, not expert-confirmed.
5. Data comes from one agency and twelve trails; no cross-agency or regional validation exists.
6. Fitness, health, experience, and gear are unverified self-report.
7. No expert validation study has assessed v3; prior synthetic-model expert work does not validate it.
8. 63.1% of predictions are in extreme probability bands; whether this is decisive screening or a recording convention is unresolved.

Claims allowed: trained and validated on real observed outcomes; 93.33% ± 2.24% on unseen participants under participant-level cross-validation; thresholds derived from measured completion rates. Claims not allowed: cross-agency/region validation, expert confirmation, or freedom from label noise.

## Retraining and retired v2

`train_model.py` keeps the committed XLSX as its default and accepts the retraining CSV contract. The export uses stable opaque participant IDs to prevent group leakage. The retired `TrailGuard-ML/` v2 service was removed in Stage 6 and can be recovered from Git commit `15c28a0`.
