"""
Instrumented reproduction of the ACSM gate as applied at TRAINING-LABEL
generation time in generate_synthetic_dataset.py's build() - i.e. against
the deterministic score-based label (label_before_gate: the ratio-of-demand-
to-capacity comparison), NOT a trained model's live prediction. This is what
the CSV's own `gate_reason` column records, and what "Cases"/"Model correct
alone"/"Gate overrode" in the original MODEL.md table actually measured.

Captures three columns per rule instead of just the final gated label:

  Cases matched      - rows the rule's own condition applies to
  Gate lowered        - rows where THIS rule's cap() actually fired (its own
                         "hit" in apply_acsm_gate - label_before_gate's rank
                         was still above this rule's ceiling when it ran, so
                         its gate_reason got recorded)
  Already at ceiling  - Cases matched minus Gate lowered: rows where, by the
                         time this rule was evaluated, the running rank was
                         already at or below its ceiling (either the plain
                         score-based label already put it there, or an
                         earlier rule in the sequence did)

Rules run in the same order as acsm_gate.py: signs/symptoms, CVD+inactive,
CVD+active+vigorous, joint injury. Order matters because an earlier rule can
already lower rank before a later rule's mask is even checked.
"""
import pandas as pd, numpy as np, warnings, sys
warnings.filterwarnings("ignore")
import acsm_gate

RANK = {"Not Recommended": 0, "Borderline": 1, "Good Match": 2}

dataset_path = sys.argv[1] if len(sys.argv) > 1 else "trailguard_synthetic_dataset_v2.csv"

df = pd.read_csv(dataset_path)
demand = df.adjusted_trail_demand.to_numpy()

rank = np.array([RANK[l] for l in df.label_before_gate])
active = acsm_gate.is_acsm_physically_active(df).to_numpy()
vigorous = demand >= acsm_gate.VIGOROUS_INTENSITY_NPS_THRESHOLD

rules = [
    ("Signs or symptoms present",
     df.has_cvd_symptoms.to_numpy() == 1, "Not Recommended"),
    ("Known CVD, physically inactive",
     (df.has_cvd.to_numpy() == 1) & ~active, "Not Recommended"),
    ("Known CVD, vigorous-intensity trail",
     (df.has_cvd.to_numpy() == 1) & active & vigorous, "Borderline"),
    ("Joint or knee injury on steep or technical terrain",
     (df.has_joint_knee_injury.to_numpy() == 1) & ((demand >= 150) | (df.trail_terrain_type.to_numpy() >= 3)), "Borderline"),
]

print(f"{'Rule':55s} {'Cases matched':>13s} {'Gate lowered':>13s} {'Already at ceiling':>19s}")
final_reason = np.array([""] * len(df), dtype=object)
for name, mask, ceiling in rules:
    hit = mask & (rank > RANK[ceiling])
    cases = int(mask.sum())
    lowered = int(hit.sum())
    already = cases - lowered
    print(f"{name:55s} {cases:13d} {lowered:13d} {already:19d}")
    rank = rank.copy()
    rank[hit] = RANK[ceiling]
    final_reason[hit] = name

# Sanity check: this reconstruction should exactly reproduce the CSV's own
# suitability_label and gate_reason columns, since it's the same computation
# generate_synthetic_dataset.py already performed once at generation time.
reconstructed_label = np.array([{0:"Not Recommended",1:"Borderline",2:"Good Match"}[r] for r in rank])
label_match = np.mean(reconstructed_label == df.suitability_label)
print(f"\nSanity check - reconstructed suitability_label matches CSV: {100*label_match:.2f}%")
