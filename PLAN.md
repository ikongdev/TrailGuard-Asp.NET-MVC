# Active Plan — Event Completion & Final Suitability Labels

This is gap #2 from `CLAUDE.md`. It closes the event lifecycle and produces the empirical training data the manuscript's retraining strategy depends on.

Two related pieces of work:
- **Part A** — replace the status dropdown with proper lifecycle actions, record completion metadata
- **Part B** — persist final suitability labels for retraining

Work through the phases in order. Build and commit after each phase.

---

## Why This Matters

The manuscript's Limitations section commits to this:

> "As the platform transitions into live operation, this rule-based training framework will be dynamically replaced and continuously retrained using empirical, human-verified data collected via post-event evaluations."

Right now the final label is computed on the fly in `EventComparison` and thrown away. Without persistence there is no empirical dataset, and the Phase 2 retraining described in the manuscript cannot happen.

---

## Decisions Already Made

Settled during planning — implement these, don't re-open them.

### Part A — Event Completion

| Question | Decision |
|---|---|
| Automatic completion when the event date passes? | **No.** Hiking events have travel time, delays, and multi-day trips — only the organizer knows when it's actually done. |
| Replace the status dropdown? | **Yes.** Explicit action buttons, shown only when applicable. |
| Postponed vs Rescheduled | **One action only — "Reschedule"** (with a new date/time). |
| What does completion record? | `CompletedAt` = `DateTime.Now` when the organizer confirms. This is **"Completion Confirmed At"**, not the hike's end time — the naming matters, don't call it an end time. |
| Non-`Accepted` registrations when an event completes | **Void them** (`Pending`, `Awaiting Payment`, `For Payment Verification` → `Voided`). Reuses the existing status rather than inventing a new one. |
| Reschedule effect on registrations | **Automatic carry-over** for now — registrations stay as they are. Participant re-confirmation comes later with notifications. |

### Part B — Final Suitability Labels

| Question | Decision |
|---|---|
| When is a label finalized? | When feedback arrives — recompute on each participant feedback or organizer post-event assessment submission. |
| Only one side submitted? | Use that one. |
| Both submitted? | Take the **more conservative** (lower) of the two. |
| Neither submitted? | **No record** — excluded from the retraining dataset entirely. |
| Store raw feedback strings? | **Yes**, for audit and so labels can be recomputed if the mapping changes later. They are **not** exported as training features. |
| What is `FinalLabel`? | The **3-class value** (`Good-Match` / `Borderline` / `Not Recommended`), matching the model's output space. |

### Feedback → 3-class mapping

Confirmed against the actual radio values in `Views/Participant/Feedback.cshtml`:

| Feedback string | 3-class |
|---|---|
| `Much easier than expected` | Good-Match |
| `Matched perfectly` | Good-Match |
| `Matched but challenging` | Borderline |
| `Harder than expected` | Borderline |
| `Much harder` | Not Recommended |
| `Could not finish - turned back` | Not Recommended |
| `Could not finish - injured` | Not Recommended |

Conservative ordering (lower = worse) already exists in `OrganizerController.GetConservativeResult` — reuse that ordering, don't write a second one.

## Decisions Made During Phase 2

| Question | Decision |
|---|---|
| When can a participant self-cancel a registration (`RegistrationController.CancelRegistration`)? | Only from `Pending` or `Awaiting Payment`. Once the organizer has approved and payment is underway/complete, logistics are committed — cancellation from that point goes through the organizer directly, outside the system. |

---

## Phase 1 — Models & Migration

### `Models/Event.cs`

```csharp
public DateTime? CompletedAt { get; set; }
public string? CompletedBy { get; set; }
public DateTime? CancelledAt { get; set; }
public string? CancellationReason { get; set; }
```

### New: `Models/FinalSuitabilityLabel.cs`

```csharp
public int Id { get; set; }
public int RegistrationId { get; set; }      // FK, navigation to EventRegistration
public int EventId { get; set; }
public string UserId { get; set; }
public int AssessmentId { get; set; }        // FK — critical, links back to the raw features
public string PreHikeLabel { get; set; }     // the ML prediction at registration time
public string? ParticipantFeedback { get; set; }  // raw string, audit only
public string? OrganizerAssessment { get; set; }  // raw string, audit only
public string FinalLabel { get; set; }       // 3-class
public DateTime ResolvedAt { get; set; }
```

`AssessmentId` is the most important field here — without it there's a label with no features attached, which is useless for training. Add a unique index on `RegistrationId` so a registration can only ever have one final label.

Register the `DbSet` in `ApplicationDbContext`, then:

```bash
dotnet ef migrations add AddEventCompletionAndFinalLabels
dotnet ef database update
```

---

## Phase 2 — Backend Logic

### 2.1 Label resolution service

New service, e.g. `Services/FinalLabelService.cs`:

```
MapFeedbackToClass(feedbackString) → 3-class string
    per the mapping table above; anything unrecognized → null

ResolveFinalLabel(participantFeedback, organizerAssessment) → 3-class or null
    map both (either may be null)
    if both present  → return the more conservative of the two
    if one present   → return that one
    if neither       → return null

UpsertFinalLabel(context, registrationId)
    load the registration with its Assessment
    skip unless status is "Accepted"
    look up participant feedback (EventFeedbacks) and organizer assessment (PostEventAssessments)
    resolve the label
    if null → delete any existing row for this registration, then return
    otherwise → insert or update the row, refreshing ResolvedAt
```

Making this an upsert matters: feedback can arrive in either order, and either side can be edited afterwards (`SubmitPostEventAssessment` already updates in place). The label must always reflect the current inputs.

### 2.2 Call the upsert from both feedback paths

- `ParticipantController.SubmitFeedback` — after saving the feedback
- `OrganizerController.SubmitPostEventAssessment` — after saving the assessment

### 2.3 Event lifecycle actions

Replace the generic status update with three explicit actions on `EventController` (or `OrganizerController` — put them wherever the existing event status update lives):

**`CompleteEvent(int id)`**
- Guard: only from `Upcoming`
- Set `Status = "Completed"`, `CompletedAt = DateTime.Now`, `CompletedBy` = current organizer's name
- Void every registration for the event whose status is `Pending`, `Awaiting Payment`, or `For Payment Verification`

**`CancelEvent(int id, string reason)`**
- Guard: only from `Upcoming`
- Set `Status = "Cancelled"`, `CancelledAt = DateTime.Now`, `CancellationReason = reason`
- Reason is required

**`RescheduleEvent(int id, DateTime newDate, TimeSpan newTime)`**
- Guard: only from `Upcoming`
- Update `EventDate` / `EventTime`, keep `Status = "Upcoming"`
- Registrations carry over untouched

Remove the old dropdown-driven status update endpoint once these are in place.

---

## Phase 3 — Organizer UI

### 3.1 Event Details (`Views/Event/Details.cshtml`)

Replace the status `<select>` with contextual buttons:

- **Upcoming** → "Mark as Completed" (green, confirmation dialog warning that unpaid/unreviewed registrations will be voided), "Reschedule" (blue, modal for new date + time), "Cancel Event" (red, modal requiring a reason)
- **Completed** → no actions; show "Completed on {CompletedAt}" and by whom
- **Cancelled** → no actions; show the cancellation date and reason

The point of this change is that an organizer should see what they can do without opening a menu, and that completing an event should feel like a decision rather than a field edit.

### 3.2 Event Comparison (`Views/Organizer/EventComparison.cshtml`)

This page already shows the pre-hike vs post-hike comparison computed live. Now that labels are persisted, show the stored `FinalLabel` and add a small indicator for rows where no label exists yet (neither side has submitted feedback) so the organizer can see what's still outstanding.

Run `npm run build` after adding any new Tailwind classes.

---

## Phase 4 — Testing

1. Create an event, register participants with a mix of statuses (`Pending`, `Awaiting Payment`, `For Payment Verification`, `Accepted`)
2. Mark the event completed → confirm `CompletedAt`/`CompletedBy` are set and the three non-`Accepted` registrations became `Voided`
3. Submit **participant feedback only** → confirm a `FinalSuitabilityLabel` row appears with the mapped class
4. Submit the **organizer assessment** for the same participant with a *worse* rating → confirm the existing row updates to the more conservative label
5. Edit the organizer assessment to a *better* rating → confirm the label recomputes correctly (this is where a non-upsert implementation breaks)
6. Confirm a participant with **no feedback from either side** has no row at all
7. Cancel a different event with a reason → confirm `CancelledAt`/`CancellationReason` are stored and the buttons disappear
8. Reschedule a third event → confirm the date changes, status stays `Upcoming`, and registrations are untouched
9. Query the table directly and confirm every row has a valid `AssessmentId` pointing at a real assessment

---

## Out of Scope Here

- Notifications on reschedule (participant re-confirmation flow)
- The actual retraining pipeline — this work produces the dataset; consuming it is separate
- `Notes` / `Reminders` fields on `Event` (gap #4)
- Weather risk level and suggested reminder (gap #5)
- The UI/UX consistency pass — deferred until all feature gaps are closed
