import numpy as np
import pandas as pd
import xgboost as xgb
from sklearn.model_selection import GroupKFold

from encoding import load_and_encode, FEATURE_COLUMNS

df = load_and_encode("data/TrailGuard_Training_Data_Final.xlsx")

X = df[FEATURE_COLUMNS]
y = df["completed"]
groups = df["participant_profile_id"]

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

gkf = GroupKFold(n_splits=5)
oof_proba = np.zeros(len(df))

for train_idx, test_idx in gkf.split(X, y, groups):
    model = xgb.XGBClassifier(
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
    model.fit(X.iloc[train_idx], y.iloc[train_idx])
    oof_proba[test_idx] = model.predict_proba(X.iloc[test_idx])[:, 1]

df["predicted_proba"] = oof_proba

bins = [0.0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0]
labels = ["0-10%", "10-20%", "20-30%", "30-40%", "40-50%",
          "50-60%", "60-70%", "70-80%", "80-90%", "90-100%"]

df["proba_bin"] = pd.cut(df["predicted_proba"], bins=bins, labels=labels, include_lowest=True)

summary = df.groupby("proba_bin", observed=True)["completed"].agg(["count", "sum", "mean"])
summary.columns = ["n_cases", "n_completed", "actual_completion_rate"]
summary["pct_of_total"] = (summary["n_cases"] / len(df) * 100).round(1)

print("Out-of-fold probability distribution (all 1200 rows):\n")
print(summary.to_string())

print(f"\nOverall completion rate: {df['completed'].mean():.4f}")
print(f"Mean predicted probability: {df['predicted_proba'].mean():.4f}")