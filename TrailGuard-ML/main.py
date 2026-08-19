import logging

import pandas as pd
import numpy as np
import xgboost as xgb
import shap
import joblib
from fastapi import FastAPI
from pydantic import BaseModel
from sklearn.model_selection import train_test_split
from sklearn.metrics import accuracy_score

import acsm_gate
from generate_synthetic_dataset import FEATURE_COLUMNS, TERRAIN_MULTIPLIER

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")
logger = logging.getLogger("trailguard.gate")

MODEL_VERSION = "v2-acsm"

app = FastAPI(title="TrailGuard Suitability Prediction API")

model = xgb.XGBClassifier()
model.load_model("trailguard_xgboost_model_v2.json")

label_encoder = joblib.load("label_encoder_v2.pkl")

explainer = shap.TreeExplainer(model)

# SHAP is always explained against the "Good Match" class logit, never the
# predicted class. For a gated case (e.g. reported CVD symptoms) the predicted
# class is "Not Recommended" reached by elimination: the model has pushed the
# case out of "Good Match" and "Borderline", so at the "Not Recommended" logit
# itself the decisive feature can show an impact of ~0.0 while an unrelated
# feature (e.g. BMI) appears dominant. Anchoring on "Good Match" instead means
# a negative impact always means "pushed away from Good Match", which is what
# the participant-facing "Helped / Reduced your result" copy already claims.
# Looked up by label rather than hardcoded so a change in class ordering can't
# silently break this.
GOOD_MATCH_CLASS_INDEX = int(list(label_encoder.classes_).index("Good Match"))

# Reproduce train_model.py's held-out test split (same random_state/test_size/
# stratify) against the model already on disk, so /model-info reports a real,
# current accuracy figure without retraining at startup.
_df = pd.read_csv("trailguard_synthetic_dataset_v2.csv")
TRAINING_ROW_COUNT = len(_df)

_X = _df[FEATURE_COLUMNS]
_y = label_encoder.transform(_df["suitability_label"])
_, _X_test, _, _y_test = train_test_split(
    _X, _y, test_size=0.2, random_state=42, stratify=_y)
TEST_ACCURACY = float(accuracy_score(_y_test, model.predict(_X_test)))


class PredictionRequest(BaseModel):
    bmi: float
    exercise_frequency_score: int
    continuous_cardio_duration_score: int
    exercise_consistency_score: int
    hiking_experience_score: int
    last_hike_recency_score: int
    hardest_trail_completed_score: int
    gear_score: int
    has_asthma: int
    has_cvd: int
    has_joint_knee_injury: int
    has_cvd_symptoms: int
    trail_distance_km: float
    trail_elevation_gain_m: float
    trail_terrain_type: int


class ShapFeatureImpact(BaseModel):
    feature: str
    raw_value: float
    impact: float


class PredictionResponse(BaseModel):
    suitability_label: str
    model_label: str
    confidence_score: float
    medical_clearance_required: bool
    gate_applied: bool
    gate_reason: str
    nps_score: float
    nps_band: str
    model_version: str = MODEL_VERSION
    shap_breakdown: list[ShapFeatureImpact]


class ModelInfoResponse(BaseModel):
    model_version: str
    feature_columns: list[str]
    training_row_count: int
    test_accuracy: float


@app.get("/")
def health_check():
    return {"status": "TrailGuard ML API is running"}


@app.get("/model-info", response_model=ModelInfoResponse)
def model_info():
    return ModelInfoResponse(
        model_version=MODEL_VERSION,
        feature_columns=FEATURE_COLUMNS,
        training_row_count=TRAINING_ROW_COUNT,
        test_accuracy=TEST_ACCURACY,
    )


@app.post("/predict", response_model=PredictionResponse)
def predict(request: PredictionRequest):
    # The NPS Shenandoah formula lives only in acsm_gate.py - the caller sends
    # raw trail geometry, never the derived score, so C# can't drift from it.
    rating = float(acsm_gate.shenandoah_rating(
        request.trail_distance_km, request.trail_elevation_gain_m))
    band = str(acsm_gate.nps_band(np.array([rating]))[0])

    feature_values = request.model_dump()
    feature_values["trail_shenandoah_score"] = rating
    input_row = pd.DataFrame([feature_values])[FEATURE_COLUMNS]

    predicted_class_index = int(model.predict(input_row)[0])
    predicted_probabilities = model.predict_proba(input_row)[0]
    model_label = str(label_encoder.classes_[predicted_class_index])
    confidence = float(predicted_probabilities[predicted_class_index])

    # Same terrain-adjusted demand the gate was validated against in
    # generate_synthetic_dataset.build() - the raw NPS rating alone is not
    # what apply_acsm_gate's thresholds were calibrated on.
    adjusted_demand = np.array([rating * TERRAIN_MULTIPLIER[request.trail_terrain_type]])

    gated_labels, clearance, reasons = acsm_gate.apply_acsm_gate(
        np.array([model_label]), input_row, adjusted_demand)
    suitability_label = str(gated_labels[0])
    medical_clearance_required = bool(clearance[0])
    gate_reason = str(reasons[0])
    gate_applied = suitability_label != model_label

    if gate_applied:
        logger.warning(
            "ACSM gate override: model=%s -> gate=%s reason=%r input=%s",
            model_label, suitability_label, gate_reason, feature_values,
        )

    shap_result = explainer(input_row)
    sample_shap = shap_result.values[0, :, GOOD_MATCH_CLASS_INDEX]

    shap_breakdown = [
        ShapFeatureImpact(
            feature=feature_name,
            raw_value=float(input_row.iloc[0][feature_name]),
            impact=float(impact_value),
        )
        for feature_name, impact_value in zip(FEATURE_COLUMNS, sample_shap)
    ]
    shap_breakdown.sort(key=lambda x: abs(x.impact), reverse=True)

    return PredictionResponse(
        suitability_label=suitability_label,
        model_label=model_label,
        confidence_score=confidence,
        medical_clearance_required=medical_clearance_required,
        gate_applied=gate_applied,
        gate_reason=gate_reason,
        nps_score=rating,
        nps_band=band,
        model_version=MODEL_VERSION,
        shap_breakdown=shap_breakdown[:10],
    )
