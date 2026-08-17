# Active Plan — Registration Form

The last participant-side page. Same pattern as the assessment report: it still shows the legacy rule-based score breakdown, and the sidebar is carrying information that belongs on the page the participant just came from.

Read `DESIGN.md` first for colour tokens, radius, and input patterns.

---

## What's Wrong

**The rule-based scoring is back.** The sidebar shows "Score: 31 / 44" and four category bars — the same display just removed from the assessment report, for the same reason: it can contradict the ML result sitting directly above it.

**The sidebar is doing too much.** Four cards: assessment result, category scores, trail demands, event details. The participant arrived here from the assessment report, where they already saw all of this in more detail. Repeating it pushes the actual task — filling in the form — into a narrow column.

**The layout is lopsided.** The left column ends at the medical clearance field while the right column keeps going, leaving a large empty gap. Removing two sidebar cards makes that worse unless the layout changes with it.

**Trail Demands bars have no scale.** "Distance 7 km" with a bar filled to roughly 40% — 40% of what? No maximum is stated, so the bar conveys nothing.

**"Step 2 of 2"** implies a wizard that doesn't exist on this page. It's presumably counting the assessment as step 1, but nothing says so.

---

## Decisions Already Made

| Question | Decision |
|---|---|
| Category Scores card | **Remove** — legacy rule-based, same as the report. |
| Trail Demands card | **Remove** — the event details card already lists distance, elevation, duration, and terrain as plain values, without the meaningless bars. |
| Assessment result card | **Keep**, showing result and confidence, plus a link to the full report. |
| Full SHAP breakdown here | **No** — the participant just came from the report where they saw it. A link back is enough. |
| Contact number fields | Fixed `+63` prefix, participant types the local number starting with 9. |
| Medical clearance upload | Add a clear (×) button, matching My Registrations. |

---

## Phase 1 — Contact Number Inputs

Three fields: `contactNumber`, `emergencyContactNumber`, and the participant's own number pulled from their profile.

Render each as a `+63` prefix attached to the input, with the field itself holding the 10-digit local number (starting with 9). Format as `9XX XXX XXXX` while typing.

On submit, combine the prefix and the digits into the value posted — the controller and database expect a full number, and this shouldn't change what gets stored.

Handle an existing profile number that already includes `+63` or a leading `0`: strip it when populating the field, so the prefix isn't doubled.

---

## Phase 2 — Sidebar

Remove the Category Scores and Trail Demands cards entirely.

The assessment result card becomes:

- Result label, in the shared result colours
- Confidence, capped at 99.9%, one decimal — only when there's an ML prediction
- A link: "View full assessment report →"

When there's no `SuitabilityResult` (the ML service was down and the rule-based fallback produced the result), show the label without confidence. Don't leave an empty space where the percentage would be.

Keep the Event & Trail Details card as it is — it's the one piece of context genuinely useful while filling in the form.

---

## Phase 3 — Layout

With two cards gone, the sidebar is short and the form column is long. Widen the form and narrow the sidebar so the two end closer together.

Also:
- Remove "Step 2 of 2" — there's no wizard on this page
- Add the clear (×) button to the medical clearance file input, matching the pattern in `Views/Registration/MyRegistrations.cshtml`

---

## Phase 4 — Styling

Apply `DESIGN.md`:

- Orange and purple → accent
- Cards → `rounded-xl`
- Inputs → the documented pattern: `bg-surface-card border-gray-700 rounded-xl focus:border-accent focus:ring-1 focus:ring-accent focus:outline-none`
- Submit button → capsule, brand gradient, `hover:brightness-110 hover:scale-[1.02]`
- Cancel and Retake → capsule, secondary treatment
- Replace any hardcoded surface hex with the theme tokens

Run `npm run build` afterwards.

---

## Phase 5 — Testing

1. Typing a contact number produces a correctly formatted `+63 9XX XXX XXXX`, and the submitted value matches what the controller expects
2. A profile number already stored with `+63` or a leading `0` populates the field without doubling the prefix
3. Selecting a medical clearance file shows the × button; clicking it clears the input and hides the button again
4. No score, category bars, or trail demand bars appear anywhere on the page
5. The assessment result card shows confidence for an ML-backed assessment, and just the label when there's none
6. "View full assessment report" opens the correct report
7. A Not Recommended registration still shows both required documents and blocks submission when either is missing
8. Submitting a complete form still creates the registration with status `Pending`

Test 7 matters — the document requirements were built in an earlier pass and shouldn't regress while the layout around them changes.

---

## Out of Scope

- The Feedback page
- Organizer and Admin pages
- `AssessmentResultViewModel` itself — the report now uses its own model, and this page can keep using the shared one as long as the score fields simply aren't displayed
