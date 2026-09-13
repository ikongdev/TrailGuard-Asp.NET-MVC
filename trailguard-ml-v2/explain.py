import numpy as np
import xgboost as xgb

from encoding import load_and_encode, FEATURE_COLUMNS
from explainer import build_explainer, explain_one, recommendation_targets

model = xgb.XGBClassifier()
model.load_model("trailguard_model.json")

df = load_and_encode("data/TrailGuard_Training_Data_Final.xlsx")
X = df[FEATURE_COLUMNS]

explainer = build_explainer(model)
shap_values = explainer(X)

for sample_idx in [0, 4, 25]:
    row = X.iloc[sample_idx].to_numpy()
    proba = model.predict_proba(X.iloc[[sample_idx]])[0][1]
    result = explain_one(shap_values.values[sample_idx], row)

    print(f"\n{'=' * 76}")
    print(f"Case {df.iloc[sample_idx]['case_id']}  |  trail {df.iloc[sample_idx]['trail_id']}")
    print(f"Predicted probability : {proba:.4f}")
    print(f"Actual outcome        : {'Completed' if df.iloc[sample_idx]['completed'] == 1 else 'Not completed'}")
    print(f"{'=' * 76}")
    print(f"{'Factor':36s} {'Direction':>10s} {'Share':>8s} {'Category':>12s}")
    for item in result["breakdown"]:
        print(f"{item['friendly_name']:36s} {item['direction']:>10s} "
              f"{item['share_pct']:7.1f}% {item['category']:>12s}")

    recs = result["recommendations"]
    meds = result["medical_flags"]

    rec_text = ", ".join(f"{b['friendly_name']} ({b['share_pct']}%)" for b in recs) if recs else "none"
    med_text = ", ".join(b["friendly_name"] for b in meds) if meds else "none"

    print(f"\nRecommendations : {rec_text}")
    print(f"Medical pathway : {med_text}")

print(f"\n{'=' * 76}")
print("COVERAGE ACROSS ALL 1,200 CASES")
print(f"{'=' * 76}")

X_np = X.to_numpy()
rec_counts = []
rec_feature_tally = {}

for i in range(len(df)):
    recs = recommendation_targets(shap_values.values[i], X_np[i])
    rec_counts.append(len(recs))
    for r in recs:
        rec_feature_tally[r["friendly_name"]] = rec_feature_tally.get(r["friendly_name"], 0) + 1

rec_counts = np.array(rec_counts)
proba_all = model.predict_proba(X)[:, 1]

print(f"\nCases with at least one recommendation : "
      f"{(rec_counts > 0).sum()} of {len(df)} ({100 * (rec_counts > 0).mean():.1f}%)")
print(f"Mean recommendations per case          : {rec_counts.mean():.2f}")

print("\nBy predicted probability band:")
bands = [(0.0, 0.3, "Not Recommended"), (0.3, 0.8, "Borderline"), (0.8, 1.01, "Good Match")]
for lo, hi, label in bands:
    mask = (proba_all >= lo) & (proba_all < hi)
    n = mask.sum()
    with_rec = (rec_counts[mask] > 0).sum()
    print(f"  {label:18s} n={n:5d}   with recommendation: {with_rec:5d} "
          f"({100 * with_rec / n:.1f}%)")

print("\nMost common recommendation targets:")
for name, count in sorted(rec_feature_tally.items(), key=lambda x: -x[1]):
    print(f"  {name:28s} {count:5d} ({100 * count / len(df):.1f}% of all cases)")