import numpy as np
import pandas as pd
import xgboost as xgb
from sklearn.model_selection import GroupKFold
from sklearn.metrics import accuracy_score, confusion_matrix

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


def build_model():
    return xgb.XGBClassifier(
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


gkf = GroupKFold(n_splits=5)

accuracies = []
false_good_matches = []
fold_data = {}

for fold, (train_idx, test_idx) in enumerate(gkf.split(X, y, groups), start=1):
    X_train, X_test = X.iloc[train_idx], X.iloc[test_idx]
    y_train, y_test = y.iloc[train_idx], y.iloc[test_idx]

    model = build_model()
    model.fit(X_train, y_train)

    pred = model.predict(X_test)
    acc = accuracy_score(y_test, pred)
    cm = confusion_matrix(y_test, pred)
    fgm = cm[0][1]

    accuracies.append(acc)
    false_good_matches.append(int(fgm))
    fold_data[fold] = (test_idx, y_test, pred)

    n_test_profiles = groups.iloc[test_idx].nunique()
    print(f"Fold {fold}: accuracy={acc:.4f}  test_rows={len(test_idx)}  "
          f"test_profiles={n_test_profiles}  false_completed={fgm}")

print(f"\nMean accuracy: {np.mean(accuracies):.4f}")
print(f"Std deviation: {np.std(accuracies):.4f}")
print(f"Min / Max: {np.min(accuracies):.4f} / {np.max(accuracies):.4f}")
print(f"\nFalse 'Completed' per fold: {false_good_matches}")
print(f"Total across all folds: {sum(false_good_matches)} out of {len(df)} rows")

WORST_FOLD = int(np.argmax(false_good_matches)) + 1
test_idx, y_test, pred = fold_data[WORST_FOLD]

wrong_mask = (y_test.values == 0) & (pred == 1)
wrong_rows = df.iloc[test_idx][wrong_mask]

print(f"\n=== FOLD {WORST_FOLD}: {len(wrong_rows)} false 'Completed' cases ===")
print(wrong_rows[["participant_profile_id", "trail_id", "hiking_experience",
                  "exercise_frequency", "cardio_duration", "hardest_trail_completed",
                  "has_signs_symptoms", "has_cvd", "has_joint_knee_injury",
                  "trail_class", "elevation_gain_m", "bmi"]].to_string())

print(f"\n=== Feature distribution in false 'Completed' cases (Fold {WORST_FOLD}) ===")
for col in ["hiking_experience", "exercise_frequency", "cardio_duration",
            "hardest_trail_completed", "trail_class", "has_signs_symptoms",
            "has_cvd", "has_joint_knee_injury"]:
    print(f"\n{col}:")
    print(wrong_rows[col].value_counts().sort_index().to_string())