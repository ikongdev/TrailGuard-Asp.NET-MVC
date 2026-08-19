# Why the model was rebuilt — v1 to v2

A narrative explanation of what was wrong with v1, what changed in v2, and
what is still unresolved. Written to be defensible under questioning.

---

## 1. The problem that started this

TrailGuard v1 reported 80.50% accuracy. That number looked acceptable until
we asked what it was accuracy *at*.

The training labels were not observations of anything. They were produced by
a formula the development team wrote: a weighted sum of participant
readiness, minus a z-scored measure of trail demand, with Gaussian noise
added and thresholds applied at ±0.62. The model was then trained to predict
the output of that formula.

So 80.50% did not mean the system was right 80.50% of the time. It meant the
model reproduced the team's own rule 80.50% of the time — a rule already
present in the codebase, which could be called directly at no cost and with
no error.

Measuring the ceiling made this concrete. Recomputing the formula without the
added noise reproduces the stored labels 94.00% of the time; that is the most
any model could recover. The v1 model reached 80.50%. The 13.5-point gap was
not learning. It was a decision tree failing to approximate
`sqrt(elevation × 2 × distance)`, a smooth multiplicative surface that
axis-aligned splits handle badly.

The question a panel would ask: *if you wrote the labels, what did the model
learn that you did not already know?* v1 had no answer.

---

## 2. Health conditions barely worked

This was the serious defect, and it was invisible in the accuracy figure.

Setting all four health flags — asthma, hypertension or heart condition,
joint or knee injury, vertigo — on all 2,000 participants changed the
predicted label for **1.40%** of them. For 98.6% of people, declaring a heart
condition made no difference to the safety recommendation.

For comparison, on the same model:

| Change | Labels changed |
|---|---|
| All four health conditions turned on | 1.40% |
| Age changed from 18 to 59 | 2.00% |
| All eight gear items removed | 17.70% |

The system responded more to age — which was never part of the label formula
— than to declared cardiac disease. Forgetting a flashlight mattered roughly
twelve times more than having vertigo.

The model was not at fault. It had learned the data correctly. The defect was
in the generator, where health carried the smallest weight of any component:

```
health:      0.10 × (1 − penalty),  penalty capped at 0.50  →  swing 0.05
bmi:         0.10 × (0.30 … 1.00)                           →  swing 0.07
gear:        0.20 × (0 … 1)                                 →  swing 0.20
fitness:     0.25 × (0 … 1)                                 →  swing 0.25
experience:  0.35 × (0 … 1)                                 →  swing 0.35
```

Health had the smallest possible influence on a system whose stated purpose
is hiking safety.

---

## 3. The explanations shown to users were partly invented

v1 displayed a "Why This Result?" panel driven by SHAP values — the top ten
features by influence on that participant's prediction.

Four of the 27 features were never used to generate any label: `age`,
`exercise_type_category`, `height_cm`, and `weight_kg`. The last two
contributed only through `bmi`; the first two contributed nothing at all.

Because XGBoost will still split on noise, SHAP still assigned them
influence. Measured across all 2,000 participants:

- **47.4%** were shown at least one of these features in the top ten
- **27.5%** were shown one in the top three
- they held **5.2%** of total SHAP magnitude

A participant could therefore be told *"Weight — Reduced your result"* about
a variable that had no role in the outcome. That is a fabricated explanation
presented as a safety finding.

---

## 4. What changed in v2

### Trail demand now uses a published standard

v1 z-scored the trail demand, which made an arbitrary quantity out of
something that already has an official scale. The formula v1 used —
`sqrt(elevation_ft × 2 × distance_mi)` — is the National Park Service's
Shenandoah Hiking Difficulty rating, with published bands: under 50 Easiest,
50–100 Moderate, 100–150 Moderately Strenuous, 150–200 Strenuous, above 200
Very Strenuous.

v2 uses the raw rating and those bands. The output is now a statement a
person can check: *this trail rates 160 on the NPS scale; your profile
supports approximately 128; that is 1.25 times your estimated capacity.*

The rating was also given to the model directly as a feature. The model no
longer has to approximate the square root from separate distance and
elevation inputs. This is the principal cause of the accuracy improvement
from 80.50% to 91.75%.

### Health became a gate rather than a weight

The 2015 ACSM preparticipation screening algorithm (Riebe et al.) is based on
three factors: the individual's current level of physical activity, the
presence of signs or symptoms or known cardiovascular, metabolic, or renal
disease, and the desired exercise intensity.

The assessment form already collected all three. The error was structural:
ACSM treats health as a clearance gate, not as a term in a fitness score.
Under ACSM, a physically inactive person with known cardiovascular disease
requires medical clearance before exercise of any intensity — that is a gate,
not a 0.10 weight.

v2 implements four rules:

1. Signs or symptoms present → clearance required regardless of intensity
2. Known CVD and physically inactive → clearance required, cap at
   Not Recommended
3. Known CVD, physically active, vigorous-intensity trail → clearance
   required, cap at Borderline
4. Joint or knee injury on steep or technical terrain → cap at Borderline

Asthma deliberately triggers no gate. ACSM states that pulmonary disease is
not an automatic referral, so treating it identically to cardiac disease
would be wrong.

The gate can only lower a label. It never raises one.

### The form was extended to match ACSM

Three changes were needed for the mapping to be exact rather than
approximate:

- The cardio duration option "15 to 30 minutes" straddled the ACSM threshold
  of 30 minutes. Someone at exactly 30 meets the criterion; someone at 16
  does not; both selected the same option. Rebucketed to "15 to 29 minutes"
  and "30 to 60 minutes".
- Chest pain and shortness of breath were added to the medical conditions
  question. ACSM screens on signs and symptoms, not only diagnosed disease,
  and v1 captured only one of them (dizziness).
- A question on how long the participant has exercised at their stated
  frequency was added. ACSM requires at least three months of consistency;
  v1 had no way to know this.

The gender question was removed. It was collected and never used anywhere in
the system, which is a data minimisation concern under RA 10173.

### Features reduced from 27 to 14

Dead features were removed. Collinear groups were collapsed: height and
weight into `bmi`, eight gear flags into `gear_score`, and distance,
elevation, and duration into `trail_shenandoah_score`. Two features were
added and two renamed.

The result is that every feature the model sees now has a role in producing
the label, so every SHAP explanation refers to something real. Measured
across all 6,000 participants: **0.0%** are shown a feature that never
affected the outcome, down from 47.4%.

### Participants became internally coherent

v1 sampled every feature independently. A 58-year-old with vertigo had
exactly the same chance of being an expert mountaineer as a 22-year-old
athlete. The model was learning from people who cannot exist.

v2 draws features from a shared latent conditioning factor, so experience,
fitness, gear, and BMI move together, and health conditions become more
prevalent with age. The first-timer dependency already enforced in the form
is mirrored in the generator.

### Monotonicity became a guarantee

v1 happened to be monotonic — more experience never produced a worse result —
but nothing enforced it. v2 declares monotonic constraints on 13 of the 14
features, so the property survives retraining. BMI is deliberately
unconstrained, since both extremes reduce readiness.

### The dataset grew to 6,000 rows

Not because more data is better. Because the gate rules are rare. At 2,000
rows, two of the four rules would have had roughly one example each in the
test split — unmeasurable. At 6,000, each rule has between 22 and 410 cases.

This is worth stating carefully: 6,000 synthetic rows do not contain three
times more knowledge than 2,000. The labels come from the same formula. What
the larger dataset buys is enough examples per rare subgroup to measure
whether the model handles it, and nothing more.

---

## 5. Results

| | v1 | v2 |
|---|---|---|
| Accuracy | 0.8050 | 0.9175 |
| Fidelity to the rule engine | 80.50% | 95.12% |
| Health conditions change the label | 1.40% | 76.60% |
| Fabricated features in explanations | 47.40% | 0.00% |
| Monotonicity | incidental | enforced |
| Features | 27 | 14 |

**One metric regressed.** The v1 model never predicted Good Match for a
participant the rules called Not Recommended. The v2 model does so once in
1,200 test rows — 0.83 per thousand against v1's zero.

This is why the ACSM gate is applied as an independent check after the model
rather than trusted to the model. Across the full 6,000-row dataset the v2
model alone produces two such cases; with the gate applied, zero.

The gate overrides the model on 0.20% of predictions overall. Broken down by
rule, it never needs to intervene on signs and symptoms (the model learned
that rule perfectly across all 410 cases), but intervenes on 32% of
joint-injury cases. That rule is the only one with no clinical source, and
it is the one the model fails to learn.

---

## 6. What is still unresolved

**The labels are still synthetic.** v2 fixed the sourcing and the safety
inversion. It did not remove the circularity. The model still learns from a
rule the team wrote.

What changed is the question a panel can ask. It is no longer *"why not just
use the formula?"* — it is *"how accurate is the formula?"* That question is
answerable, but only through independent expert validation.

**Several constants remain unsourced.** The readiness weights, terrain
multipliers, capacity range, decision thresholds, and joint-injury rule are
marked `PENDING EXPERT ELICITATION` in the generator. They are placeholders
carried over from v1, where they originated in a chat with an AI assistant
and had no basis at all.

An expert rating instrument has been prepared: 60 profiles for blind rating
by a practising organizer, with 40 held back for a second round. Agreement
will be reported as quadratic weighted kappa. Until that is returned, no
claim about real-world accuracy is supportable.
