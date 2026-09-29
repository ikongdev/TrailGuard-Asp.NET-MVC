"""Return a native TreeSHAP explanation for one frozen TrailGuard v2 prediction."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any

import pandas as pd

from .constants import MODEL_FEATURES
from .preprocessing import transform_features, validate_saved_schema
from .shap_support import load_native_model, native_tree_shap, verify_native_tree_shap


def _read_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def explain_row(model_dir: Path, raw_input: dict[str, Any]) -> dict[str, Any]:
    """Explain one input without training, tuning, or accessing any dataset partition."""
    schema = _read_json(model_dir / "feature_schema.json")
    configuration = _read_json(model_dir / "training_configuration.json")
    manifest = _read_json(model_dir / "manifest.json")
    validate_saved_schema(schema)
    if configuration.get("selected_tree_count") != 985 or configuration.get("threshold") != 0.5:
        raise ValueError("Frozen TrailGuard v2 explanation contract requires 985 trees and a 0.5 threshold.")
    if not isinstance(raw_input, dict) or set(raw_input) != set(MODEL_FEATURES):
        raise ValueError("Input JSON must contain exactly the 11 frozen model features and no metadata, target, or notes.")
    frame = pd.DataFrame([[raw_input[name] for name in MODEL_FEATURES]], columns=MODEL_FEATURES)
    encoded = transform_features(frame)
    model = load_native_model(str(model_dir / manifest["model_file"]))
    result = native_tree_shap(model, encoded, configuration["selected_tree_count"])
    verification = verify_native_tree_shap(result)
    base_value = float(result.base_values[0])
    contributions = result.feature_contributions[0]
    margin = float(result.model_margin[0])
    probability_yes = float(result.probability_before[0])
    return {
        "probability_yes": probability_yes,
        "predicted_completion": int(probability_yes >= configuration["threshold"]),
        "threshold": configuration["threshold"],
        "selected_tree_count": configuration["selected_tree_count"],
        "base_value": base_value,
        "model_margin": margin,
        "explanation_scale": "raw_margin_log_odds",
        "contribution_interpretation": "Positive values push the model toward Yes; negative values push toward No. Contributions are log-odds changes, not percentage-point probability changes, causal effects, or medical conclusions.",
        "features": [
            {
                "feature": name,
                "original_input_value": raw_input[name],
                "shap_contribution": float(contribution),
            }
            for name, contribution in zip(MODEL_FEATURES, contributions, strict=True)
        ],
        "verification": {
            "additivity_error": verification["max_additivity_error"],
            "probability_reconstruction_error": verification["max_probability_reconstruction_error"],
            "prediction_change_after_explanation": verification["max_prediction_change_after_explanation"],
            "tolerance_absolute": verification["tolerance_absolute"],
            "predictions_unchanged": verification["predictions_unchanged"],
            "within_tolerance": verification["within_tolerance"],
            "feature_order_matches_frozen_schema": list(MODEL_FEATURES) == schema["feature_order"],
        },
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Explain one frozen TrailGuard v2 inference with native XGBoost TreeSHAP.")
    parser.add_argument("--model-dir", type=Path, default=Path("."))
    parser.add_argument("--input-json", type=Path, required=True)
    parser.add_argument("--output", type=Path, help="Optional new JSON output path; existing files are never overwritten.")
    args = parser.parse_args()
    payload = explain_row(args.model_dir, _read_json(args.input_json))
    rendered = json.dumps(payload, ensure_ascii=False, indent=2)
    print(rendered)
    if args.output:
        if args.output.exists():
            raise FileExistsError(f"Refusing to overwrite existing explanation: {args.output}")
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(rendered + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
