import os
import pandas as pd

EXERCISE_FREQUENCY = {
    "Sedentary": 0,
    "1-2x": 1,
    "3-4x": 2,
    "5+ times per week": 3,
}

CARDIO_DURATION = {
    "<15min": 0,
    "15-29min": 1,
    "30-60min": 2,
    ">60min": 3,
}

EXERCISE_CONSISTENCY = {
    "<1 month": 0,
    "1-2 months": 1,
    "3+ months": 2,
}

HIKING_EXPERIENCE = {
    "First-timer": 0,
    "1-3": 1,
    "4-10": 2,
    "10+ mountains": 3,
}

LAST_HIKE_RECENCY = {
    "Never": 0,
    ">1yr ago": 1,
    "4-12 months ago": 2,
    "1-3 months ago": 3,
}

HARDEST_TRAIL_COMPLETED = {
    "None": 0,
    "Minor day hikes": 1,
    "Major w/ steep sections": 2,
    "Multi-day": 3,
}

COMPLETED = {
    "No": 0,
    "Yes": 1,
}

FEATURE_COLUMNS = [
    "bmi", "exercise_frequency", "cardio_duration", "exercise_consistency",
    "hiking_experience", "last_hike_recency", "hardest_trail_completed",
    "gear_score", "has_asthma", "has_cvd", "has_joint_knee_injury",
    "has_signs_symptoms", "distance_km", "elevation_gain_m",
    "trail_class", "typical_duration_hours",
]


def load_and_encode(path, sheet_name="Training_Data"):
    extension = os.path.splitext(path)[1].lower()
    if extension == ".csv":
        df = pd.read_csv(path)
    elif extension in {".xlsx", ".xls"}:
        df = pd.read_excel(path, sheet_name=sheet_name)
    else:
        raise ValueError(f"Unsupported training-data format: {extension}")

    required = {"case_id", "participant_profile_id", "trail_id", *FEATURE_COLUMNS, "completed"}
    missing = required - set(df.columns)
    if missing:
        raise ValueError(f"Training data is missing required columns: {sorted(missing)}")

    df["hardest_trail_completed"] = df["hardest_trail_completed"].fillna("None")

    df["exercise_frequency"] = df["exercise_frequency"].map(EXERCISE_FREQUENCY)
    df["cardio_duration"] = df["cardio_duration"].map(CARDIO_DURATION)
    df["exercise_consistency"] = df["exercise_consistency"].map(EXERCISE_CONSISTENCY)
    df["hiking_experience"] = df["hiking_experience"].map(HIKING_EXPERIENCE)
    df["last_hike_recency"] = df["last_hike_recency"].map(LAST_HIKE_RECENCY)
    df["hardest_trail_completed"] = df["hardest_trail_completed"].map(HARDEST_TRAIL_COMPLETED)
    df["completed"] = df["completed"].map(COMPLETED)

    if df[list(CATEGORICAL_MAPS)].isna().any().any() or df["completed"].isna().any():
        raise UnknownCategoryError("Training data contains an unrecognised categorical or completed value.")
    return df

CATEGORICAL_MAPS = {
    "exercise_frequency": EXERCISE_FREQUENCY,
    "cardio_duration": CARDIO_DURATION,
    "exercise_consistency": EXERCISE_CONSISTENCY,
    "hiking_experience": HIKING_EXPERIENCE,
    "last_hike_recency": LAST_HIKE_RECENCY,
    "hardest_trail_completed": HARDEST_TRAIL_COMPLETED,
}


class UnknownCategoryError(ValueError):
    pass


def encode_value(field, value):
    mapping = CATEGORICAL_MAPS[field]
    if value not in mapping:
        raise UnknownCategoryError(
            f"Unrecognised value for {field}: {value!r}. "
            f"Expected one of: {sorted(mapping)}"
        )
    return mapping[value]


def encode_request(payload):
    encoded = dict(payload)
    for field in CATEGORICAL_MAPS:
        encoded[field] = encode_value(field, payload[field])
    return [encoded[col] for col in FEATURE_COLUMNS]
