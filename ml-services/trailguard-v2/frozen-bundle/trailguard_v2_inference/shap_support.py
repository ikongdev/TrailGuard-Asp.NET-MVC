"""Native XGBoost TreeSHAP helpers constrained to the frozen selected trees."""

from __future__ import annotations

from dataclasses import dataclass

import numpy as np
import xgboost as xgb

from .constants import MODEL_FEATURES

EXPLANATION_TOLERANCE = 1e-4


@dataclass(frozen=True)
class NativeShapResult:
    probability_before: np.ndarray
    probability_after: np.ndarray
    model_margin: np.ndarray
    feature_contributions: np.ndarray
    base_values: np.ndarray


def sigmoid(raw_margin: np.ndarray) -> np.ndarray:
    """Stable logistic transform for binary:logistic raw margins."""
    raw_margin = np.asarray(raw_margin, dtype=np.float64)
    return np.where(raw_margin >= 0, 1.0 / (1.0 + np.exp(-raw_margin)), np.exp(raw_margin) / (1.0 + np.exp(raw_margin)))


def load_native_model(model_path: str) -> xgb.Booster:
    model = xgb.Booster()
    model.load_model(model_path)
    if model.num_features() != len(MODEL_FEATURES):
        raise ValueError(f"Frozen model has {model.num_features()} features; expected {len(MODEL_FEATURES)}.")
    return model


def native_tree_shap(model: xgb.Booster, encoded_features: np.ndarray, selected_tree_count: int) -> NativeShapResult:
    """Predict and explain only [0, selected_tree_count) with native pred_contribs."""
    if selected_tree_count != 985:
        raise ValueError("TrailGuard v2 explanations require exactly selected_tree_count=985.")
    matrix = xgb.DMatrix(encoded_features, feature_names=list(MODEL_FEATURES))
    tree_range = (0, selected_tree_count)
    probability_before = np.asarray(model.predict(matrix, iteration_range=tree_range), dtype=np.float64).reshape(-1)
    model_margin = np.asarray(model.predict(matrix, output_margin=True, iteration_range=tree_range), dtype=np.float64).reshape(-1)
    contributions = np.asarray(model.predict(matrix, pred_contribs=True, iteration_range=tree_range), dtype=np.float64)
    probability_after = np.asarray(model.predict(matrix, iteration_range=tree_range), dtype=np.float64).reshape(-1)
    expected_shape = (encoded_features.shape[0], len(MODEL_FEATURES) + 1)
    if contributions.shape != expected_shape:
        raise ValueError(f"Native contribution output has shape {contributions.shape}; expected {expected_shape}.")
    return NativeShapResult(
        probability_before=probability_before,
        probability_after=probability_after,
        model_margin=model_margin,
        feature_contributions=contributions[:, :-1],
        base_values=contributions[:, -1],
    )


def verify_native_tree_shap(result: NativeShapResult, tolerance: float = EXPLANATION_TOLERANCE) -> dict[str, float | bool]:
    """Fail closed unless native contributions exactly preserve frozen prediction behavior."""
    if tolerance <= 0:
        raise ValueError("Explanation tolerance must be positive.")
    values = {
        "probability_before": result.probability_before,
        "probability_after": result.probability_after,
        "model_margin": result.model_margin,
        "base_values": result.base_values,
        "feature_contributions": result.feature_contributions,
    }
    for name, array in values.items():
        if not np.isfinite(np.asarray(array, dtype=np.float64)).all():
            raise ValueError(f"Native TreeSHAP verification failed: non-finite {name}.")
    if np.any((result.probability_before < 0) | (result.probability_before > 1)):
        raise ValueError("Native TreeSHAP verification failed: probability_before is outside [0, 1].")
    if np.any((result.probability_after < 0) | (result.probability_after > 1)):
        raise ValueError("Native TreeSHAP verification failed: probability_after is outside [0, 1].")
    if not np.array_equal(result.probability_before, result.probability_after):
        change = float(np.max(np.abs(result.probability_before - result.probability_after)))
        raise ValueError(f"Native TreeSHAP verification failed: prediction changed after explanation (max change {change:.12g}).")
    additivity_errors = np.abs(result.base_values + result.feature_contributions.sum(axis=1) - result.model_margin)
    probability_errors = np.abs(sigmoid(result.model_margin) - result.probability_before)
    max_additivity_error = float(additivity_errors.max())
    max_probability_error = float(probability_errors.max())
    if max_additivity_error > tolerance:
        raise ValueError(
            "Native TreeSHAP verification failed: additivity error "
            f"{max_additivity_error:.12g} exceeds tolerance {tolerance:.12g}."
        )
    if max_probability_error > tolerance:
        raise ValueError(
            "Native TreeSHAP verification failed: probability reconstruction error "
            f"{max_probability_error:.12g} exceeds tolerance {tolerance:.12g}."
        )
    return {
        "tolerance_absolute": tolerance,
        "max_additivity_error": max_additivity_error,
        "max_probability_reconstruction_error": max_probability_error,
        "max_prediction_change_after_explanation": 0.0,
        "predictions_unchanged": True,
        "within_tolerance": True,
    }
