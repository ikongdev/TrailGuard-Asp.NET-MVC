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

THRESHOLD_OPTIONS = [
    (0.30, 0.70),
    (0.30, 0.80),
    (0.40, 0.70),
    (0.40, 0.80),
    (0.40, 0.85),
    (0.50, 0.80),
    (0.50, 0.85),
    (0.50, 0.90),
]

rows = []

for low, high in THRESHOLD_OPTIONS:
    category = np.where(df["predicted_proba"] >= high, "Good Match",
                np.where(df["predicted_proba"] >= low, "Borderline", "Not Recommended"))

    n_total = len(df)
    n_gm = (category == "Good Match").sum()
    n_bl = (category == "Borderline").sum()
    n_nr = (category == "Not Recommended").sum()

    gm_mask = category == "Good Match"
    nr_mask = category == "Not Recommended"

    unsafe = int(((df["completed"] == 0) & gm_mask).sum())
    over_rejected = int(((df["completed"] == 1) & nr_mask).sum())
    gm_purity = df.loc[gm_mask, "completed"].mean() if n_gm > 0 else float("nan")
    nr_purity = 1 - df.loc[nr_mask, "completed"].mean() if n_nr > 0 else float("nan")

    rows.append({
        "low": low,
        "high": high,
        "GoodMatch_%": round(100 * n_gm / n_total, 1),
        "Borderline_%": round(100 * n_bl / n_total, 1),
        "NotRec_%": round(100 * n_nr / n_total, 1),
        "unsafe_GM": unsafe,
        "over_rejected": over_rejected,
        "GM_purity": round(gm_purity, 3),
        "NR_purity": round(nr_purity, 3),
    })

result = pd.DataFrame(rows)

print("Sensitivity analysis across threshold options (n = 1200, out-of-fold predictions)\n")
print(result.to_string(index=False))

print("\nColumn meanings:")
print("  GoodMatch_% / Borderline_% / NotRec_%  = share of all cases in each category")
print("  unsafe_GM      = actual 'Not Completed' cases shown as Good Match (safety-critical)")
print("  over_rejected  = actual 'Completed' cases shown as Not Recommended")
print("  GM_purity      = share of Good Match cases that actually completed")
print("  NR_purity      = share of Not Recommended cases that actually did not complete")