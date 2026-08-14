from fastapi import FastAPI
from pydantic import BaseModel
import pandas as pd
import xgboost as xgb
import shap
import joblib

app = FastAPI(title="TrailGuard Suitability Prediction API")

FEATURE_COLUMNS = [
    "age", "height_cm", "weight_kg", "bmi",
    "has_asthma", "has_hypertension_heart_condition", "has_joint_knee_injury", "has_vertigo",
    "exercise_frequency_score", "exercise_type_category", "continuous_cardio_duration_score",
    "hiking_experience_score", "last_hike_recency_score", "hardest_trail_completed_score",
    "gear_water", "gear_trail_food", "gear_first_aid_medicine", "gear_flashlight_headlamp",
    "gear_whistle", "gear_raincoat_poncho", "gear_navigation", "gear_proper_shoes", "gear_score",
    "trail_distance_km", "trail_elevation_gain_m", "trail_terrain_type", "trail_estimated_duration_hr",
]

model = xgb.XGBClassifier()
model.load_model("trailguard_xgboost_model.json")

label_encoder = joblib.load("label_encoder.pkl")

explainer = shap.TreeExplainer(model)


class PredictionRequest(BaseModel):
    age: int
    height_cm: float
    weight_kg: float
    bmi: float
    has_asthma: int
    has_hypertension_heart_condition: int
    has_joint_knee_injury: int
    has_vertigo: int
    exercise_frequency_score: int
    exercise_type_category: int
    continuous_cardio_duration_score: int
    hiking_experience_score: int
    last_hike_recency_score: int
    hardest_trail_completed_score: int
    gear_water: int
    gear_trail_food: int
    gear_first_aid_medicine: int
    gear_flashlight_headlamp: int
    gear_whistle: int
    gear_raincoat_poncho: int
    gear_navigation: int
    gear_proper_shoes: int
    gear_score: int
    trail_distance_km: float
    trail_elevation_gain_m: float
    trail_terrain_type: int
    trail_estimated_duration_hr: float


class ShapFeatureImpact(BaseModel):
    feature: str
    raw_value: float
    impact: float


class PredictionResponse(BaseModel):
    suitability_label: str
    confidence_score: float
    model_version: str
    shap_breakdown: list[ShapFeatureImpact]


@app.get("/")
def health_check():
    return {"status": "TrailGuard ML API is running"}


@app.post("/predict", response_model=PredictionResponse)
def predict(request: PredictionRequest):
    input_dict = request.model_dump()
    input_row = pd.DataFrame([input_dict])[FEATURE_COLUMNS]

    predicted_class_index = int(model.predict(input_row)[0])
    predicted_probabilities = model.predict_proba(input_row)[0]
    predicted_label = label_encoder.classes_[predicted_class_index]
    confidence = float(predicted_probabilities[predicted_class_index])

    shap_result = explainer(input_row)
    sample_shap = shap_result.values[0, :, predicted_class_index]

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
        suitability_label=str(predicted_label),
        confidence_score=confidence,
        model_version="v1-synthetic",
        shap_breakdown=shap_breakdown[:10],
    )