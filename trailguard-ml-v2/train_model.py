import json
import numpy as np
import pandas as pd
import xgboost as xgb
from sklearn.model_selection import GroupKFold
from sklearn.metrics import accuracy_score, confusion_matrix

from encoding import load_and_encode, FEATURE_COLUMNS

DATA_PATH = "data/TrailGuard_Training_Data_Final.xlsx"
MODEL_PATH = "trailguard_model.json"
METADATA_PATH = "model_metadata.json"

RATIO_BORDERLINE = 0.30
RATIO_GOOD_MATCH = 0.80

MONOTONE_CONSTRAINTS = {
    "bmi": 0,
    "exercise_frequency": 1,
    "cardio_duration": 1,
    "exercise_consistency": 1,
    "hiking_experience": 1,
    "last_hike_recency": 1,
    "hardest_trail_completed": 1,
    "gear_score": 1,
    "has_asthma": -1,
    "has_cvd": -1,
    "has_joint_knee_injury": -1,
    "has_signs_symptoms": -1,
    "distance_km": -1,
    "elevation_gain_m": -1,
    "trail_class": -1,
    "typical_duration_hours": -1,
}

CONSTRAINT_STRING = "(" + ",".join(str(MONOTONE_CONSTRAINTS[c]) for c in FEATURE_COLUMNS) + ")"

HYPERPARAMS = dict(
    objective="binary:logistic",
    eval_metric="logloss",
    n_estimators=150,
    max_depth=3,
    learning_rate=0.08,
    min_child_weight=10,
    subsample=0.9,
    colsample_bytree=0.9,
    monotone_constraints=CONSTRAINT_STRING,
    random_state=42,
)


def build_model():
    return xgb.XGBClassifier(**HYPERPARAMS)


df = load_and_encode(DATA_PATH)
X = df[FEATURE_COLUMNS]
y = df["completed"]
groups = df["participant_profile_id"]

gkf = GroupKFold(n_splits=5)
accuracies = []
unsafe_counts = []
oof_proba = np.zeros(len(df))

for fold, (train_idx, test_idx) in enumerate(gkf.split(X, y, groups), start=1):
    model = build_model()
    model.fit(X.iloc[train_idx], y.iloc[train_idx])

    pred = model.predict(X.iloc[test_idx])
    proba = model.predict_proba(X.iloc[test_idx])[:, 1]
    oof_proba[test_idx] = proba

    acc = accuracy_score(y.iloc[test_idx], pred)
    cm = confusion_matrix(y.iloc[test_idx], pred)

    accuracies.append(acc)
    unsafe_counts.append(int(cm[0][1]))
    print(f"Fold {fold}: accuracy={acc:.4f}  false_completed={cm[0][1]}")

cv_mean = float(np.mean(accuracies))
cv_std = float(np.std(accuracies))

print(f"\nCross-validated accuracy: {cv_mean:.4f} +/- {cv_std:.4f}")
print(f"Safety-critical errors per fold: {unsafe_counts}  (total {sum(unsafe_counts)})")

category = np.where(oof_proba >= RATIO_GOOD_MATCH, "Good Match",
            np.where(oof_proba >= RATIO_BORDERLINE, "Borderline", "Not Recommended"))

gm_mask = category == "Good Match"
nr_mask = category == "Not Recommended"

unsafe_gm = int(((y == 0) & gm_mask).sum())
over_rejected = int(((y == 1) & nr_mask).sum())

print(f"\nCategory distribution at thresholds {RATIO_BORDERLINE} / {RATIO_GOOD_MATCH}:")
for name in ["Good Match", "Borderline", "Not Recommended"]:
    n = int((category == name).sum())
    print(f"  {name:18s} {n:5d}  ({100 * n / len(df):.1f}%)")

print(f"  Unsafe Good Match  : {unsafe_gm}")
print(f"  Over-rejected      : {over_rejected}")
print(f"  Good Match purity  : {y[gm_mask].mean():.3f}")

final_model = build_model()
final_model.fit(X, y)
final_model.save_model(MODEL_PATH)

metadata = {
    "model_version": "v3-real-outcomes",
    "training_rows": int(len(df)),
    "unique_participants": int(groups.nunique()),
    "unique_trails": int(df["trail_id"].nunique()),
    "feature_columns": FEATURE_COLUMNS,
    "target": "completed",
    "hyperparameters": {k: v for k, v in HYPERPARAMS.items()},
    "monotone_constraints": MONOTONE_CONSTRAINTS,
    "thresholds": {
        "borderline": RATIO_BORDERLINE,
        "good_match": RATIO_GOOD_MATCH,
    },
    "cross_validation": {
        "method": "GroupKFold, 5 splits, grouped by participant_profile_id",
        "mean_accuracy": round(cv_mean, 4),
        "std_accuracy": round(cv_std, 4),
        "fold_accuracies": [round(a, 4) for a in accuracies],
        "safety_critical_errors_per_fold": unsafe_counts,
    },
    "out_of_fold_category_distribution": {
        name: int((category == name).sum())
        for name in ["Good Match", "Borderline", "Not Recommended"]
    },
    "out_of_fold_unsafe_good_match": unsafe_gm,
    "out_of_fold_over_rejected": over_rejected,
}

with open(METADATA_PATH, "w") as f:
    json.dump(metadata, f, indent=2)

print(f"\nSaved model to {MODEL_PATH}")
print(f"Saved metadata to {METADATA_PATH}")