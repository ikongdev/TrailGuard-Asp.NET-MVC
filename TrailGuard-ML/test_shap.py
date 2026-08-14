import pandas as pd
import numpy as np
import xgboost as xgb
import shap
import joblib

df = pd.read_csv("trailguard_synthetic_dataset.csv")

feature_columns = [
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

X = df[feature_columns]

explainer = shap.TreeExplainer(model)
shap_values = explainer(X.iloc[:5])

print("SHAP values shape:", shap_values.values.shape)
print("Classes:", list(label_encoder.classes_))

sample_index = 0
sample_row = X.iloc[sample_index]
predicted_class_index = model.predict(X.iloc[[sample_index]])[0]
predicted_label = label_encoder.classes_[predicted_class_index]

print(f"\n--- Sample participant (row {sample_index}) ---")
print(f"Predicted label: {predicted_label}")

sample_shap = shap_values.values[sample_index, :, predicted_class_index]

shap_df = pd.DataFrame({
    "feature": feature_columns,
    "raw_value": sample_row.values,
    "shap_impact": sample_shap,
}).sort_values("shap_impact", key=abs, ascending=False)

print("\nTop feature impacts on this prediction:")
print(shap_df.head(10).to_string(index=False))

print("\nSHAP setup verified successfully.")