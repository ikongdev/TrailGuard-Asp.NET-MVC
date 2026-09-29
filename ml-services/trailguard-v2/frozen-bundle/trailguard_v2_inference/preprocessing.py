"""Shared, deliberately narrow preprocessing for training and inference."""

from __future__ import annotations

import hashlib
import json
from typing import Any

import numpy as np
import pandas as pd

from .constants import CATEGORY_MAPPINGS, MODEL_FEATURES, NUMERIC_FEATURES
from .numeric_validation import numeric_validation_errors


class FeatureSchemaError(ValueError):
    """Raised when an inference or model input does not exactly match the schema."""


def schema_payload() -> dict[str, Any]:
    return {
        "schema_version": 1,
        "feature_order": list(MODEL_FEATURES),
        "categorical_mappings": CATEGORY_MAPPINGS,
        "numeric_features": list(NUMERIC_FEATURES),
        "categorical_encoding": "explicit ordinal codes; no numeric estimates for category ranges",
    }


def schema_fingerprint(payload: dict[str, Any] | None = None) -> str:
    encoded = json.dumps(payload or schema_payload(), sort_keys=True, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def validate_saved_schema(payload: dict[str, Any]) -> None:
    expected = schema_payload()
    if payload != expected:
        raise FeatureSchemaError("Saved feature schema does not match the frozen TrailGuard v2 schema.")


def transform_features(frame: pd.DataFrame) -> np.ndarray:
    """Encode exactly the 11 permitted model features in frozen feature order."""
    actual = list(frame.columns)
    expected = list(MODEL_FEATURES)
    if actual != expected:
        raise FeatureSchemaError(
            "Model input must contain exactly the 11 approved features in this order: " + ", ".join(expected) + ". "
            "IDs, target, dataset_split, notes, and any other fields are rejected."
        )
    if frame.empty:
        raise FeatureSchemaError("Model input contains no rows.")
    output = pd.DataFrame(index=frame.index)
    for column, mapping in CATEGORY_MAPPINGS.items():
        values = frame[column]
        if values.isna().any() or (values.astype("string").str.strip() == "").any():
            raise FeatureSchemaError(f"Missing or blank categorical value in {column}.")
        unknown = sorted(set(values.astype(str)) - set(mapping))
        if unknown:
            raise FeatureSchemaError(f"Unsupported {column} category: {unknown}. Allowed: {list(mapping)}.")
        output[column] = values.astype(str).map(mapping)
    for column in NUMERIC_FEATURES:
        errors = numeric_validation_errors(frame[column], column)
        if errors:
            raise FeatureSchemaError(" ".join(errors))
        output[column] = frame[column]
    return output.loc[:, MODEL_FEATURES].to_numpy(dtype=np.float64)
