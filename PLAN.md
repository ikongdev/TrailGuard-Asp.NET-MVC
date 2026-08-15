# Active Plan — Three-Section Post-Event Feedback

Closes gap #6, found by re-reading the manuscript's functional requirements after the other gaps were done.

---

## What the Manuscript Requires

Functional requirement #16:

> "The system shall allow Participants to submit a **structured post-event feedback form divided into three distinct sections**: (1) personal hiking experience, (2) assessment of the trekked trail conditions, and (3) evaluation of the organizer and overall event management."

The current form has three fields total — an overall star rating, `DifficultyExperience`, and a free-text comment. That covers section 1 only. Sections 2 and 3 don't exist.

Requirement #17 (final label resolution) is already implemented correctly and needs no changes — both available takes the safer label, one available uses that one, neither excludes the record from retraining.

---

## Decisions Already Made

| Question | Decision |
|---|---|
| Do sections 2 and 3 affect the final suitability label? | **No.** Only `DifficultyExperience` feeds the label. Trail condition and organizer quality are different questions from whether *this participant* suited *this trail* — mixing them would corrupt the training signal. Sections 2 and 3 are for records and organizer insight. |
| Are the new sections required? | **Yes, all required.** Most are single-click choices, and complete data is worth more than a marginally shorter form. |
| How many comment fields? | **One**, in the final section, as a general comment for the whole form. |
| Layout | **Multi-step wizard** — one section visible at a time, with Back/Next navigation and a progress indicator. |

### Wizard implementation constraint

The wizard must be **one HTML form with JavaScript show/hide** — not real page navigation. If each step were a separate request, answers from earlier steps would be lost on navigation, and going Back would clear them.

Each step also needs its own validation gate: a participant shouldn't reach step 3 and then discover a missed field back on step 1, where they can no longer see it.

---

## Section Contents

### Section 1 — Personal Hiking Experience

Already exists, keep as is:
- `DifficultyExperience` — the seven radio options (this is what feeds the final label; the exact strings must not change, `FinalLabelService` maps against them)

### Section 2 — Trail Conditions

All new:

| Field | Type | Options |
|---|---|---|
| `TrailCondition` | radio | Well-maintained / Passable / Poorly maintained / Hazardous |
| `TrailSignage` | radio | Clear / Adequate / Confusing / Absent |
| `WaterSourceAvailability` | radio | Available / Limited / None |
| `HazardsEncountered` | text | Optional — the one exception to "all required", since most hikes have none |

### Section 3 — Organizer & Event Management

| Field | Type | Options |
|---|---|---|
| `Rating` | 1–5 stars | Existing field, moved here — it belongs with event evaluation, not hiking experience |
| `PreEventCommunication` | radio | Excellent / Good / Fair / Poor |
| `SafetyManagement` | radio | Excellent / Good / Fair / Poor |
| `GroupManagement` | radio | Excellent / Good / Fair / Poor |
| `Comment` | textarea | Existing field, optional, general comment for the whole form |

---

## Phase 1 — Model & Migration

Add to `Models/EventFeedback.cs`:

```csharp
public string? TrailCondition { get; set; }
public string? TrailSignage { get; set; }
public string? WaterSourceAvailability { get; set; }
public string? HazardsEncountered { get; set; }
public string? PreEventCommunication { get; set; }
public string? SafetyManagement { get; set; }
public string? GroupManagement { get; set; }
```

All nullable at the database level even though the form requires them — existing feedback rows predate these fields, and making them non-nullable would break the migration.

```bash
dotnet ef migrations add AddFeedbackSections
dotnet ef database update
```

---

## Phase 2 — Backend

Update `ParticipantController.SubmitFeedback` to accept and persist the seven new parameters. Keep the existing duplicate-submission guard and the `FinalLabelService.UpsertFinalLabel` call at the end — the upsert still keys off `DifficultyExperience` only, so nothing changes there.

Server-side validation should reject a submission missing any required field rather than trusting the client-side gates.

---

## Phase 3 — UI

Rebuild `Views/Participant/Feedback.cshtml` as a three-step wizard:

- A progress indicator showing which of the three steps is active and which are complete
- One `<form>` containing all fields; JavaScript toggles section visibility
- Back / Next buttons, with Submit replacing Next on the final step
- Next is blocked until every required field in the current section is answered, with a clear message about what's missing
- Moving backward preserves everything already entered

Existing star-rating JS moves to section 3 unchanged.

Run `npm run build` afterwards.

---

## Phase 4 — Organizer-Side Display

The new data is useless if nobody reads it. Surface it where organizers already look at feedback — the records/reports view and any per-event feedback listing — grouped by the three sections rather than as a flat field dump.

Trail condition answers in particular are worth making visible: several participants reporting "Poorly maintained" or "Hazardous" on the same trail is a signal the organizer should act on.

---

## Phase 5 — Testing

1. Submit feedback through all three steps → confirm every field persists
2. Try to advance past step 1 without answering → confirm it's blocked with a clear message
3. Advance to step 3, go Back to step 1 → confirm earlier answers are still there
4. Submit and confirm the `FinalSuitabilityLabel` still resolves from `DifficultyExperience` exactly as before
5. Confirm an older feedback row (created before this change) still displays without errors, with the new fields blank
6. Confirm the duplicate-submission guard still works
7. Confirm the new answers appear in the organizer's view

---

## Out of Scope

- Changing how final labels are computed — requirement #17 is already satisfied
- Aggregating trail condition reports across events into trail-level warnings
- The UI/UX consistency pass
