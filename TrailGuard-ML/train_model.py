"""TrailGuard - model training (v2). Monotonic constraints enforced."""

import pandas as pd, numpy as np, joblib, xgboost as xgb
from sklearn.model_selection import train_test_split
from sklearn.preprocessing import LabelEncoder
from sklearn.metrics import (accuracy_score, precision_recall_fscore_support,
                             classification_report, confusion_matrix)
from generate_synthetic_dataset import FEATURE_COLUMNS

# Safety monotonicity. Classes are encoded alphabetically by LabelEncoder:
#   0 = Borderline, 1 = Good Match, 2 = Not Recommended
# so the constraint sign is expressed against the "Good Match" logit.
#  +1 : more of this feature must never worsen the outcome
#  -1 : more of this feature must never improve the outcome
#   0 : unconstrained (BMI is non-monotonic - both extremes are penalised)
MONOTONE = {
    "bmi": 0,
    "exercise_frequency_score": 1,
    "continuous_cardio_duration_score": 1,
    "exercise_consistency_score": 1,
    "hiking_experience_score": 1,
    "last_hike_recency_score": 1,
    "hardest_trail_completed_score": 1,
    "gear_score": 1,
    "has_asthma": -1,
    "has_cvd": -1,
    "has_joint_knee_injury": -1,
    "has_cvd_symptoms": -1,
    "trail_shenandoah_score": -1,
    "trail_terrain_type": -1,
}

df = pd.read_csv("trailguard_synthetic_dataset_v2.csv")
X, y_raw = df[FEATURE_COLUMNS], df["suitability_label"]
le = LabelEncoder(); y = le.fit_transform(y_raw)

X_train, X_test, y_train, y_test = train_test_split(
    X, y, test_size=0.2, random_state=42, stratify=y)

model = xgb.XGBClassifier(
    objective="multi:softprob", num_class=3, eval_metric="mlogloss",
    n_estimators=400, max_depth=5, learning_rate=0.08,
    min_child_weight=3, subsample=0.9, colsample_bytree=0.9,
    monotone_constraints="(" + ",".join(str(MONOTONE[c]) for c in FEATURE_COLUMNS) + ")",
    random_state=42,
)
model.fit(X_train, y_train)

pred = model.predict(X_test)
acc = accuracy_score(y_test, pred)
pr, rc, f1, _ = precision_recall_fscore_support(y_test, pred, average="weighted")

print("=" * 52); print("TEST SET RESULTS (v2)"); print("=" * 52)
print(f"Accuracy: {acc:.4f}  Precision: {pr:.4f}  Recall: {rc:.4f}  F1: {f1:.4f}\n")
print(classification_report(y_test, pred, target_names=le.classes_))
print("Confusion matrix (rows = actual):")
print(pd.DataFrame(confusion_matrix(y_test, pred),
                   index=le.classes_, columns=le.classes_))

# Safety-critical error: someone the rules call Not Recommended, predicted Good Match
nr, gm = list(le.classes_).index("Not Recommended"), list(le.classes_).index("Good Match")
print(f"\nCritical false-positives (Not Recommended -> Good Match): "
      f"{int(((y_test == nr) & (pred == gm)).sum())} of {len(y_test)}")

model.save_model("trailguard_xgboost_model_v2.json")
joblib.dump(le, "label_encoder_v2.pkl")
print("\nSaved trailguard_xgboost_model_v2.json")
