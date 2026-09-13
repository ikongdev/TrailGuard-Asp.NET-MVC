import json
import logging

import numpy as np
import xgboost as xgb
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel

from encoding import FEATURE_COLUMNS, UnknownCategoryError, encode_request
from explainer import build_explainer, explain_one

logging.basicConfig(level=logging.INFO,
                    format="%(asctime)s %(levelname)s %(name)s: %(message)s")
logger = logging.getLogger("trailguard.v3")

MODEL_PATH = "trailguard_model.json"
METADATA_PATH = "model_metadata.json"

app = FastAPI(title="TrailGuard Suitability Prediction API (v3)")

model = xgb.XGBClassifier()
model.load_model(MODEL_PATH)

with open(METADATA_PATH) as f:
    METADATA = json.load(f)

MODEL_VERSION = METADATA["model_version"]
BORDERLINE_THRESHOLD = METADATA["thresholds"]["borderline"]
GOOD_MATCH_THRESHOLD = METADATA["thresholds"]["good_match"]

if METADATA["feature_columns"] != FEATURE_COLUMNS:
    raise RuntimeError(
        "Feature contract mismatch between model_metadata.json and encoding.py. "
        "The saved model was trained on a different feature set."
    )

explainer = build_explainer(model)


class PredictionRequest(BaseModel):
    bmi: float
    exercise_frequency: str
    cardio_duration: str
    exercise_consistency: str
    hiking_experience: str
    last_hike_recency: str
    hardest_trail_completed: str
    gear_score: int
    has_asthma: int
    has_cvd: int
    has_joint_knee_injury: int
    has_signs_symptoms: int
    distance_km: float
    elevation_gain_m: float
    trail_class: int
    typical_duration_hours: float


class ShapFactor(BaseModel):
    feature: str
    friendly_name: str
    category: str
    raw_value: float
    shap_value: float
    share_pct: float
    direction: str


class RecommendationTarget(BaseModel):
    feature: str
    friendly_name: str
    raw_value: float
    shap_value: float
    share_pct: float


class PredictionResponse(BaseModel):
    suitability_label: str
    completion_probability: float
    model_version: str
    thresholds: dict
    shap_breakdown: list[ShapFactor]
    recommendations: list[RecommendationTarget]
    medical_flags: list[ShapFactor]


class ModelInfoResponse(BaseModel):
    model_version: str
    feature_columns: list[str]
    training_rows: int
    unique_participants: int
    unique_trails: int
    cross_validation: dict
    thresholds: dict


def categorise(probability):
    if probability >= GOOD_MATCH_THRESHOLD:
        return "Good Match"
    if probability >= BORDERLINE_THRESHOLD:
        return "Borderline"
    return "Not Recommended"


@app.get("/")
def health_check():
    return {"status": "TrailGuard ML API v3 is running", "model_version": MODEL_VERSION}


@app.get("/model-info", response_model=ModelInfoResponse)
def model_info():
    return ModelInfoResponse(
        model_version=MODEL_VERSION,
        feature_columns=FEATURE_COLUMNS,
        training_rows=METADATA["training_rows"],
        unique_participants=METADATA["unique_participants"],
        unique_trails=METADATA["unique_trails"],
        cross_validation=METADATA["cross_validation"],
        thresholds=METADATA["thresholds"],
    )


@app.post("/predict", response_model=PredictionResponse)
def predict(request: PredictionRequest):
    payload = request.model_dump()

    if not 1 <= payload["trail_class"] <= 4:
        raise HTTPException(
            status_code=422,
            detail=f"trail_class must be 1-4, received {payload['trail_class']}",
        )

    try:
        feature_values = encode_request(payload)
    except UnknownCategoryError as exc:
        logger.warning("Rejected request with unrecognised category: %s", exc)
        raise HTTPException(status_code=422, detail=str(exc)) from exc

    input_row = np.array([feature_values], dtype=float)

    probability = float(model.predict_proba(input_row)[0][1])
    label = categorise(probability)

    shap_row = explainer(input_row).values[0]
    explanation = explain_one(shap_row, feature_values)

    return PredictionResponse(
        suitability_label=label,
        completion_probability=probability,
        model_version=MODEL_VERSION,
        thresholds=METADATA["thresholds"],
        shap_breakdown=explanation["breakdown"],
        recommendations=explanation["recommendations"],
        medical_flags=explanation["medical_flags"],
    )