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
Shenandoah Hiking Difficulty rating. NPS publishes its own bands for it:
under 50 Easiest, 50–100 Moderate, 100–150 Moderately Strenuous, 150–200
Strenuous, above 200 Very Strenuous.

v2 gives the model the raw rating directly as a feature, instead of making
it approximate the square root from separate distance and elevation inputs.
This is the principal cause of the accuracy improvement from 80.50% to
91.42%.

But NPS's own bands were not carried over for what gets *shown* to a
participant. They're calibrated for Shenandoah National Park, and Philippine
trails run harder: of the 28 Philippine mountains checked against them, 68%
already exceed NPS's own top threshold. A scale that can't tell most of the
Philippines' own mountains apart from one another isn't a scale worth
showing a Filipino hiker.

### Validated against a scale hikers already trust — but not its rule

Before replacing NPS's bands with something else, the rating needed
checking against a scale Filipino hikers actually use: PinoyMountaineer's
published 1–9 difficulty ratings for those same 28 well-known Philippine
mountains. The NPS rating, adjusted by the trail's terrain class (below),
correlates with PM's published rating at Spearman rho 0.859 — strong enough
to build four PM-flavored tiers on top of it: Easy, Minor Climb, Major
Climb, and Major Climb — Difficult, each cross-referenced to the PM level
range a hiker would recognize (1–2/9 through 7–9/9).

What wasn't adopted is PM's own written *rule* for reaching that rating —
duration plus trail class. Applied directly against the same 28 mountains,
it reproduced PM's own published ratings only half the time. The reason is
specific: multi-day status in the Philippines often reflects logistics, not
difficulty. Mt. Pulag via Ambangeg is a two-day itinerary because groups
camp overnight for the sunrise, not because the trail is technically harder
than a comparable one-day hike — PM rates it a mild 3/9. A rule that reads
"two days" as "harder" would have gotten that one wrong. The NPS-based
rating, calibrated against PM's *outcomes* rather than copying PM's
*method*, doesn't make that mistake — though it's calibrated and tested on
the same 28 mountains, which is its own limitation (see MODEL.md).

### Terrain became its own dimension — Trail Class 1 to 4

PinoyMountaineer's Trail Class scale runs 1 to 6; TrailGuard uses the first
four — Walking, Hiking, Scrambling, and Simple Climbing. Classes 5 and 6 are
technical and aid climbing, and no organized hiking event puts general
participants on those.

Class 4 wasn't in the model from the start. It was added in a later retrain,
because a three-class scheme has no way to represent a trail like
Mt. Guiting-Guiting or Mt. Mantalingajan — genuinely exposed routes with
fixed ropes, where a fall is more than a bad afternoon. Excluding them
wasn't a simplification; it was a gap in what the system could assess at
all.

The multiplier attached to each class was fitted, not chosen. An earlier
build guessed 1.00 / 1.10 / 1.25 for three classes, with no source beyond a
hunch. The current multipliers — 1.00 / 1.15 / 1.35 / 1.60, Walking through
Simple Climbing — come from fitting the adjusted NPS rating against the same
28 Philippine mountains' published PM ratings (the rho 0.859 above). Holding
everything else about a participant fixed and moving only the trail from
Class 1 to Class 4 changes the model's label 38.17% of the time — a large
effect for one feature, and the expected result of a multiplier that now
spans 1.00 to 1.60 rather than topping out at 1.25.

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

Not because more data is better. Because the gate rules are rare, and adding
Class 4 made the rarest terrain rarer still. At 2,000 rows, two of the four
rules would have had roughly one example each in the test split —
unmeasurable. At 6,000, each rule now matches between 42 and 493 rows.

This is worth stating carefully: 6,000 synthetic rows do not contain three
times more knowledge than 2,000. The labels come from the same formula. What
the larger dataset buys is enough examples per rare subgroup to measure
whether the model handles it, and nothing more.

### The rule-based fallback was removed

For a while, if the ML service was unreachable, the system fell back to a
rule-based heuristic, `GetResult()`, so a participant always got some
answer. That fallback no longer exists.

`GetResult()` predates the NPS/ACSM work above — it had its own notion of
trail demand, one that was never reconciled with the Shenandoah rating or
the ACSM gate. In testing, the same assessment could come back Good-Match
from the model and Borderline from the fallback. A fallback that disagrees
with the primary system isn't resilience; it's a second, unvalidated opinion
that happens to run when the first one can't be reached — and it bypasses
the ACSM gate entirely, so a participant reporting a genuine cardiovascular
symptom could get a confident-looking result that skipped the one check
built specifically to catch that.

If the ML service is down now, the system says so and asks the participant
to try again. No result is produced. That's deliberate: producing no answer
is safer than producing a second one that might disagree with the first.

---

## 5. Results

v1 reached 80.50% accuracy against its own rule; v2 reaches 91.42%, mostly
on the strength of giving the model the NPS rating directly instead of
making it approximate one. Fidelity to the rule engine — how often the
model's prediction matches what the deterministic formula would have said —
moved from 80.50% to 95.22%.

The more consequential change doesn't show up in either number. In v1, all
four health conditions turned on together changed the predicted label for
1.40% of participants; in v2, the same test changes 76.60%. Health went from
the weakest signal in the system to the strongest.

The fabricated-explanation problem also went to zero: no v2 feature is ever
irrelevant to the label it's shown next to, so no participant is told a
feature "reduced their result" when it never touched the outcome.

**One metric regressed, and it's why the ACSM gate exists as an independent
check.** The v1 model never predicted Good Match for a case the rules called
Not Recommended. The v2 model, taken alone, does — 0.83 times per 1,000 test
rows. That's a real regression in the model by itself, which is exactly why
the gate runs as a separate step after the model rather than being trusted
to have learned the rule: with the gate applied, the rate returns to zero.

(Full tables — confusion matrices, per-rule gate statistics, monotonicity
tests — are in `MODEL.md`. This section is the shape of the change, not the
record of it.)

---

## 6. What is still unresolved

**The labels are still synthetic.** v2 fixed the sourcing and the safety
inversion. It did not remove the circularity: the model still learns from a
rule the development team wrote — a better-sourced rule than v1's, but still
not an observation of a real outcome.

That circularity is what expert validation exists to test, and it's no
longer hypothetical. Two blind rating rounds are complete. A practising
hiking organizer rated 100 generated profiles — 60 in the first round, 40 in
the second — against trail specifications alone, with no system output
attached, and the system's own label was withheld until each round was
returned. Combined agreement: quadratic weighted kappa 0.555. That is
calibration evidence against one rater's judgment of synthetic profiles, not
independent statistical validation, and it says nothing yet about real
hikers.

**The finding that matters isn't the kappa — it's where the two disagree.**
Across all 100 profiles, the organizer chose Borderline 13 times; the system
chose it 35 times, independently in both rounds. The organizer commits to a
call; the system hedges toward the middle category more than twice as
often.

That points at two specific numbers, not at the model. The decision
thresholds that separate Good Match, Borderline, and Not Recommended sit at
0.75 and 1.30 — a 0.55-wide band of demand-to-capacity ratio that counts as
Borderline before anything else is considered. And `CAPACITY_MAX` is set to
230, meaning even a participant scoring the theoretical maximum on every
readiness component tops out at a capacity of 230 — while six of the eight
seeded events demand more than 172, the point past which no participant can
reach Good Match on them at all, no matter how prepared they are. A wide
Borderline band combined with a capacity ceiling most real events exceed is
a plausible, structural explanation for why the system hedges where a human
organizer commits — and it's a testable one, not a guess about the model's
behavior.

Both the thresholds and the capacity range are marked
`PENDING EXPERT ELICITATION` in the generator, exactly as they were before
this round of validation — alongside the readiness component weights and
the joint-injury rule, which this round's findings didn't bear on directly.
What's changed is that there is now a specific, measured symptom pointing at
the thresholds and the capacity ceiling, rather than a general note that
they're unsourced. A third validation round, using hiker–trail scenarios
instead of individual profiles, is planned once those numbers have an
actual source.
