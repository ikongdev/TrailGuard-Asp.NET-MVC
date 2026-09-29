"""Single-row inference example using the saved TrailGuard v2 preprocessing."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import pandas as pd
import xgboost as xgb

from .constants import MODEL_FEATURES
from .preprocessing import transform_features, validate_saved_schema


def main() -> int:
    parser = argparse.ArgumentParser(description="Score one approved-feature TrailGuard v2 input row.")
    parser.add_argument("--model-dir", type=Path, required=True)
    parser.add_argument("--input-json", type=Path, required=True)
    args = parser.parse_args()
    schema = json.loads((args.model_dir / "feature_schema.json").read_text(encoding="utf-8"))
    manifest = json.loads((args.model_dir / "manifest.json").read_text(encoding="utf-8"))
    training_configuration = json.loads((args.model_dir / "training_configuration.json").read_text(encoding="utf-8"))
    validate_saved_schema(schema)
    raw = json.loads(args.input_json.read_text(encoding="utf-8"))
    if not isinstance(raw, dict) or set(raw) != set(MODEL_FEATURES):
        raise ValueError("Input JSON must be one object with exactly the 11 approved feature keys and no metadata, target, or notes.")
    features = pd.DataFrame([[raw[column] for column in MODEL_FEATURES]], columns=MODEL_FEATURES)
    model = xgb.XGBClassifier()
    model.load_model(args.model_dir / manifest["model_file"])
    probability = float(model.predict_proba(transform_features(features), iteration_range=(0, training_configuration["selected_tree_count"]))[0, 1])
    print(json.dumps({"probability_yes": probability, "predicted_completion": int(probability >= training_configuration["threshold"]), "threshold": training_configuration["threshold"]}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
