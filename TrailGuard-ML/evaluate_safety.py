"""TrailGuard - safety evaluation suite. These four tests go in the manuscript."""
import pandas as pd, numpy as np, xgboost as xgb, shap, joblib, warnings
warnings.filterwarnings("ignore")
from generate_synthetic_dataset import FEATURE_COLUMNS

df = pd.read_csv("trailguard_synthetic_dataset_v2.csv"); X = df[FEATURE_COLUMNS].copy()
m = xgb.XGBClassifier(); m.load_model("trailguard_xgboost_model_v2.json")
le = joblib.load("label_encoder_v2.pkl"); C = list(le.classes_)
RANK = {"Not Recommended":0, "Borderline":1, "Good Match":2}
lab  = lambda Xr: np.array([C[i] for i in m.predict_proba(Xr).argmax(1)])
rank = lambda Xr: np.array([RANK[l] for l in lab(Xr)])
base, brank = lab(X), rank(X)

print("TEST 1 - HEALTH SENSITIVITY")
for name, cols in [("CVD symptoms", ["has_cvd_symptoms"]), ("known CVD", ["has_cvd"]),
                   ("joint/knee injury", ["has_joint_knee_injury"]),
                   ("asthma", ["has_asthma"]),
                   ("all conditions at once", ["has_cvd_symptoms","has_cvd","has_joint_knee_injury","has_asthma"])]:
    Xc = X.copy()
    for c in cols: Xc[c] = 1
    print(f"  turn ON {name:24s} -> label changed for {100*np.mean(lab(Xc)!=base):5.2f}%")
Xg = X.copy()
for g in ["gear_score"]: Xg[g] = 0
print(f"  {'remove all gear':32s} -> label changed for {100*np.mean(lab(Xg)!=base):5.2f}%")

print("\nTEST 2 - EXPLANATION HONESTY (dead features in the displayed top-10)")
sv = shap.TreeExplainer(m)(X).values
pred_i = m.predict(X)
per = np.abs(np.array([sv[i,:,pred_i[i]] for i in range(len(X))]))
top10 = np.argsort(-per, axis=1)[:, :10]
DEAD = [f for f in FEATURE_COLUMNS if f in ("age","height_cm","weight_kg","exercise_type_category")]
print(f"  features never used to build the label : {DEAD if DEAD else 'NONE'}")
print(f"  participants shown a dead feature      : "
      f"{100*np.mean([any(FEATURE_COLUMNS[j] in DEAD for j in r) for r in top10]):.1f}%")

print("\nTEST 3 - MONOTONICITY")
checks = [("hiking_experience_score",0,3,+1),("exercise_frequency_score",0,3,+1),
          ("exercise_consistency_score",0,2,+1),("gear_score",0,8,+1),
          ("has_cvd",0,1,-1),("has_cvd_symptoms",0,1,-1),
          ("trail_shenandoah_score",60,260,-1),("trail_terrain_type",1,3,-1)]
bad = 0
for col, lo_v, hi_v, sign in checks:
    lo, hi = X.copy(), X.copy(); lo[col], hi[col] = lo_v, hi_v
    rl, rh = rank(lo), rank(hi)
    viol = np.mean(rh < rl) if sign > 0 else np.mean(rh > rl)
    bad += viol > 0
    print(f"  {col:30s} violations: {100*viol:.2f}%")
print(f"  -> {'PASS' if bad==0 else 'FAIL'}")

print("\nTEST 4 - FIDELITY CEILING")
print(f"  rule engine reproduces stored label : 100.00%  (by construction)")
print(f"  model reproduces stored label       : {100*np.mean(base==df.suitability_label):.2f}%")

print("\nTEST 5 - FACE VALIDITY (real Philippine trails, NPS rating)")
from generate_synthetic_dataset import shenandoah_rating, nps_band
for n,d,e in [("Mt. Maculot Rockies",3.0,450),("Mt. Daraitan",6.0,600),
              ("Mt. Batulao",6.0,700),("Mt. Ulap",9.0,700),("Mt. Pulag Ambangeg",14.0,900)]:
    r = shenandoah_rating(d,e); print(f"  {n:22s} {d:4.1f}km {e:4.0f}m -> {r:6.1f}  {nps_band(r)}")
