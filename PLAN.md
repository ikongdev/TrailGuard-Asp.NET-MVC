# Active Plan — Assessment Form Data Integrity

Part of the UI/UX pass, but what turned up is mostly data integrity: the form lets participants submit answer combinations that are internally contradictory, and those answers feed straight into the ML model as features.

Read `DESIGN.md` first — its colour tokens, radius scale, and hover rules apply to the styling portion.

---

## Why This Matters

Questions 8, 9, and 10 map directly to `hiking_experience_score`, `last_hike_recency_score`, and `hardest_trail_completed_score` — three of the model's features. Question 4 maps to the four health flags.

Nothing currently stops a participant from answering "First-timer" for Q8 and "1–3 months ago" for Q9, or ticking both "Asthma" and "None of the above". Those combinations are impossible in reality and never appeared in the synthetic training data, so the model is being asked to score a profile it has no basis for.

Garbage in, confident-looking garbage out — and the confidence score makes that worse, because a nonsense input can still come back at 95%.

---

## Decisions Already Made

| Question | Decision |
|---|---|
| Q4 "None of the above" | **Mutually exclusive.** Ticking it clears and disables the four conditions; ticking any condition clears and disables it. |
| Q8 → Q9, Q10 dependency | **Auto-set and disable**, not validate-and-warn. There's no legitimate reason to answer these inconsistently, so the form shouldn't allow it in the first place. |
| Age range | **18–60**, matching the synthetic training data. |
| Height / weight ranges | Sensible bounds so BMI stays meaningful. |
| BMI computation | **No change** — the formula and WHO thresholds are already correct. |

### The Q8 dependency, precisely

**Q8 = "First-timer"** →
- Q9 auto-selects "Never climbed", other options disabled
- Q10 auto-selects "None", other options disabled

**Q8 = anything else** (Beginner / Intermediate / Experienced) →
- Q9's "Never climbed" is disabled; the other three are selectable
- Q10's "None" is disabled; the other three are selectable
- If "Never climbed" or "None" was already selected, clear it

Changing Q8 after the fact must re-apply the rule and clear anything now invalid. A participant who picks Experienced, answers Q9, then goes back to First-timer should not keep the old Q9 answer.

---

## Phase 1 — Question 4 Exclusivity

In `Views/Assessment/Form.cshtml`:

- Give the "None of the above" checkbox its own id
- On change of any condition checkbox: if now checked, uncheck and disable "None of the above"; if none remain checked, re-enable it
- On change of "None of the above": if checked, uncheck and disable all four conditions; if unchecked, re-enable them
- Disabled checkboxes should read as disabled — muted text, `cursor-not-allowed`, reduced opacity

The existing `validateStep` check for `medicalConditions` already requires at least one selection, so that stays as is.

---

## Phase 2 — Q8 / Q9 / Q10 Dependency

Same file. Wire a handler to Q8 (`mountainsClimbed`) that applies the rule above to Q9 (`recencyOfHike`) and Q10 (`trailDifficultyCompleted`).

Run it once on page load too, not just on change — a participant returning to a partially filled form should see the same state.

Disabled radio options need the same muted treatment as disabled checkboxes.

**Watch out:** `validateStep` checks radio groups for *any* checked option. Auto-selecting satisfies that automatically, which is fine — but make sure disabling doesn't accidentally leave a group with nothing selected, or step 2 becomes impossible to pass.

---

## Phase 3 — Input Ranges

| Field | Current | Change to |
|---|---|---|
| Age | `min="10" max="100"` | `min="18" max="60"` |
| Height | `min="100" max="250"` | `min="120" max="220"` |
| Weight | `min="30" max="200"` | `min="25" max="200"` |

`validateStep` currently only checks that a value is present, not that it's within range. Add a range check for these three, with a clear message naming which field is out of bounds — the browser's own `min`/`max` validation doesn't fire reliably inside a hidden wizard step, which is the same reason the native `required` attributes were dropped from the feedback wizard earlier.

Add a short line under the age field so the restriction doesn't look arbitrary:

> Our assessment model currently covers ages 18–60.

### Scope limitation

The model was trained on synthetic data generated within an adult age range of 18–60, so the form restricts input to that range rather than extrapolating beyond what the model has seen. Predicting outside the training range would still return a confident-looking score with no basis behind it.

This is a documented limitation to be addressed during empirical retraining, once real participant data across a wider demographic is available. It also belongs in the manuscript's Limitations section.

Related, and worth raising with the medical validator: the BMI thresholds used here are the adult WHO ranges. Children are assessed against age-and-sex percentile charts instead, so extending the age range later isn't just a matter of widening the input — the BMI handling would need revisiting too.

---

## Phase 4 — Styling

Bring the form in line with `DESIGN.md`:

- `accent-orange-500` on checkboxes and radios → `accent-accent`
- Step indicator, section numbers, and icons → accent
- Card radius → `rounded-xl`
- Consent boxes: keep amber and blue, they're carrying meaning — but use the documented opacity pattern
- Privacy modal: move into `@section Modals` per the modal pattern in `DESIGN.md`, with a `fixed` backdrop
- Submit and Next buttons → brand gradient, capsule, `hover:brightness-110 hover:scale-[1.02]`

Run `npm run build` afterwards.

---

## Phase 5 — Testing

1. Tick "Asthma", then "None of the above" → Asthma clears, the four conditions disable
2. Untick "None of the above" → the four re-enable
3. Tick a condition while "None" is checked → "None" clears and disables
4. Select "First-timer" → Q9 becomes "Never climbed", Q10 becomes "None", both locked
5. Change Q8 to "Experienced" → Q9 and Q10 clear, "Never climbed" and "None" become the disabled options
6. Select Experienced → answer Q9 → go back to First-timer → confirm the old Q9 answer is replaced, not kept
7. Enter age 17 or 61 → blocked with a message naming the field
8. Enter height 50 → blocked
9. Submit a valid form end to end → confirm the assessment saves and the ML prediction returns
10. Reload a partially filled form → dependency state matches the selected Q8

---

## Out of Scope

- Changing the questions themselves — that's pending the expert validation currently with the medical and hiking reviewers
- The BMI formula or thresholds — already correct
- Assessment Report page — separate pass
