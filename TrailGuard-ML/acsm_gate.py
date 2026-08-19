"""
TrailGuard - ACSM Preparticipation Gate (shared module)

Single source of truth for the NPS Shenandoah difficulty formula and the
ACSM 2015 preparticipation screening gate. Both training
(generate_synthetic_dataset.py) and serving (main.py) import from here so
the gate logic can never diverge between what was validated offline and
what is actually applied to a live prediction.

REFERENCES
  [NPS]   National Park Service, Shenandoah National Park.
          "How to Determine Hiking Difficulty."
          Rating = sqrt(elevation_gain_ft * 2 * distance_mi)
          Bands: <50 Easiest | 50-100 Moderate | 100-150 Moderately Strenuous
                 | 150-200 Strenuous | >200 Very Strenuous
          Pace bands: 1.5 / 1.4 / 1.3 / 1.2 mph respectively.
  [ACSM]  Riebe D, Franklin BA, Thompson PD, Garber CE, Whitfield GP,
          Magal M, Pescatello LS. "Updating ACSM's Recommendations for
          Exercise Preparticipation Health Screening."
          Med Sci Sports Exerc. 2015;47(11):2473-2479.
"""

import numpy as np

# --- ACSM "physically active" criterion -------------------------------------
# SOURCE: [ACSM] - planned, structured physical activity of at least 30 min
#         at moderate intensity, at least 3 d/wk, for at least the last 3 months.
ACSM_MIN_FREQUENCY_SCORE   = 2   # "3 to 4 times per week"
ACSM_MIN_CARDIO_SCORE      = 2   # "30 to 60 minutes"
ACSM_MIN_CONSISTENCY_SCORE = 2   # "3 months or more"

# --- Exercise intensity threshold -------------------------------------------
# SOURCE: [ACSM] separates moderate- from vigorous-intensity exercise when
#         deciding on medical clearance. [NPS] band boundary at 100 is used as
#         the proxy for the moderate/vigorous crossover.
# STATUS: PENDING EXPERT VALIDATION - the mapping of an NPS band onto an ACSM
#         intensity class is our adaptation, not an ACSM statement.
VIGOROUS_INTENSITY_NPS_THRESHOLD = 100.0


# ============================================================================
# TRAIL DEMAND  -  [NPS] Shenandoah Hiking Difficulty
# ============================================================================

def shenandoah_rating(distance_km, elevation_gain_m):
    """Official NPS Shenandoah difficulty rating. Inputs metric, formula imperial.

    Mirrored in C# by Services/DifficultyCalculator.cs (ComputeRating/LabelFor/
    PaceMph). These two files must be changed together - the C# side is what
    Views/Event and TrailController show and recompute, this side is what /predict
    returns as nps_score/nps_band. A rating computed two different ways here and
    there produces exactly the report-page-vs-event-page disagreement this pairing
    exists to prevent.
    """
    elevation_ft = elevation_gain_m * 3.28084
    distance_mi = distance_km / 1.60934
    return np.sqrt(elevation_ft * 2.0 * distance_mi)


def nps_band(rating):
    """Published NPS descriptor for a difficulty rating."""
    return np.select(
        [rating < 50, rating < 100, rating < 150, rating < 200],
        ["Easiest", "Moderate", "Moderately Strenuous", "Strenuous"],
        default="Very Strenuous",
    )


def nps_pace_mph(rating):
    """Published NPS average pace band, used for duration estimation."""
    return np.select(
        [rating < 50, rating < 100, rating < 150],
        [1.5, 1.4, 1.3],
        default=1.2,
    )


# ============================================================================
# ACSM PREPARTICIPATION GATE
# ============================================================================

def is_acsm_physically_active(p):
    """SOURCE: [ACSM] - >=30 min moderate, >=3 d/wk, for >=3 months."""
    return ((p.exercise_frequency_score >= ACSM_MIN_FREQUENCY_SCORE)
            & (p.continuous_cardio_duration_score >= ACSM_MIN_CARDIO_SCORE)
            & (p.exercise_consistency_score >= ACSM_MIN_CONSISTENCY_SCORE))


RANK = {"Not Recommended": 0, "Borderline": 1, "Good Match": 2}
UNRANK = {v: k for k, v in RANK.items()}


def apply_acsm_gate(base_label, p, demand):
    """
    Caps the score-based label according to the 2015 ACSM screening algorithm.
    Returns (gated_label, medical_clearance_required, gate_reason).

    The gate can only ever LOWER a label. It never promotes.
    """
    rank = np.array([RANK[l] for l in base_label])
    clearance = np.zeros(len(p), dtype=int)
    reason = np.array([""] * len(p), dtype=object)

    active = is_acsm_physically_active(p).to_numpy()
    vigorous = demand >= VIGOROUS_INTENSITY_NPS_THRESHOLD

    def cap(mask, ceiling, why, needs_clearance=True):
        hit = mask & (rank > RANK[ceiling])
        rank[hit] = RANK[ceiling]
        reason[hit] = why
        if needs_clearance:
            clearance[mask] = 1

    # RULE 1 [ACSM]: signs or symptoms suggestive of cardiovascular disease
    # require medical clearance regardless of intensity or activity level.
    cap(p.has_cvd_symptoms.to_numpy() == 1, "Not Recommended",
        "ACSM: signs/symptoms present - medical clearance required")

    # RULE 2 [ACSM]: physically INACTIVE with known CVD must obtain clearance
    # before beginning exercise of ANY intensity.
    cap((p.has_cvd.to_numpy() == 1) & ~active, "Not Recommended",
        "ACSM: known CVD and physically inactive - clearance required")

    # RULE 3 [ACSM]: physically ACTIVE with known CVD may continue moderate
    # exercise, but requires clearance before VIGOROUS exercise.
    cap((p.has_cvd.to_numpy() == 1) & active & vigorous, "Borderline",
        "ACSM: known CVD, vigorous-intensity trail - clearance required")

    # RULE 4 [ACSM]: pulmonary disease is NOT an automatic referral.
    #                Asthma therefore produces an advisory, never a gate.

    # RULE 5: musculoskeletal injury on steep or technical terrain.
    # SOURCE: PENDING EXPERT ELICITATION - no ACSM basis.
    cap((p.has_joint_knee_injury.to_numpy() == 1)
        & ((demand >= 150) | (p.trail_terrain_type.to_numpy() == 3)),
        "Borderline", "Joint/knee injury on steep or technical terrain",
        needs_clearance=False)

    return np.array([UNRANK[r] for r in rank]), clearance, reason
