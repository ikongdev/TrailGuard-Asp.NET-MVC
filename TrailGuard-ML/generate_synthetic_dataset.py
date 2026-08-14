import numpy as np
import pandas as pd

RNG = np.random.default_rng(42)
N_ROWS = 2000

distance_km = RNG.uniform(1.5, 20.0, N_ROWS)
elevation_gain_m = RNG.uniform(50, 2000, N_ROWS)
terrain_type = RNG.choice([1, 2, 3], size=N_ROWS, p=[0.25, 0.55, 0.20])
estimated_duration_hr = (
    distance_km * 0.35 + elevation_gain_m / 400 + RNG.normal(0, 0.5, N_ROWS)
)
estimated_duration_hr = np.clip(estimated_duration_hr, 0.5, 14)

age = RNG.integers(18, 60, N_ROWS)
height_cm = RNG.normal(163, 8, N_ROWS).clip(145, 190)
weight_kg = RNG.normal(65, 12, N_ROWS).clip(40, 110)
bmi = weight_kg / ((height_cm / 100) ** 2)

has_asthma = RNG.choice([0, 1], N_ROWS, p=[0.90, 0.10])
has_hypertension_heart_condition = RNG.choice([0, 1], N_ROWS, p=[0.88, 0.12])
has_joint_knee_injury = RNG.choice([0, 1], N_ROWS, p=[0.85, 0.15])
has_vertigo = RNG.choice([0, 1], N_ROWS, p=[0.93, 0.07])

exercise_frequency_score = RNG.choice([0, 1, 2, 3], N_ROWS, p=[0.20, 0.30, 0.30, 0.20])
exercise_type_category = RNG.choice([1, 2, 3], N_ROWS, p=[0.30, 0.40, 0.30])
continuous_cardio_duration_score = RNG.choice([0, 1, 2, 3], N_ROWS, p=[0.25, 0.30, 0.30, 0.15])

hiking_experience_score = RNG.choice([0, 1, 2, 3], N_ROWS, p=[0.30, 0.30, 0.25, 0.15])
last_hike_recency_score = RNG.choice([0, 1, 2, 3], N_ROWS, p=[0.25, 0.25, 0.30, 0.20])
hardest_trail_completed_score = RNG.choice([0, 1, 2, 3], N_ROWS, p=[0.25, 0.30, 0.30, 0.15])

gear_items = [
    "gear_water", "gear_trail_food", "gear_first_aid_medicine",
    "gear_flashlight_headlamp", "gear_whistle", "gear_raincoat_poncho",
    "gear_navigation", "gear_proper_shoes",
]
gear_data = {}
for item in gear_items:
    p_yes = 0.55 if item in ("gear_navigation", "gear_whistle") else 0.75
    gear_data[item] = RNG.choice([0, 1], N_ROWS, p=[1 - p_yes, p_yes])

gear_score = sum(gear_data.values())

elevation_gain_ft = elevation_gain_m / 0.3048
distance_miles = distance_km / 1.609
shenandoah_rating = np.sqrt(elevation_gain_ft * 2 * distance_miles)

terrain_adjustment = {1: 1.0, 2: 1.10, 3: 1.25}
terrain_multiplier = np.array([terrain_adjustment[t] for t in terrain_type])
trail_demand_raw = shenandoah_rating * terrain_multiplier

trail_demand_score = (trail_demand_raw - trail_demand_raw.min()) / (
    trail_demand_raw.max() - trail_demand_raw.min()
)

fitness_component = (
    exercise_frequency_score + continuous_cardio_duration_score
) / 6.0

experience_component = (
    hiking_experience_score + last_hike_recency_score + hardest_trail_completed_score
) / 9.0

gear_component = gear_score / 8.0

bmi_component = np.where(
    (bmi >= 18.5) & (bmi <= 24.9),
    1.0,
    np.where(
        (bmi >= 17.0) & (bmi < 18.5) | (bmi > 24.9) & (bmi <= 29.9),
        0.6,
        0.3,
    ),
)

health_penalty = (
    has_asthma * 0.10
    + has_hypertension_heart_condition * 0.15
    + has_joint_knee_injury * 0.15
    + has_vertigo * 0.10
)
health_component = np.clip(1.0 - health_penalty, 0, 1)

participant_readiness_score = (
    0.35 * experience_component
    + 0.25 * fitness_component
    + 0.20 * gear_component
    + 0.10 * bmi_component
    + 0.10 * health_component
)

z_demand = (trail_demand_raw - trail_demand_raw.mean()) / trail_demand_raw.std()
z_readiness = (
    participant_readiness_score - participant_readiness_score.mean()
) / participant_readiness_score.std()

gap = z_readiness - z_demand
gap_noisy = gap + RNG.normal(0, 0.15, N_ROWS)

UPPER_THRESHOLD = 0.62
LOWER_THRESHOLD = -0.62

conditions = [
    gap_noisy >= UPPER_THRESHOLD,
    (gap_noisy < UPPER_THRESHOLD) & (gap_noisy > LOWER_THRESHOLD),
    gap_noisy <= LOWER_THRESHOLD,
]
choices = ["Good Match", "Borderline", "Not Recommended"]
suitability_label = np.select(conditions, choices, default="Borderline")

df = pd.DataFrame({
    "age": age,
    "height_cm": np.round(height_cm, 2),
    "weight_kg": np.round(weight_kg, 2),
    "bmi": np.round(bmi, 2),
    "has_asthma": has_asthma,
    "has_hypertension_heart_condition": has_hypertension_heart_condition,
    "has_joint_knee_injury": has_joint_knee_injury,
    "has_vertigo": has_vertigo,
    "exercise_frequency_score": exercise_frequency_score,
    "exercise_type_category": exercise_type_category,
    "continuous_cardio_duration_score": continuous_cardio_duration_score,
    "hiking_experience_score": hiking_experience_score,
    "last_hike_recency_score": last_hike_recency_score,
    "hardest_trail_completed_score": hardest_trail_completed_score,
    **gear_data,
    "gear_score": gear_score,
    "trail_distance_km": np.round(distance_km, 2),
    "trail_elevation_gain_m": np.round(elevation_gain_m, 1),
    "trail_terrain_type": terrain_type,
    "trail_estimated_duration_hr": np.round(estimated_duration_hr, 2),
    "trail_demand_score": np.round(trail_demand_score, 4),
    "participant_readiness_score": np.round(participant_readiness_score, 4),
    "suitability_label": suitability_label,
})

output_path = "trailguard_synthetic_dataset.csv"
df.to_csv(output_path, index=False)

print(f"Saved {len(df)} rows to {output_path}\n")
print("Class distribution:")
print(df["suitability_label"].value_counts())
print("\nClass distribution (%):")
print((df["suitability_label"].value_counts(normalize=True) * 100).round(2))