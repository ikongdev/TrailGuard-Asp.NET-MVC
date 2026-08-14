# Active Plan — Result-Based Registration Workflow

This is gap #1 from `CLAUDE.md`. It's both a missing-feature gap and a **behavioral bug**: the current code hard-blocks "Not Recommended" participants from registering, but the manuscript says they may still submit with additional requirements.

Work through the phases in order. Build and commit after each phase.

---

## Decisions Already Made

These were settled in planning — don't re-litigate them, just implement.

| Question | Decision |
|---|---|
| When is medical clearance required? | Automatically, based on the participant's assessment answers. No organizer toggle. |
| Good Match / Borderline **without** medical conditions | Medical clearance **optional** |
| Good Match / Borderline **with** medical conditions | Medical clearance **required** |
| Not Recommended | Medical clearance **required** regardless of reason, plus a preparation plan |
| Preparation plan format | Free text (single textarea), visible to the organizer during review |
| When can a participant pay? | **Only after the organizer approves** — not at initial submission |
| Payment window | 3 days from approval |
| What if the event is sooner than 3 days out? | `deadline = min(ApprovedAt + 3 days, EventDate)` |
| Expiry mechanism | **Lazy check** — no background service |
| Does an unpaid-but-approved registration hold a slot? | **Yes** — it counts toward capacity |
| Existing registration test data | Wiped clean before starting |

### Status flow

```
Pending                     ← participant submitted, organizer hasn't reviewed
   ↓ organizer approves (sets ApprovedAt + PaymentDeadline)
Awaiting Payment            ← waiting on the PARTICIPANT to upload a receipt
   ↓ participant uploads receipt
For Payment Verification    ← waiting on the ORGANIZER to verify
   ↓ organizer verifies
Accepted                    ← confirmed slot
```

Terminal states: `Rejected`, `Cancelled`, `Voided` (deadline passed without payment).

Two separate waiting states are deliberate — with one shared status nobody can tell whose turn it is to act.

## Decisions Made During Phase 2

| Question | Decision |
|---|---|
| Do post-event flows (`PostEventAssessment`, `EventComparison`) count `For Payment Verification` registrations as having joined the hike? | **No** — only `Accepted` registrations are included. On-site payment disputes are handled by the organizer outside the system. |

**Known limitation (accepted, not fixed):** if an organizer approves a registration on the event date itself or the day before, the computed payment deadline may already be in the past, causing the registration to be voided on the next page load. This is considered acceptable — approving that late is outside normal operating practice, and organizers can coordinate directly with the participant in that case.

---

## Phase 1 — Models & Migration

Add to `Models/EventRegistration.cs`:

```csharp
public string? MedicalClearanceUrl { get; set; }
public string? PreparationPlan { get; set; }
public DateTime? ApprovedAt { get; set; }
public DateTime? PaymentDeadline { get; set; }
public DateTime? PaymentReceiptUploadedAt { get; set; }
public string? DecisionReason { get; set; }
```

`DecisionReason` closes a separate gap: `RegistrationDetails.cshtml` already has a `decisionReason` textarea whose value is sent to the server but never persisted.

Before migrating, wipe registration test data (children first, then parents):

```sql
DELETE FROM "EventFeedbacks";
DELETE FROM "PostEventAssessments";
DELETE FROM "ShapValues";
DELETE FROM "SuitabilityResults";
DELETE FROM "EventRegistrations";
DELETE FROM "Assessments";
```

Leave `Events`, `Trails`, and `Users` intact — that's seeded sample data plus real accounts.

Then:
```bash
dotnet ef migrations add AddRegistrationWorkflowFields
dotnet ef database update
```

---

## Phase 2 — Backend Logic

### 2.1 Document requirement helper

Put this where both the registration controller and views can reach it — a static helper alongside `ShapHelper.cs` is fine.

```
RequiresMedicalClearance(assessment):
    if assessment.Result == "Not Recommended" → true
    if assessment has any medical condition flagged → true
    otherwise → false

RequiresPreparationPlan(assessment):
    assessment.Result == "Not Recommended"
```

"Has any medical condition" means `MedicalConditions` is non-empty and isn't just `"None of the above"`. `AssessmentController.HasCondition()` already does this kind of keyword matching — reuse the same approach rather than inventing a second one.

### 2.2 Lazy expiry checker

A single method that finds `Awaiting Payment` registrations past their `PaymentDeadline` and flips them to `Voided`.

Call it at the **top** of every action that reads registration data, so no stale status is ever displayed and no capacity count is ever wrong:

- `OrganizerController.Index` (dashboard stats)
- `OrganizerController.Registrations`
- `OrganizerController.RegistrationDetails`
- `RegistrationController.MyRegistrations`
- `ParticipantController.Index`
- `ParticipantController.Events` (slot counts)
- `ParticipantController.Details` (slot counts)
- `RecordsController.Index`

Missing any one of these reintroduces the stale-data problem this design is meant to avoid.

### 2.3 Registration submission

In `RegistrationController.Register` (POST):
- Accept `medicalClearance` (IFormFile) and `preparationPlan` (string)
- Validate against the rules in 2.1; reject with a clear message if a required document is missing
- Save the uploaded file the same way `PaymentReceiptUrl` is currently handled
- **Remove** the current `IsPaid = !string.IsNullOrEmpty(receiptUrl)` line and stop accepting a payment receipt at this stage — payment now happens after approval
- Status stays `Pending`

### 2.4 Approval sets the payment window

In `OrganizerController.UpdateRegistrationStatus`, when the status is `Accepted`/approve:
- Set `ApprovedAt = DateTime.Now`
- Set `PaymentDeadline = min(ApprovedAt + 3 days, Event.EventDate)`
- Set status to **`Awaiting Payment`** (not `Accepted` — that now comes later)
- Persist `DecisionReason` from the request

### 2.5 Receipt upload

In `RegistrationController.UpdatePaymentReceipt`:
- Only allow when status is `Awaiting Payment` and the deadline hasn't passed
- Set `PaymentReceiptUploadedAt = DateTime.Now`
- Set status to `For Payment Verification`
- Do **not** set `IsPaid` here — that's the organizer's call

### 2.6 Payment verification (new action)

New organizer action, e.g. `VerifyPayment(int id, bool approved)`:
- Approved → `IsPaid = true`, status `Accepted`
- Rejected → status back to `Awaiting Payment` so they can re-upload (deadline unchanged)

### 2.7 Capacity counting

Wherever `RegisteredCount` is computed, count these statuses: `Pending`, `Awaiting Payment`, `For Payment Verification`, `Accepted`. Exclude `Rejected`, `Cancelled`, `Voided`.

---

## Phase 3 — Participant UI

### 3.1 Unblock "Not Recommended" in `Views/Assessment/Report.cshtml`

Currently there's a disabled button reading "Registration Not Recommended". Replace it with an enabled link to registration, labelled something like "Proceed with Additional Requirements", styled to still signal caution (amber/red rather than the standard gradient).

The existing acknowledgement checkbox gating already covers the "make sure they read it" concern — keep that wired up.

### 3.2 `Views/Registration/Register.cshtml`

Add conditional fields driven by the Phase 2.1 rules:
- Medical clearance file upload — shown always, marked **Required** when the rules say so
- Preparation plan textarea — shown only for Not Recommended, always required there
- **Remove** the payment receipt upload from this page entirely — it moves to `MyRegistrations`

Explain *why* a document is required inline (e.g. "Required because you indicated a pre-existing medical condition") rather than just marking it with an asterisk.

### 3.3 `Views/Registration/MyRegistrations.cshtml`

This page carries the new payment flow:
- `Awaiting Payment` → show the receipt upload plus a visible countdown ("2 days left to upload payment")
- `For Payment Verification` → show "Waiting for organizer to verify your payment", no upload control
- `Voided` → explain why, with the missed deadline date
- Other statuses render as they do now

---

## Phase 4 — Organizer UI

In `Views/Organizer/RegistrationDetails.cshtml`:
- New card showing submitted documents: medical clearance (link/preview, reuse the receipt tooltip pattern) and preparation plan text
- When status is `Awaiting Payment`, show the deadline and remaining time
- When status is `For Payment Verification`, show the receipt with **Verify Payment** / **Reject Payment** buttons
- Keep the existing approve/reject/recommend-alternative controls for `Pending`

In `Views/Organizer/Registrations.cshtml`, make sure the new statuses render with sensible colors — `Awaiting Payment` amber, `For Payment Verification` blue, `Voided` gray.

---

## Phase 5 — Testing

Walk the full path with the Python ML service running:

1. Submit an assessment that yields **Not Recommended** → confirm the report page now allows proceeding
2. Try submitting without a preparation plan → confirm it's rejected with a clear message
3. Submit with both documents → status `Pending`
4. Approve as organizer → status `Awaiting Payment`, `PaymentDeadline` set correctly
5. Check `MyRegistrations` → countdown shows
6. Upload receipt → status `For Payment Verification`
7. Verify as organizer → status `Accepted`, `IsPaid` true
8. **Expiry test:** manually backdate a `PaymentDeadline` in pgAdmin, reload any registration page, confirm it flips to `Voided`
9. Confirm capacity counts exclude voided registrations

---

## Out of Scope Here

These are separate gaps — don't fold them into this work:
- Event completion confirmation
- Persisting final suitability labels from feedback
- `Notes` / `Reminders` fields on `Event`
- Weather risk level and suggested reminder
