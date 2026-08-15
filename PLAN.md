# Active Plan — Event Notes & Weather Risk Fields

Closes gaps #4 and #5 from `CLAUDE.md` together, since they overlap: both touch event-level informational fields, and the reminder concepts need untangling in one pass rather than two.

---

## The Actual Problem

Two separate issues that look bigger than they are:

**1. `Announcements` is already a combined field in practice.** The UI labels it "Notes & Announcements" (create/edit forms) and "Reminders and Announcements" (detail pages), but the model field is just `Announcements`. The name has drifted from the usage.

**2. Weather risk level already exists — it's just buried.** `WeatherService.GetRiskLevel()` computes `Low` / `Moderate` / `Moderate to High` / `High (Thunderstorm)` and is working. But it gets concatenated into one big string that's stored in `WeatherForecastAdvisory`. Because it isn't a field, it can't be queried, filtered, styled, or displayed separately — and the manuscript's data dictionary lists `weather risk level` and `suggested reminder` as distinct event attributes.

So this work is mostly **separating what's already there**, plus adding one genuinely new piece (the weather reminder).

---

## Decisions Already Made

| Question | Decision |
|---|---|
| Notes, announcements, and reminders — separate or combined? | **Combined into one field**, `NotesAndReminders`. Rename the existing `Announcements` column rather than dropping and recreating it, so existing content survives. |
| Deviation from the manuscript? | **Yes, accepted.** The manuscript lists notes, announcements, and reminders separately. The manuscript will be realigned to the system after implementation, not the other way around. Document this. |
| Weather reminder field name | **`WeatherReminder`** — unambiguous that it's both a reminder and weather-scoped, and clearly distinct from `WeatherForecastAdvisory`. |
| Is `WeatherReminder` editable? | **Yes.** The manuscript specifies the organizer "may review or edit before publishing." Generate it as a starting point, let them change it. |
| Weather risk levels | Keep the existing four from `GetRiskLevel()` — don't invent a new scale. |

### Field layout after this work

| Field | Contents | Source |
|---|---|---|
| `WeatherForecastAdvisory` | Forecast details only — temperature, rain chance, wind, last updated | Generated |
| `WeatherRiskLevel` | `Low` / `Moderate` / `Moderate to High` / `High (Thunderstorm)` | Generated |
| `WeatherReminder` | Preparation advice matched to the risk level | Generated, organizer-editable |
| `NotesAndReminders` | Anything the organizer wants participants to know | Organizer only |

---

## Phase 1 — Models & Migration

### `Models/Event.cs`

Rename `Announcements` → `NotesAndReminders` (keep it `string?`), and add:

```csharp
public string? WeatherRiskLevel { get; set; }
public string? WeatherReminder { get; set; }
```

Use `migrationBuilder.RenameColumn` for the rename so existing content is preserved — do not drop and re-add.

Also update `EventCreateModel` and `EventEditModel`, which both carry `Announcements`.

```bash
dotnet ef migrations add RenameAnnouncementsAndAddWeatherFields
dotnet ef database update
```

Verify the migration uses `RenameColumn` before applying it. If it generated a drop + add instead, fix it by hand — that would silently wipe every existing event's content.

---

## Phase 2 — Backend Logic

### 2.1 Restructure `WeatherService`

Right now `GetWeatherForecastAsync` returns one concatenated string. Change it to return a small result object with three parts:

```
WeatherResult
    ForecastDetails   — the existing text minus the risk level line
    RiskLevel         — from the existing GetRiskLevel(), unchanged
    SuggestedReminder — new, derived from RiskLevel
```

Keep `GetRiskLevel`, `GetWeatherDescription`, and `GetWindSpeedDescription` as they are. The failure paths currently return plain strings like "Weather forecast temporarily unavailable" — those should still work, with an empty risk level and reminder.

### 2.2 New: reminder generation

Map risk level to preparation advice. Suggested starting text (the organizer can edit it afterwards, so this only needs to be a reasonable default):

| Risk level | Reminder |
|---|---|
| `Low` | Conditions look favorable. Bring enough water and sun protection, and follow the usual trail safety guidelines. |
| `Moderate` | Rain is possible. Bring a raincoat and waterproof your electronics. Expect slippery sections and allow extra time. |
| `Moderate to High` | Heavy rain expected. Trails may be slippery and river crossings may rise. Bring full rain gear and be ready to turn back if conditions worsen. |
| `High (Thunderstorm)` | Thunderstorms expected. Consider rescheduling. If the event pushes through, avoid exposed ridges and summits, and monitor conditions closely. |

### 2.3 Wire into event create/edit

`EventController` already calls `_weatherService.GetWeatherForecastAsync(trail.Location, eventDate)` when creating an event. Update that call site to populate all three fields.

On edit: regenerate the forecast and risk level, but **do not overwrite `WeatherReminder` if the organizer has already edited it** — that would silently discard their wording. Only fill it when it's empty, or when the risk level has changed (in which case the old reminder no longer matches the conditions).

---

## Phase 3 — UI

### 3.1 Create/Edit forms (`Views/Event/Index.cshtml`)

- Relabel the "Notes & Announcements" textarea to **"Notes & Reminders"**, bound to `NotesAndReminders`
- Add an editable **Weather Reminder** textarea, pre-filled with the generated text, with a short hint that it was generated from the forecast and can be adjusted

### 3.2 Event detail pages

Both `Views/Event/Details.cshtml` (organizer) and `Views/Participant/Details.cshtml` (participant) currently render a "Reminders and Announcements" section from `Announcements`. Update both to:

- Show **Notes & Reminders** from `NotesAndReminders`
- Show the **weather risk level** as a colored badge — green for `Low`, amber for `Moderate`, orange for `Moderate to High`, red for `High (Thunderstorm)` — near the existing weather advisory block
- Show the **weather reminder** below the risk badge

The risk badge is the main visible win here: right now the risk level is a line of text inside a paragraph, easy to skim past. As a badge it's the first thing someone notices about the weather section.

Run `npm run build` afterwards.

---

## Phase 4 — Testing

1. Confirm existing events still show their previous `Announcements` content under the new "Notes & Reminders" heading — nothing lost in the rename
2. Create an event on a trail with clear weather → risk badge shows `Low` in green, reminder matches
3. Create an event where the forecast is worse → badge color and reminder change accordingly
4. Edit a `WeatherReminder`, save, then edit the event again without changing the date → confirm the custom text survives
5. Change the event date so the risk level changes → confirm the reminder regenerates
6. Confirm the participant detail page shows the same risk badge and reminder
7. Confirm an event whose weather lookup fails still saves, with an empty risk level and reminder rather than a crash

---

## Out of Scope

- Refreshing weather data on a schedule — it's generated at create/edit time only
- Notifications when the risk level changes
- The UI/UX consistency pass — still deferred until all gaps are closed
- Realigning the manuscript to match the system — happens after implementation
