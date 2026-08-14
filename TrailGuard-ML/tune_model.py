import pandas as pd
import numpy as np
from sklearn.model_selection import train_test_split, GridSearchCV, StratifiedKFold
from sklearn.preprocessing import LabelEncoder
from sklearn.metrics import accuracy_score, precision_recall_fscore_support, confusion_matrix, classification_report
import xgboost as xgb
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

X = df[feature_columns]
y_raw = df["suitability_label"]

label_encoder = LabelEncoder()
y = label_encoder.fit_transform(y_raw)

X_train, X_test, y_train, y_test = train_test_split(
    X, y, test_size=0.2, random_state=42, stratify=y
)

param_grid = {
    "max_depth": [3, 4, 5, 6],
    "n_estimators": [100, 200, 300],
    "learning_rate": [0.05, 0.1, 0.2],
    "min_child_weight": [1, 3, 5],
}

base_model = xgb.XGBClassifier(
    objective="multi:softprob",
    num_class=3,
    eval_metric="mlogloss",
    random_state=42,
)

cv = StratifiedKFold(n_splits=5, shuffle=True, random_state=42)

print("Starting grid search... this may take a few minutes.")
grid_search = GridSearchCV(
    estimator=base_model,
    param_grid=param_grid,
    scoring="f1_weighted",
    cv=cv,
    n_jobs=-1,
    verbose=1,
)

grid_search.fit(X_train, y_train)

print("\n" + "=" * 50)
print("BEST HYPERPARAMETERS FOUND")
print("=" * 50)
for param, value in grid_search.best_params_.items():
    print(f"  {param}: {value}")
print(f"\nBest cross-validation F1 (weighted): {grid_search.best_score_:.4f}")

best_model = grid_search.best_estimator_
y_pred = best_model.predict(X_test)

accuracy = accuracy_score(y_test, y_pred)
precision, recall, f1, support = precision_recall_fscore_support(y_test, y_pred, average="weighted")

print("\n" + "=" * 50)
print("TEST SET RESULTS (TUNED MODEL)")
print("=" * 50)
print(f"Accuracy:  {accuracy:.4f}")
print(f"Precision: {precision:.4f}")
print(f"Recall:    {recall:.4f}")
print(f"F1 Score:  {f1:.4f}")

print("\nPer-Class Report:")
print(classification_report(y_test, y_pred, target_names=label_encoder.classes_))

print("Confusion Matrix:")
print("Rows = actual, Columns = predicted")
cm = confusion_matrix(y_test, y_pred)
cm_df = pd.DataFrame(cm, index=label_encoder.classes_, columns=label_encoder.classes_)
print(cm_df)

best_model.save_model("trailguard_xgboost_model.json")
joblib.dump(label_encoder, "label_encoder.pkl")

print("\nTuned model saved as trailguard_xgboost_model.json (overwrote previous version)")