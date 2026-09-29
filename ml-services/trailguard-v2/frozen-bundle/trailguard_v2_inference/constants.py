"""Frozen dataset contract and model feature schema for TrailGuard v2."""

from __future__ import annotations

MODEL_FEATURES = (
    "exercise_frequency",
    "cardio_duration",
    "exercise_consistency",
    "hiking_experience",
    "hiking_recency",
    "hardest_trail_completed",
    "gear_score",
    "distance_km",
    "elevation_gain_m",
    "trail_class",
    "typical_duration_hours",
)

CATEGORICAL_FEATURES = MODEL_FEATURES[:6]
NUMERIC_FEATURES = MODEL_FEATURES[6:]
TARGET = "expert_expected_completion"
METADATA_COLUMNS = ("scenario_id", "participant_profile_id", "trail_id", "dataset_split")
REQUIRED_TRAINING_COLUMNS = METADATA_COLUMNS + MODEL_FEATURES + (TARGET,)

# The source documentation defines these ordered categories.  Values are codes,
# not estimates of the category ranges.
CATEGORY_MAPPINGS = {
    "exercise_frequency": {
        "No regular exercise": 0,
        "1–2": 1,
        "3–4": 2,
        "5+": 3,
    },
    "cardio_duration": {"None": 0, "<15": 1, "15–29": 2, "30–60": 3, ">60": 4},
    "exercise_consistency": {
        "None": 0,
        "<1 month": 1,
        "1–2 months": 2,
        "3+ months": 3,
    },
    "hiking_experience": {"0": 0, "1–3": 1, "4–10": 2, "11+": 3},
    "hiking_recency": {
        "Never": 0,
        ">12 months": 1,
        ">3–12 months": 2,
        "Within 3 months": 3,
    },
    "hardest_trail_completed": {
        "None": 0,
        "Class 1": 1,
        "Class 2": 2,
        "Class 3": 3,
        "Class 4": 4,
    },
}

EXPECTED_SPLITS = {
    "Train": {"rows": 840, "profiles": 210},
    "Validation": {"rows": 180, "profiles": 45},
    "Test": {"rows": 180, "profiles": 45},
}

NUMERIC_CONSTRAINTS = {
    "gear_score": {"integer": True, "minimum": 0, "maximum": 8},
    "distance_km": {"integer": False, "exclusive_minimum": 0, "maximum": 100},
    "elevation_gain_m": {"integer": True, "minimum": 0, "maximum": 10000},
    "trail_class": {"integer": True, "minimum": 1, "maximum": 4},
    "typical_duration_hours": {"integer": False, "exclusive_minimum": 0, "maximum": 24},
}

REFERENCE_TRAIL_COLUMNS = (
    "trail_id",
    "distance_km",
    "elevation_gain_m",
    "trail_class",
    "typical_duration_hours",
)
REFERENCE_PROFILE_COLUMNS = ("participant_profile_id",) + MODEL_FEATURES[:7]

RANDOM_SEED = 20260930
THRESHOLD = 0.5
MODEL_MANIFEST_NAME = "TrailGuard v2"

# Candidate sets are intentionally small and fixed. Validation log loss is the
# sole selection metric; each uses 1,000 possible boosting rounds and early stop.
CANDIDATE_CONFIGURATIONS = (
    {
        "name": "conservative_depth_3",
        "max_depth": 3,
        "learning_rate": 0.05,
        "min_child_weight": 2,
        "subsample": 0.90,
        "colsample_bytree": 0.90,
        "reg_lambda": 2.0,
        "n_estimators": 1000,
        "early_stopping_rounds": 50,
    },
    {
        "name": "balanced_depth_4",
        "max_depth": 4,
        "learning_rate": 0.05,
        "min_child_weight": 1,
        "subsample": 0.90,
        "colsample_bytree": 1.0,
        "reg_lambda": 1.0,
        "n_estimators": 1000,
        "early_stopping_rounds": 50,
    },
    {
        "name": "regularized_depth_2",
        "max_depth": 2,
        "learning_rate": 0.03,
        "min_child_weight": 3,
        "subsample": 1.0,
        "colsample_bytree": 1.0,
        "reg_lambda": 4.0,
        "n_estimators": 1000,
        "early_stopping_rounds": 75,
    },
)
