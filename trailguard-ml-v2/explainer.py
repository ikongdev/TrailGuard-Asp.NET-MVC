import numpy as np
import shap

from encoding import FEATURE_COLUMNS

TOP_N = 5
MIN_RECOMMENDATION_SHARE_PCT = 2.0
MAX_RECOMMENDATIONS = 3

ACTIONABLE = "actionable"
CONTEXT = "context"
MEDICAL = "medical"
TRAIL = "trail"

FEATURE_CATEGORY = {
    "exercise_frequency": ACTIONABLE,
    "cardio_duration": ACTIONABLE,
    "exercise_consistency": ACTIONABLE,
    "gear_score": ACTIONABLE,
    "bmi": CONTEXT,
    "hiking_experience": CONTEXT,
    "last_hike_recency": CONTEXT,
    "hardest_trail_completed": CONTEXT,
    "has_asthma": MEDICAL,
    "has_cvd": MEDICAL,
    "has_joint_knee_injury": MEDICAL,
    "has_signs_symptoms": MEDICAL,
    "distance_km": TRAIL,
    "elevation_gain_m": TRAIL,
    "trail_class": TRAIL,
    "typical_duration_hours": TRAIL,
}

FRIENDLY_NAMES = {
    "bmi": "Body mass index",
    "exercise_frequency": "Exercise frequency",
    "cardio_duration": "Cardio endurance",
    "exercise_consistency": "Exercise consistency",
    "hiking_experience": "Hiking experience",
    "last_hike_recency": "Recency of last hike",
    "hardest_trail_completed": "Hardest trail completed",
    "gear_score": "Gear preparedness",
    "has_asthma": "Asthma",
    "has_cvd": "Heart condition or hypertension",
    "has_joint_knee_injury": "Joint or knee injury",
    "has_signs_symptoms": "Cardiovascular signs or symptoms",
    "distance_km": "Trail distance",
    "elevation_gain_m": "Trail elevation gain",
    "trail_class": "Trail technical class",
    "typical_duration_hours": "Trail duration",
}

if set(FEATURE_CATEGORY) != set(FEATURE_COLUMNS):
    raise RuntimeError(
        "FEATURE_CATEGORY does not match FEATURE_COLUMNS: "
        f"missing={set(FEATURE_COLUMNS) - set(FEATURE_CATEGORY)}, "
        f"unexpected={set(FEATURE_CATEGORY) - set(FEATURE_COLUMNS)}"
    )

if set(FRIENDLY_NAMES) != set(FEATURE_COLUMNS):
    raise RuntimeError(
        "FRIENDLY_NAMES does not match FEATURE_COLUMNS: "
        f"missing={set(FEATURE_COLUMNS) - set(FRIENDLY_NAMES)}, "
        f"unexpected={set(FRIENDLY_NAMES) - set(FEATURE_COLUMNS)}"
    )


def build_explainer(model):
    return shap.TreeExplainer(model)


def is_displayable(name, raw_value):
    if FEATURE_CATEGORY[name] != MEDICAL:
        return True
    return raw_value == 1


def build_shap_breakdown(shap_row, feature_values, top_n=TOP_N):
    shap_row = np.asarray(shap_row, dtype=float)
    impacts = np.abs(shap_row)

    eligible = [i for i in range(len(FEATURE_COLUMNS))
                if is_displayable(FEATURE_COLUMNS[i], float(feature_values[i]))]

    order = sorted(eligible, key=lambda i: -impacts[i])[:top_n]

    displayed_total = sum(impacts[i] for i in order)
    if displayed_total == 0:
        return []

    breakdown = []
    for i in order:
        name = FEATURE_COLUMNS[i]
        shap_value = float(shap_row[i])
        breakdown.append({
            "feature": name,
            "friendly_name": FRIENDLY_NAMES[name],
            "category": FEATURE_CATEGORY[name],
            "raw_value": float(feature_values[i]),
            "shap_value": shap_value,
            "share_pct": round(float(100 * impacts[i] / displayed_total), 1),
            "direction": "Helped" if shap_value > 0 else "Reduced",
        })
    return breakdown


def build_shap_all(shap_row, feature_values):
    """Return raw values for persistence; this list has no display denominator."""
    shap_row = np.asarray(shap_row, dtype=float)
    return [
        {
            "feature": name,
            "friendly_name": FRIENDLY_NAMES[name],
            "category": FEATURE_CATEGORY[name],
            "raw_value": float(feature_values[i]),
            "shap_value": float(shap_row[i]),
        }
        for i, name in enumerate(FEATURE_COLUMNS)
    ]


def recommendation_targets(shap_row, feature_values,
                           min_share_pct=MIN_RECOMMENDATION_SHARE_PCT,
                           max_items=MAX_RECOMMENDATIONS):
    shap_row = np.asarray(shap_row, dtype=float)
    total_impact = np.abs(shap_row).sum()
    if total_impact == 0:
        return []

    candidates = []
    for i, name in enumerate(FEATURE_COLUMNS):
        shap_value = float(shap_row[i])
        if shap_value >= 0 or FEATURE_CATEGORY[name] != ACTIONABLE:
            continue

        share = float(100 * abs(shap_value) / total_impact)
        if share < min_share_pct:
            continue

        candidates.append({
            "feature": name,
            "friendly_name": FRIENDLY_NAMES[name],
            "raw_value": float(feature_values[i]),
            "shap_value": shap_value,
            "share_pct": round(share, 1),
        })

    candidates.sort(key=lambda c: c["shap_value"])
    return candidates[:max_items]


def clearance_flags(breakdown):
    return [b for b in breakdown
            if b["direction"] == "Reduced" and b["category"] == MEDICAL
            and b["raw_value"] == 1]


def explain_one(shap_row, feature_values):
    breakdown = build_shap_breakdown(shap_row, feature_values)
    return {
        "breakdown": breakdown,
        "all": build_shap_all(shap_row, feature_values),
        "recommendations": recommendation_targets(shap_row, feature_values),
        "medical_flags": clearance_flags(breakdown),
    }
