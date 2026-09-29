# TrailGuard v2 inference bundle with native TreeSHAP

This bundle is a frozen integration-review artifact. It contains the native XGBoost model, its schema/mappings, the selected training configuration, minimal shared preprocessing, and native XGBoost TreeSHAP explanations. It is not a training or evaluation package.

## Contract

- Native model: `selected_model.ubj`
- Positive class: Yes (`1`)
- Frozen tree count: `985`
- Frozen decision threshold: `0.5`
- Input: exactly the 11 feature keys in `feature_schema.json`; IDs, labels, metadata, and extra fields are rejected.
- Validation: categorical values must be in the saved mappings. Numeric values must be real, finite, non-boolean, satisfy declared bounds, and be whole numbers where required.

## Windows use

From the repository root with the reviewed virtual environment active:

```powershell
$env:PYTHONPATH = (Resolve-Path .\inference_bundle_with_shap).Path
.\.venv\Scripts\python.exe -m trailguard_v2_inference.predict --model-dir .\inference_bundle_with_shap --input-json .\inference_bundle_with_shap\example_input.json
```

```cmd
set PYTHONPATH=%CD%\inference_bundle_with_shap
.venv\Scripts\python.exe -m trailguard_v2_inference.predict --model-dir .\inference_bundle_with_shap --input-json .\inference_bundle_with_shap\example_input.json
```

The command emits `probability_yes`, `predicted_completion`, and the frozen threshold. It uses `iteration_range=(0, 985)` and the shared preprocessing implementation; it does not train, tune, or evaluate the Test partition.

## One-row native TreeSHAP explanation

```powershell
$env:PYTHONPATH = (Resolve-Path .\inference_bundle_with_shap).Path
.\.venv\Scripts\python.exe -m trailguard_v2_inference.explain --model-dir .\inference_bundle_with_shap --input-json .\inference_bundle_with_shap\example_input.json
```

```cmd
set PYTHONPATH=%CD%\inference_bundle_with_shap
.venv\Scripts\python.exe -m trailguard_v2_inference.explain --model-dir .\inference_bundle_with_shap --input-json .\inference_bundle_with_shap\example_input.json
```

The explanation response includes `probability_yes`, `predicted_completion`, `threshold`, `base_value`, `model_margin`, `explanation_scale`, and one ordered entry per frozen feature with its original input and SHAP contribution. `explanation_scale` is `raw_margin_log_odds`: positive contributions push the model toward Yes and negative contributions toward No. They are not percentage-point probability changes, causal effects, or medical conclusions.

Every explanation fails closed unless probabilities, margins, bias, and all contributions are finite; native additivity and sigmoid reconstruction stay within an absolute `1e-4` tolerance; and prediction before/after explanation is exactly unchanged.

The entry point calls XGBoost native `Booster.predict(pred_contribs=True, iteration_range=(0, 985))`. XGBoost documents that `pred_contribs` returns one value per feature plus a bias term whose sum equals the raw margin, and that `iteration_range` limits prediction to the selected tree range: https://xgboost.readthedocs.io/en/release_3.3.0/python/python_api.html

## Integrity

Install the exact reviewed runtime with `requirements.lock.txt`. Verify every bundle file before integration with the SHA-256 values in `SHA256SUMS.txt`. The original run outputs under `runs/training_v2` and `runs/final_test_v2` are not part of this bundle and must remain unchanged.
