"""
TrailGuard - Synthetic Dataset Generator (v2)

Every constant in the CONFIG block below carries a SOURCE tag.
Tags marked PENDING must be replaced with expert-elicited values before
the dataset is used in the final manuscript.

REFERENCES
  [NPS]   National Park Service, Shenandoah National Park.
          "How to Determine Hiking Difficulty."
          Rating = sqrt(elevation_gain_ft * 2 * distance_mi)
          Pace bands: 1.5 / 1.4 / 1.3 / 1.2 mph at <50 / 50-100 / 100-150 / >=150.
  [PM]    PinoyMountaineer difficulty bands - see acsm_gate.nps_band() for the
          boundaries and calibration caveat. trail_nps_band below is diagnostic
          output only (not a model feature).
  [ACSM]  Riebe D, Franklin BA, Thompson PD, Garber CE, Whitfield GP,
          Magal M, Pescatello LS. "Updating ACSM's Recommendations for
          Exercise Preparticipation Health Screening."
          Med Sci Sports Exerc. 2015;47(11):2473-2479.
  [WHO]   WHO BMI classification for adults.
"""

import numpy as np
import pandas as pd

from acsm_gate import (
    ACSM_MIN_FREQUENCY_SCORE,
    ACSM_MIN_CARDIO_SCORE,
    ACSM_MIN_CONSISTENCY_SCORE,
    VIGOROUS_INTENSITY_NPS_THRESHOLD,
    shenandoah_rating,
    nps_band,
    nps_pace_mph,
    is_acsm_physically_active,
    apply_acsm_gate,
    RANK,
    UNRANK,
)

RNG = np.random.default_rng(42)
N_SAMPLES = 6000


# ============================================================================
# CONFIG
# ============================================================================
#
# The ACSM "physically active" criterion, the vigorous-intensity NPS
# threshold, the Shenandoah difficulty formula, and the ACSM preparticipation
# gate itself now live in acsm_gate.py (imported above) so training and
# serving can never apply different gate logic.

# --- Terrain multiplier ------------------------------------------------------
# SOURCE: fitted against 28 Philippine mountains with published
#         PinoyMountaineer difficulty ratings, Spearman rho 0.859. See
#         acsm_gate.py's [PM] reference and MODEL.md for the calibration
#         caveat (fitted and validated on the same 28-mountain sample).
# NOTE: [NPS] formula has NO terrain term. This is a local adaptation for
#       Philippine trails and must be disclosed as such in Chapter 3.
TERRAIN_MULTIPLIER = {1: 1.00, 2: 1.15, 3: 1.35, 4: 1.60}  # Walking / Hiking / Scrambling / Simple Climbing

# --- Readiness component weights ---------------------------------------------
# SOURCE: PENDING EXPERT ELICITATION (organizer session, Sept 2026)
# Health is deliberately ABSENT here. Per [ACSM], health status acts as a
# clearance gate, not as an additive term in a fitness score.
W_EXPERIENCE = 0.45
W_FITNESS    = 0.30
W_GEAR       = 0.15
W_BMI        = 0.10

# --- Capacity mapping ---------------------------------------------------------
# Maps readiness in [0,1] onto the maximum (plain, un-adjusted) NPS
# difficulty rating the participant is estimated to handle.
# SOURCE: PENDING EXPERT ELICITATION
CAPACITY_MIN = 60.0
CAPACITY_MAX = 230.0

# --- Decision thresholds ------------------------------------------------------
# ratio = adjusted_trail_demand / estimated_capacity
# SOURCE: PENDING EXPERT ELICITATION
RATIO_GOOD_MATCH      = 0.75
RATIO_NOT_RECOMMENDED = 1.30

# --- Self-report measurement error --------------------------------------------
# Participants misjudge their own fitness and experience. Modelled as Gaussian
# noise on the readiness estimate rather than on the final decision boundary.
# SOURCE: PENDING - justify in Chapter 3 as self-report measurement error.
READINESS_NOISE_SD = 0.05


# ============================================================================
# FORM OPTION ENCODERS  (must match Views/Assessment/Form.cshtml exactly)
# ============================================================================

EXERCISE_FREQUENCY = {
    "I do not exercise / Sedentary": 0,
    "1 to 2 times per week":         1,
    "3 to 4 times per week":         2,
    "5 or more times per week":      3,
}

CARDIO_ENDURANCE = {          # Q7 - buckets corrected to align with ACSM 30-min rule
    "Less than 15 minutes":  0,
    "15 to 29 minutes":      1,
    "30 to 60 minutes":      2,
    "More than 60 minutes":  3,
}

EXERCISE_CONSISTENCY = {      # NEW Q - required by the ACSM 3-month criterion
    "Less than 1 month":  0,
    "1 to 2 months":      1,
    "3 months or more":   2,
}

MOUNTAINS_CLIMBED = {
    "This will be my first time / First-timer":  0,
    "1 to 3 mountains / Beginner":               1,
    "4 to 10 mountains / Intermediate":          2,
    "More than 10 mountains / Experienced":      3,
}

RECENCY_OF_HIKE = {
    "I have never climbed a mountain before": 0,
    "More than 1 year ago":                   1,
    "Within the past 4 to 12 months":         2,
    "Within the past 1 to 3 months":          3,
}

TRAIL_DIFFICULTY_COMPLETED = {
    "None / I have never climbed before":      0,
    "Minor day hikes only":                    1,
    "Major hikes with steep assault sections": 2,
    "Multi-day or overnight expeditions":      3,
}

GEAR_ITEMS = ["Enough water", "Trail food", "First aid kit", "Flashlight",
              "Whistle", "Raincoat", "Navigation tool", "Proper hiking shoes"]

# Q4 - expanded so the ACSM signs/symptoms axis is actually covered.
MEDICAL_CONDITIONS = {
    "Asthma / lung-related condition":                    "asthma",
    "Hypertension / heart-related condition":             "cvd",
    "Joint or knee injury":                               "joint",
    "Vertigo / frequent dizziness":                       "symptom",
    "Chest pain or discomfort during activity":           "symptom",  # NEW [ACSM]
    "Shortness of breath at rest or with mild exertion":  "symptom",  # NEW [ACSM]
}


# ============================================================================
# PARTICIPANT GENERATION  (correlated, not independent)
# ============================================================================

def generate_participants(n):
    """
    Features are drawn from a shared latent 'outdoor conditioning' factor so
    that profiles are internally coherent. Independent sampling produced
    participants that do not exist (e.g. sedentary expert mountaineers).
    """
    latent = RNG.normal(0, 1, n)                       # outdoor conditioning
    age = np.clip(RNG.normal(31, 9, n), 18, 60).round().astype(int)

    def tiered(shift, n_levels):
        z = latent * 0.85 + RNG.normal(0, 0.75, n) + shift
        q = np.quantile(z, np.linspace(0, 1, n_levels + 1)[1:-1])
        return np.digitize(z, q)

    freq        = tiered(0.00, 4)
    cardio      = tiered(-0.10, 4)
    consistency = tiered(0.10, 3)
    mountains   = tiered(0.00, 4)
    recency     = tiered(-0.05, 4)
    hardest     = tiered(-0.15, 4)

    # A first-timer cannot have hiked recently or completed a hard trail.
    # Mirrors the dependency logic already enforced in Form.cshtml.
    first_timer = mountains == 0
    recency = np.where(first_timer, 0, np.maximum(recency, 1))
    hardest = np.where(first_timer, 0, np.maximum(hardest, 1))

    # Gear correlates with experience; each item is then drawn individually.
    gear_p = np.clip(0.42 + 0.16 * mountains + 0.05 * latent, 0.05, 0.97)
    gear = (RNG.random((n, len(GEAR_ITEMS))) < gear_p[:, None]).astype(int)

    # BMI: better-conditioned participants trend toward the healthy range.
    bmi = np.clip(RNG.normal(24.5 - 1.1 * latent, 3.4, n), 15.0, 45.0)

    # Health conditions. Prevalence rises with age, falls with conditioning.
    def condition(base, age_slope, latent_slope=-0.20):
        p = np.clip(base + age_slope * (age - 30) / 30 + latent_slope * latent * base, 0.005, 0.60)
        return (RNG.random(n) < p).astype(int)

    has_asthma = condition(0.070, 0.010)
    has_cvd    = condition(0.055, 0.075)
    has_joint  = condition(0.080, 0.045)
    vertigo    = condition(0.035, 0.015)
    chest_pain = condition(0.020, 0.030)
    sob        = condition(0.025, 0.030)

    return pd.DataFrame({
        "age": age,
        "bmi": bmi.round(1),
        "exercise_frequency_score": freq,
        "continuous_cardio_duration_score": cardio,
        "exercise_consistency_score": consistency,
        "hiking_experience_score": mountains,
        "last_hike_recency_score": recency,
        "hardest_trail_completed_score": hardest,
        "gear_score": gear.sum(axis=1),
        "has_asthma": has_asthma,
        "has_cvd": has_cvd,
        "has_joint_knee_injury": has_joint,
        "has_cvd_symptoms": np.maximum.reduce([vertigo, chest_pain, sob]),
    })


def generate_trails(n):
    distance = np.clip(RNG.gamma(3.4, 1.7, n), 1.5, 30.0)
    elevation = np.clip(RNG.gamma(2.7, 230.0, n), 60.0, 2600.0)
    # Class 4 (Simple Climbing) events are genuinely rare - 6% keeps roughly
    # 360 examples at N_SAMPLES=6000, enough to learn from without distorting
    # the overall terrain distribution.
    terrain = RNG.choice([1, 2, 3, 4], n, p=[0.40, 0.36, 0.18, 0.06])
    rating = shenandoah_rating(distance, elevation)
    duration = (distance / 1.60934) / nps_pace_mph(rating)
    return pd.DataFrame({
        "trail_distance_km": distance.round(1),
        "trail_elevation_gain_m": elevation.round(0),
        "trail_terrain_type": terrain,
        "trail_shenandoah_score": rating.round(1),
        "trail_estimated_duration_hr": duration.round(1),
    })


# ============================================================================
# READINESS  ->  CAPACITY
# ============================================================================

def bmi_component(bmi):
    """SOURCE: [WHO] adult BMI categories. Penalty magnitude PENDING EXPERT."""
    return np.select(
        [bmi < 16.0, bmi < 18.5, bmi < 25.0, bmi < 30.0, bmi < 35.0],
        [0.30,       0.70,       1.00,       0.80,       0.50],
        default=0.30,
    )


def compute_readiness(p):
    experience = (p.hiking_experience_score / 3 * 0.45
                  + p.last_hike_recency_score / 3 * 0.30
                  + p.hardest_trail_completed_score / 3 * 0.25)
    fitness = (p.exercise_frequency_score / 3 * 0.45
               + p.continuous_cardio_duration_score / 3 * 0.35
               + p.exercise_consistency_score / 2 * 0.20)
    gear = p.gear_score / len(GEAR_ITEMS)
    bmi = bmi_component(p.bmi)

    readiness = (W_EXPERIENCE * experience + W_FITNESS * fitness
                 + W_GEAR * gear + W_BMI * bmi)
    readiness = readiness + RNG.normal(0, READINESS_NOISE_SD, len(p))
    return np.clip(readiness, 0.0, 1.0)


# ============================================================================
# BUILD
# ============================================================================
# The ACSM preparticipation gate (is_acsm_physically_active, apply_acsm_gate,
# RANK/UNRANK) is imported from acsm_gate.py above.

def build(n=N_SAMPLES):
    df = pd.concat([generate_participants(n), generate_trails(n)], axis=1)

    readiness = compute_readiness(df)
    capacity = CAPACITY_MIN + readiness * (CAPACITY_MAX - CAPACITY_MIN)
    demand = df.trail_shenandoah_score * df.trail_terrain_type.map(TERRAIN_MULTIPLIER)
    ratio = demand / capacity

    # Diagnostic only (not a model feature) - nps_band() now expects the
    # terrain-adjusted rating, which isn't known until trail_terrain_type is
    # merged in above, so this can't live in generate_trails() any more.
    df["trail_nps_band"] = nps_band(demand)

    base = np.select(
        [ratio <= RATIO_GOOD_MATCH, ratio <= RATIO_NOT_RECOMMENDED],
        ["Good Match", "Borderline"],
        default="Not Recommended",
    )

    label, clearance, reason = apply_acsm_gate(base, df, demand.to_numpy())

    df["estimated_capacity_nps"] = capacity.round(1)
    df["adjusted_trail_demand"] = demand.round(1)
    df["demand_capacity_ratio"] = ratio.round(3)
    df["label_before_gate"] = base
    df["suitability_label"] = label
    df["medical_clearance_required"] = clearance
    df["gate_reason"] = reason
    return df


FEATURE_COLUMNS = [
    "bmi",
    "exercise_frequency_score", "continuous_cardio_duration_score",
    "exercise_consistency_score",
    "hiking_experience_score", "last_hike_recency_score",
    "hardest_trail_completed_score",
    "gear_score",
    "has_asthma", "has_cvd", "has_joint_knee_injury", "has_cvd_symptoms",
    "trail_shenandoah_score", "trail_terrain_type",
]

if __name__ == "__main__":
    df = build()
    df.to_csv("trailguard_synthetic_dataset_v2.csv", index=False)
    print(f"Rows: {len(df)}   Features: {len(FEATURE_COLUMNS)}")
    print("\nLabel distribution:")
    print(df.suitability_label.value_counts())
    print("\nGate activity:")
    print(f"  labels lowered by the ACSM gate : "
          f"{(df.label_before_gate != df.suitability_label).mean()*100:.1f}%")
    print(f"  medical clearance required      : "
          f"{df.medical_clearance_required.mean()*100:.1f}%")
    print("\nPinoyMountaineer difficulty tier distribution:")
    print(df.trail_nps_band.value_counts())
