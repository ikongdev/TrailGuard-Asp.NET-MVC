# Active Plan — Participant Dashboard

Part of the UI/UX pass, but most of this turned out to be behaviour rather than styling. Reviewing each card surfaced logic that was written before the registration workflow changed, plus several numbers that don't mean what their labels claim.

Read `DESIGN.md` first — its colour tokens, radius scale, and hover rules apply throughout.

**Already fixed in an earlier session** (don't redo): the hardcoded weather block is gone from `Index`, and the summary card counts now use `needsAction` and `activeRegistrations`. Everything below is still outstanding.

---

## Decisions Already Made

Settled during planning. Implement these; don't re-open them.

### Weather

| Question | Decision |
|---|---|
| Where does it display? | An expandable row inside Upcoming Events — badge always visible, details on click. The old dropdown card is gone. |
| Fetched when? | **Client-side, after page load**, with a per-row loading state. |
| Requests fired how? | **All at once**, not sequentially. Serialising them just moves the blocking problem to the client. |
| No forecast available? | Show a **"No Forecast Available"** badge. |
| Status badge in the list? | **Removed.** Everything in that list is upcoming by definition, so it said nothing. The weather badge needs a cloud icon so it doesn't read as a status. |

Open-Meteo only forecasts about 16 days out, so an event three weeks away legitimately has no data. That's a normal outcome, not an error — the badge should be muted and explanatory, not red and alarming.

### Upcoming Events list

Only `Accepted` registrations, and only where the event's own status is still `Upcoming`. A `Pending` registration isn't a confirmed hike, and an event the organizer already marked `Completed` shouldn't sit in a list of upcoming ones.

### Latest Assessment

| Question | Decision |
|---|---|
| How is "latest" chosen? | By `SubmittedAt`, not `RegisteredAt` — and only `IsActive` assessments. Retaking an assessment doesn't change when you registered, so the current sort returns stale results. |
| What's in the donut? | **ML confidence**, replacing the 31/44 rule-based score. |
| What else does the card show? | Event title, trail name, event difficulty, submission date, and a link to the full report. |

The 31/44 was the legacy rule-based total, while the label beside it came from XGBoost — two unrelated numbers presented as though they belonged together. Confidence is what actually backs the label.

Not every assessment has an ML prediction: if the Python service was unreachable, the controller fell back to rule-based scoring and no `SuitabilityResult` row was written. Handle that case — show the label without a donut rather than rendering an empty chart.

### Recommended Events

Current logic maps the label directly to a fixed difficulty: Good-Match → Difficult, Borderline → Moderate, Not Recommended → Easy. **This is the bug worth fixing most.** A Good-Match on an Easy trail means you suit that Easy trail — it is not evidence you can handle a Difficult one, but the code recommends Difficult events anyway. That's precisely the mismatch this system exists to prevent.

| Question | Decision |
|---|---|
| What drives the recommendation? | The **difficulty of the assessed event**, combined with the result — not the result alone. |
| Good-Match | **Same level.** Never one level up — the assessment proves you suit that level, nothing beyond it. |
| Borderline | One level down. |
| Not Recommended | Easy. |
| Nothing at the target level? | **Fall back downward** until something is found. Never upward — recommending something harder because nothing easier exists is the wrong failure mode. |
| Already registered? | Excluded. The `userId` parameter is currently passed in and never used. |

`"Technical"` also appears in the difficulty list here. It's dead — `DifficultyCalculator` never returns it. Drop it.

### Progress & Achievements

| Question | Decision |
|---|---|
| Badges Earned | **Removed.** It was `completedHikes` mapped through a lookup table, so the card showed the same number twice under two labels. |
| Replaced with | **Personal bests** from actual completed hikes: highest difficulty completed, longest distance, highest elevation. |
| Top Hiker Rank | A **real position** — "Rank 3 of 47 hikers" — not a fabricated percentile. |
| Outside the top 10? | Show **"Not yet ranked"** with a hint to complete more hikes. |
| No completed hikes? | Also "Not yet ranked". |

The existing rank is a lookup table on your own hike count. "Top 80%" implies a comparison against other users that never happens — you'd get the same string as the only user in the system. Either compute it honestly or don't claim it.

Personal bests are also more useful here than badges: they're drawn from real hiking history and they connect directly to suitability. Having completed a Moderate hike is evidence about what you can handle next.

---

## Phase 1 — Controller

### 1.1 Upcoming events filter

```csharp
var upcomingEvents = registrations
    .Where(r => r.Status == "Accepted")
    .Select(r => r.Event)
    .Where(e => e != null && e.EventDate >= DateTime.Today && e.Status == "Upcoming")
    .ToList();
```

### 1.2 Latest assessment

```csharp
var latestAssessment = registrations
    .Where(r => r.Assessment != null && r.Assessment.IsActive == true)
    .Select(r => r.Assessment)
    .OrderByDescending(a => a!.SubmittedAt)
    .FirstOrDefault();
```

Then find the registration and event that assessment belongs to, and look up its `SuitabilityResult` for the confidence score. Populate the expanded `LatestAssessmentResult` (see Phase 2).

### 1.3 Recommendations

```csharp
private async Task<List<Event>> GetRecommendedEvents(
    string assessmentResult, string assessedDifficulty, string userId)
{
    var levels = new List<string> { "Easy", "Moderate", "Difficult" };

    var currentIndex = levels.IndexOf(assessedDifficulty);
    if (currentIndex < 0) currentIndex = 1;

    var targetIndex = assessmentResult switch
    {
        "Good-Match" => currentIndex,
        "Borderline" => Math.Max(0, currentIndex - 1),
        _ => 0
    };

    var registeredEventIds = await _context.EventRegistrations
        .Where(r => r.UserId == userId && r.Status != "Cancelled" && r.Status != "Rejected")
        .Select(r => r.EventId)
        .ToListAsync();

    for (var i = targetIndex; i >= 0; i--)
    {
        var events = await _context.Events
            .Include(e => e.Trail)
            .Where(e => e.Status == "Upcoming"
                     && e.EventDate >= DateTime.Today
                     && e.Difficulty == levels[i]
                     && !registeredEventIds.Contains(e.Id))
            .OrderBy(e => e.EventDate)
            .Take(4)
            .ToListAsync();

        if (events.Any()) return events;
    }

    return new List<Event>();
}
```

Call it with the assessed event's difficulty, not just the result.

### 1.4 Personal bests and rank

Personal bests come from completed hikes — registrations that are `Accepted` on events with status `Completed`. Take the highest difficulty reached, the longest `Trail.DistanceKm`, and the highest `Trail.ElevationGainMeters`. If there are no completed hikes, all three are unset and the section shows an empty state.

Rank:

```csharp
var hikeCounts = await _context.EventRegistrations
    .Where(r => r.Status == "Accepted" && r.Event!.Status == "Completed")
    .GroupBy(r => r.UserId)
    .Select(g => new { UserId = g.Key, Count = g.Count() })
    .ToListAsync();

var rank = hikeCounts.Count(x => x.Count > completedHikes) + 1;
var totalHikers = hikeCounts.Count;
var isRanked = completedHikes > 0 && rank <= 10;
```

### 1.5 Weather endpoint

```csharp
[HttpGet]
public async Task<IActionResult> GetEventWeather(int eventId)
```

Load the event with its trail; return `success = false` if event, trail, or location is missing. Otherwise call `_weatherService.GetWeatherForecastAsync(trail.Location, ev.EventDate)` and return `success`, `riskLevel`, `details`, `reminder`.

An empty `RiskLevel` means no forecast is available for that date — not an error.

`WeatherService` needs injecting into the controller; it isn't currently.

### 1.6 Remove the dead endpoint

`GetWeatherForecast` served the dropdown that's being removed. Delete it.

---

## Phase 2 — View Model

`LatestAssessmentResult` — drop `TotalScore`, add:

```csharp
public double ConfidenceScore { get; set; }
public bool HasMlPrediction { get; set; }
public int AssessmentId { get; set; }
public int EventId { get; set; }
public string EventTitle { get; set; } = string.Empty;
public string TrailName { get; set; } = string.Empty;
public string EventDifficulty { get; set; } = string.Empty;
```

`ParticipantDashboardViewModel` — remove `WeatherByEvent` (weather is client-side now). Add personal bests and rank fields; drop `BadgesEarned`.

Remove the `WeatherForecast` class if nothing else references it.

---

## Phase 3 — View

### Upcoming Events

Spans two columns (`lg:col-span-2`). Each row renders with a loading placeholder in the badge slot and a collapsed, empty detail panel beneath it. No status badge.

Once a fetch resolves:

| Outcome | Badge |
|---|---|
| Forecast available | Cloud icon, risk level, chevron — coloured by risk, row clickable |
| No forecast | "No Forecast Available", muted grey, no chevron, not clickable |
| Fetch failed | "Forecast Unavailable", muted grey, no chevron |

Risk colours: `Low` emerald, `Moderate` amber, `Moderate to High` orange, `High (Thunderstorm)` red — 15% background opacity with matching text, per the badge pattern in `DESIGN.md`.

The expanded panel shows forecast details, then the reminder below a divider with an accent info icon.

### Latest Assessment

Confidence donut, result label, then event title, trail name, difficulty, date, description, and a link to the full report. When `HasMlPrediction` is false, show the label without the donut.

### Progress & Achievements

Personal bests as a simple list, then rank. Empty state when there are no completed hikes.

### Everything else

- Page heading plain white, not a gradient — the landing hero is the only gradient heading in the app
- All `text-purple-400` icons become `text-accent`
- `rounded-2xl` becomes `rounded-xl`
- Difficulty chips on recommended events follow the documented difficulty colours
- Avatar uses the brand gradient order: `from-orange-500 via-pink-500 to-violet-500`
- Scrollbar thumb uses accent

Run `npm run build` afterwards.

---

## Phase 4 — Testing

1. Dashboard renders immediately, before any forecast has loaded
2. Each row shows its own loading state and resolves independently
3. An event within ~16 days shows a coloured risk badge; clicking expands details and reminder
4. An event further out shows "No Forecast Available" and isn't clickable
5. Stopping the weather API mid-load leaves the page usable, failed rows showing "Forecast Unavailable"
6. Only `Accepted` registrations for `Upcoming` events appear in the list
7. An event marked `Completed` disappears from the list
8. Retaking an assessment updates the Latest Assessment card — this is what the `SubmittedAt` sort fixes
9. An assessment made while the ML service was down shows its label without a donut
10. Good-Match on a Moderate event recommends Moderate events, never Difficult
11. With no events at the target level, recommendations fall back to an easier level
12. Already-registered events don't appear in recommendations
13. Personal bests match the actual completed hikes
14. A user outside the top 10 sees "Not yet ranked"

---

## Out of Scope

- Caching forecasts — revisit if a participant ever has enough concurrent events for the request count to matter
- A **My Profile** page for completed and cancelled hike history — planned, not part of this
- Making "Needs Action" clickable through to a filtered MyRegistrations — would need a filter parameter that doesn't exist yet
- Other participant pages
