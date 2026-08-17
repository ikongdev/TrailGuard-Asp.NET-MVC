# Active Plan — Assessment Report

The last participant-side page. Like the others, most of what turned up is behaviour rather than styling — this page still presents the legacy rule-based scoring alongside the ML result, and the two contradict each other on screen.

Read `DESIGN.md` first for colour tokens, radius, and the modal pattern.

---

## The Contradiction

A real screenshot from the current page:

- Header: **Good-Match** (from the ML model)
- Summary sidebar: **Score 31, Required 32 — ❌ Below requirement** (from rule-based scoring)
- Recommendations: *"Your score of 31 meets the requirement of 32"* — which isn't even internally consistent

Two scoring systems reaching opposite conclusions, presented side by side as though they agree. A panel would notice this immediately, and a participant would just be confused.

The ML result is the one the system acts on. The rule-based scoring survives only as a fallback when the Python service is unreachable — it shouldn't be the page's headline.

---

## Decisions Already Made

| Question | Decision |
|---|---|
| Rule-based scoring display | **Remove entirely** — the donut's 31/44, the four category bars, the threshold comparison, and the score fields in the summary sidebar. |
| What replaces the donut | **ML confidence**, as on the dashboard and My Registrations. |
| SHAP factors | Add a **numerical value** alongside Helped / Reduced. |
| Risk Flags | **Remove.** It's rule-based and can contradict SHAP — the current screenshot flags "Low Cardio" when cardio isn't among the top SHAP factors at all. Anything that actually matters already surfaces there. |
| Recommendations | **Derive from negative SHAP factors** instead of score thresholds. |
| Other Events for You | Use the same corrected logic as the dashboard. |

### Showing SHAP numerically

Raw SHAP values (`-1.836`, `+1.690`) mean nothing to a participant. Convert each factor to its **share of the total absolute impact** across the displayed factors, and show that as a percentage:

> Recency of last hike — Helped · 32%

That reads as "this accounts for 32% of why you got this result", which is what someone actually wants to know. The bar width should match the same percentage so the number and the bar agree.

### Deriving recommendations

Take the SHAP factors with negative impact — the ones working against the result — and turn each into an action:

| Feature | Recommendation |
|---|---|
| `exercise_frequency_score` | Increase how often you exercise each week |
| `continuous_cardio_duration_score` | Build up how long you can sustain cardio without stopping |
| `hiking_experience_score` | Gain experience on easier trails before attempting this one |
| `last_hike_recency_score` | Consider a shorter warm-up hike before this event |
| `hardest_trail_completed_score` | Work up through easier trail types first |
| `gear_score` or any `gear_*` | Complete your gear checklist before the hike |
| `bmi` | General fitness preparation may help |
| Any health flag | Consult a physician before joining a hike of this difficulty |
| Trail-side features | Not actionable by the participant — skip |

Each one should say **why** it's being suggested, e.g. "this was one of the main factors working against your result", so it reads as explanation rather than generic advice.

When every displayed factor is positive, say so plainly instead of padding with filler: "Nothing significant is working against your result for this event."

Trail-side features (distance, elevation, terrain, duration) describe the trail, not the participant — there's no action to take, so they're excluded from recommendations even when negative.

---

## Phase 1 — Controller

`AssessmentController.Report`:

- Drop `TotalScore`, the four category scores, `MaxScore`, `Threshold`, and `RiskFlags` from the view model
- Replace `ComputeRecommendations` with SHAP-derived recommendations per the table above
- Update `GetAlternativeEvents` to match the dashboard's corrected logic: base the target on the **assessed event's difficulty** combined with the result (Good-Match → same level, Borderline → one down, otherwise Easy), fall back **downward** only, and exclude events the participant is already registered for
- Compute each SHAP factor's percentage share of total absolute impact

`ComputeFitnessScore`, `ComputeExperienceScore`, `ComputeHealthScore`, and `ComputeGearScore` **stay** — they feed the ML request. Only the *display* of their output goes. `ComputeRiskFlags` and `GetResult` also stay: `GetResult` is still the fallback when the ML service is down.

---

## Phase 2 — View

Remove:
- The score donut and its 31/44 label
- The Category Scores card
- The Risk Flags card
- Score, threshold, and category rows in the Result Summary sidebar
- The score-vs-threshold progress bar and its "Below requirement" line

Replace the donut with a **confidence donut**, matching the dashboard: capped at 99.9%, one decimal, coloured by result.

The Result Summary sidebar becomes: result, confidence, event, trail, difficulty, date.

In "Why This Result?", add the percentage next to Helped / Reduced and size each bar to match.

Keep Trail Demands, the acknowledgement flow, and the action buttons as they are.

Apply `DESIGN.md` throughout — accent instead of orange and purple, `rounded-xl`, capsule buttons, shared badge colours.

Run `npm run build` afterwards.

---

## Phase 3 — Testing

1. A Good-Match result shows a confidence donut, no 31/44, and no category bars anywhere on the page
2. SHAP factors show a percentage that matches their bar width, and the percentages across displayed factors sum to roughly 100%
3. Recommendations name real negative factors from this participant's own SHAP breakdown, and don't mention scores or thresholds
4. An assessment where every displayed factor is positive shows the "nothing working against you" message rather than filler advice
5. An assessment made while the ML service was down (no `SuitabilityResult`) still renders — result label, no donut, no SHAP panel, no crash
6. Good-Match on a Moderate event lists Moderate alternatives, never Difficult
7. Already-registered events don't appear in alternatives
8. The acknowledgement checkbox still gates the Proceed button

Test 5 matters most — the fallback path is easy to break when removing the rule-based display, since that's exactly the path where rule-based scoring is still what produced the result.

---

## Out of Scope

- Removing the rule-based scoring functions themselves — they're the ML fallback
- The Feedback page
- Organizer and Admin pages
